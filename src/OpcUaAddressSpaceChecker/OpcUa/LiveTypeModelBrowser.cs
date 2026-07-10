using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace OpcUaAddressSpaceChecker.OpcUa;

/// <summary>
/// Browses the type subtree of a connected OPC UA server (starting from the Types folder,
/// <c>i=86</c> per OPC UA Part 5 §6.3) and materializes the raw type/reference data required to
/// rebuild an instance-declaration type model.
/// </summary>
/// <remarks>
/// Unlike the instance browser, this walk captures the references that the type model depends on but
/// that a plain hierarchical browse omits: <c>HasSubtype</c> (i=45) supertype chains,
/// <c>HasComponent</c>/<c>HasProperty</c>/<c>Organizes</c> member declarations, <c>HasModellingRule</c>
/// (i=37) targets, <c>HasTypeDefinition</c> (i=40) targets, and <c>HasInterface</c> (i=17603) targets.
/// Every decoded array batch is capped by <c>min(OperationLimits.MaxNodesPerXxx, MaxArrayLength)</c>
/// (OPC UA Part 6 §5.2.2 encoding limits) to avoid <c>BadEncodingLimitsExceeded</c>.
/// </remarks>
public sealed class LiveTypeModelBrowser
{
    private const int MaxSearchDepth = 256;
    private const int DefaultMaxNodesPerBrowse = 100;
    private const int DefaultMaxNodesPerRead = 1000;
    private const int DefaultMaxArrayLength = 65535;
    private const uint DefaultMaxReferencesPerNode = 1000;

    private static readonly NodeId HasSubtype = ReferenceTypeIds.HasSubtype;
    private static readonly NodeId HasModellingRule = ReferenceTypeIds.HasModellingRule;
    private static readonly NodeId HasTypeDefinition = ReferenceTypeIds.HasTypeDefinition;
    private static readonly NodeId HasInterface = ReferenceTypeIds.HasInterface;

    private readonly ILogger<LiveTypeModelBrowser> _logger;
    private readonly OpcUaClient _client;

    public LiveTypeModelBrowser(ILogger<LiveTypeModelBrowser> logger, OpcUaClient client)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Browses the server type hierarchy under the Types folder and returns the raw model.
    /// </summary>
    public async Task<LiveTypeModel> FetchTypeModelAsync(CancellationToken cancellationToken = default)
    {
        return await _client.ExecuteWithRetryAsync(async (session, ct) =>
        {
            var stopwatch = Stopwatch.StartNew();
            var discovered = new Dictionary<NodeId, LiveTypeModelNode>();
            var supertypes = new Dictionary<NodeId, NodeId>();
            var rootNodeId = ObjectIds.TypesFolder;

            discovered[rootNodeId] = new LiveTypeModelNode { NodeId = rootNodeId };

            var browseBatchSize = GetBrowseBatchSize(session);
            _logger.LogInformation(
                "Browsing live type model from {RootNode} (batch size {BatchSize})...",
                rootNodeId,
                browseBatchSize);

            // Pass 1: walk hierarchical references (HasSubtype + Aggregates + Organizes + HasInterface)
            // forward to discover every type node and instance-declaration member.
            await BrowseTypeHierarchyAsync(session, discovered, supertypes, browseBatchSize, ct)
                .ConfigureAwait(false);

            foreach (var (targetId, superTypeId) in supertypes)
            {
                if (discovered.TryGetValue(targetId, out var node))
                {
                    node.SuperTypeId = superTypeId;
                }
            }

            // Pass 2: read the non-hierarchical modelling-rule and type-definition targets that the
            // hierarchical walk does not return.
            await BrowseNonHierarchicalReferencesAsync(session, discovered, browseBatchSize, ct)
                .ConfigureAwait(false);

            // Pass 3: read the value-shape attributes needed to reconstruct variable declarations.
            await ReadNodeAttributesAsync(session, discovered, GetReadBatchSize(session), ct)
                .ConfigureAwait(false);

            var namespaceUris = CloneNamespaceTable(session.NamespaceUris);
            var nodes = discovered.Values
                .OrderBy(node => node.NodeId.ToString(), StringComparer.Ordinal)
                .ToArray();

            stopwatch.Stop();
            _logger.LogInformation(
                "Fetched {Count} type-model node(s) in {Duration}ms.",
                nodes.Length,
                stopwatch.ElapsedMilliseconds);

            return new LiveTypeModel
            {
                Nodes = nodes,
                NamespaceUris = namespaceUris
            };
        }, "FetchTypeModel", cancellationToken).ConfigureAwait(false);
    }

    private async Task BrowseTypeHierarchyAsync(
        ISession session,
        Dictionary<NodeId, LiveTypeModelNode> discovered,
        Dictionary<NodeId, NodeId> supertypes,
        int browseBatchSize,
        CancellationToken cancellationToken)
    {
        var queued = new Queue<NodeId>();
        var queuedOrVisited = new HashSet<NodeId> { ObjectIds.TypesFolder };
        queued.Enqueue(ObjectIds.TypesFolder);

        for (var depth = 0; queued.Count > 0 && depth < MaxSearchDepth; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var level = DrainQueue(queued);

            var references = await BrowseAdaptiveAsync(
                session,
                level,
                ReferenceTypeIds.HierarchicalReferences,
                browseBatchSize,
                cancellationToken).ConfigureAwait(false);

            foreach (var reference in references)
            {
                var target = GetOrCreate(discovered, reference.TargetId);
                target.BrowseName = reference.BrowseName;
                target.DisplayName = reference.DisplayName;
                target.NodeClass = reference.NodeClass;
                if (reference.TypeDefinitionId != null)
                {
                    target.TypeDefinitionId ??= reference.TypeDefinitionId;
                }

                ClassifyReference(discovered, supertypes, reference);

                if (queuedOrVisited.Add(reference.TargetId))
                {
                    queued.Enqueue(reference.TargetId);
                }
            }
        }

        if (queued.Count > 0)
        {
            _logger.LogWarning(
                "Stopped type-model browse after reaching maximum depth {MaxDepth}; {Remaining} nodes were not expanded.",
                MaxSearchDepth,
                queued.Count);
        }
    }

    private static void ClassifyReference(
        Dictionary<NodeId, LiveTypeModelNode> discovered,
        Dictionary<NodeId, NodeId> supertypes,
        RawReference reference)
    {
        if (reference.ReferenceTypeId == HasSubtype)
        {
            // The forward HasSubtype target derives from the source: record the supertype edge.
            supertypes[reference.TargetId] = reference.SourceId;
            return;
        }

        if (reference.ReferenceTypeId == HasInterface)
        {
            var source = GetOrCreate(discovered, reference.SourceId);
            if (!source.InterfaceIds.Contains(reference.TargetId))
            {
                source.InterfaceIds.Add(reference.TargetId);
            }

            return;
        }

        // A hierarchical reference to another type node (e.g. a folder Organizes a base type) only
        // continues the type walk; it is not an instance declaration.
        if (IsTypeNodeClass(reference.NodeClass))
        {
            return;
        }

        // Any remaining hierarchical reference to an Object/Variable/Method is an instance-declaration
        // member of the source node.
        if (reference.NodeClass is NodeClass.Object or NodeClass.Variable or NodeClass.Method)
        {
            var source = GetOrCreate(discovered, reference.SourceId);
            source.Children.Add(new LiveTypeChild(reference.ReferenceTypeId, reference.TargetId));
        }
    }

    private async Task BrowseNonHierarchicalReferencesAsync(
        ISession session,
        Dictionary<NodeId, LiveTypeModelNode> discovered,
        int browseBatchSize,
        CancellationToken cancellationToken)
    {
        var nodeIds = discovered.Keys.ToArray();
        var references = await BrowseAdaptiveAsync(
            session,
            nodeIds,
            ReferenceTypeIds.NonHierarchicalReferences,
            browseBatchSize,
            cancellationToken).ConfigureAwait(false);

        foreach (var reference in references)
        {
            if (!discovered.TryGetValue(reference.SourceId, out var source))
            {
                continue;
            }

            if (reference.ReferenceTypeId == HasModellingRule)
            {
                source.ModellingRuleId ??= reference.TargetId;
            }
            else if (reference.ReferenceTypeId == HasTypeDefinition)
            {
                source.TypeDefinitionId ??= reference.TargetId;
            }
        }
    }

    private async Task ReadNodeAttributesAsync(
        ISession session,
        Dictionary<NodeId, LiveTypeModelNode> discovered,
        int initialBatchSize,
        CancellationToken cancellationToken)
    {
        var nodeIds = discovered.Keys.ToArray();
        var requests = new List<ReadRequest>(nodeIds.Length * AttributeIds.Length);

        foreach (var nodeId in nodeIds)
        {
            foreach (var attributeId in AttributeIds)
            {
                requests.Add(new ReadRequest(nodeId, attributeId));
            }
        }

        var batchSize = Math.Max(1, initialBatchSize);
        var offset = 0;

        while (offset < requests.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = Math.Min(batchSize, requests.Count - offset);
            var batch = requests.Skip(offset).Take(take).ToArray();

            try
            {
                var values = await ReadBatchOnceAsync(session, batch, cancellationToken).ConfigureAwait(false);
                for (var i = 0; i < batch.Length; i++)
                {
                    if (discovered.TryGetValue(batch[i].NodeId, out var node))
                    {
                        ApplyAttribute(node, batch[i].AttributeId, values[i]);
                    }
                }

                offset += take;
            }
            catch (Exception ex) when (IsBadEncodingLimitsExceeded(ex) && batchSize > 1)
            {
                batchSize = Math.Max(1, batchSize / 2);
                _logger.LogWarning(
                    "Type-model read batch exceeded OPC UA encoding limits; retrying with batch size {BatchSize}.",
                    batchSize);
            }
        }
    }

    private async Task<List<RawReference>> BrowseAdaptiveAsync(
        ISession session,
        IReadOnlyList<NodeId> nodeIds,
        NodeId referenceTypeId,
        int initialBatchSize,
        CancellationToken cancellationToken)
    {
        var references = new List<RawReference>();
        var batchSize = Math.Max(1, initialBatchSize);
        var offset = 0;

        while (offset < nodeIds.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = Math.Min(batchSize, nodeIds.Count - offset);
            var batch = nodeIds.Skip(offset).Take(take).ToArray();

            try
            {
                references.AddRange(await BrowseBatchOnceAsync(session, batch, referenceTypeId, cancellationToken)
                    .ConfigureAwait(false));
                offset += take;
            }
            catch (Exception ex) when (IsBadEncodingLimitsExceeded(ex) && batchSize > 1)
            {
                batchSize = Math.Max(1, batchSize / 2);
                _logger.LogWarning(
                    "Type-model browse batch exceeded OPC UA encoding limits; retrying with batch size {BatchSize}.",
                    batchSize);
            }
        }

        return references;
    }

    private async Task<List<RawReference>> BrowseBatchOnceAsync(
        ISession session,
        IReadOnlyList<NodeId> nodeIds,
        NodeId referenceTypeId,
        CancellationToken cancellationToken)
    {
        var descriptions = new BrowseDescriptionCollection(
            nodeIds.Select(nodeId => CreateForwardBrowseDescription(nodeId, referenceTypeId)));
        var response = await session.BrowseAsync(
            requestHeader: null,
            view: null,
            requestedMaxReferencesPerNode: GetRequestedMaxReferencesPerNode(),
            nodesToBrowse: descriptions,
            ct: cancellationToken).ConfigureAwait(false);

        var references = new List<RawReference>();
        var continuations = new List<(NodeId SourceId, byte[] ContinuationPoint)>();

        for (var i = 0; i < nodeIds.Count; i++)
        {
            var result = response.Results[i];
            ThrowIfEncodingLimitResult(result.StatusCode);

            if (StatusCode.IsBad(result.StatusCode))
            {
                _logger.LogDebug("Browse of {NodeId} returned {StatusCode}", nodeIds[i], result.StatusCode);
                continue;
            }

            AddReferences(nodeIds[i], result.References, references, session.NamespaceUris);
            if (HasContinuationPoint(result))
            {
                continuations.Add((nodeIds[i], result.ContinuationPoint));
            }
        }

        while (continuations.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var continuationPoints = new ByteStringCollection(continuations.Select(x => x.ContinuationPoint));
            var continuationResponse = await session.BrowseNextAsync(
                requestHeader: null,
                releaseContinuationPoints: false,
                continuationPoints: continuationPoints,
                ct: cancellationToken).ConfigureAwait(false);

            var nextContinuations = new List<(NodeId SourceId, byte[] ContinuationPoint)>();
            for (var i = 0; i < continuations.Count; i++)
            {
                var result = continuationResponse.Results[i];
                var sourceId = continuations[i].SourceId;
                ThrowIfEncodingLimitResult(result.StatusCode);

                if (StatusCode.IsBad(result.StatusCode))
                {
                    _logger.LogDebug("BrowseNext of {NodeId} returned {StatusCode}", sourceId, result.StatusCode);
                    continue;
                }

                AddReferences(sourceId, result.References, references, session.NamespaceUris);
                if (HasContinuationPoint(result))
                {
                    nextContinuations.Add((sourceId, result.ContinuationPoint));
                }
            }

            continuations = nextContinuations;
        }

        return references;
    }

    private static BrowseDescription CreateForwardBrowseDescription(NodeId nodeId, NodeId referenceTypeId)
    {
        return new BrowseDescription
        {
            NodeId = nodeId,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = referenceTypeId,
            IncludeSubtypes = true,
            NodeClassMask = 0,
            ResultMask = (uint)BrowseResultMask.All
        };
    }

    private static void AddReferences(
        NodeId sourceId,
        ReferenceDescriptionCollection references,
        List<RawReference> target,
        NamespaceTable namespaceUris)
    {
        foreach (var reference in references)
        {
            var targetId = ExpandedNodeId.ToNodeId(reference.NodeId, namespaceUris);
            if (targetId == null || NodeId.IsNull(targetId))
            {
                continue;
            }

            var referenceTypeId = ExpandedNodeId.ToNodeId(reference.ReferenceTypeId, namespaceUris)
                ?? ReferenceTypeIds.References;
            var typeDefinitionId = ExpandedNodeId.ToNodeId(reference.TypeDefinition, namespaceUris);

            target.Add(new RawReference(
                sourceId,
                referenceTypeId,
                targetId,
                reference.BrowseName,
                reference.DisplayName,
                reference.NodeClass,
                NodeId.IsNull(typeDefinitionId) ? null : typeDefinitionId));
        }
    }

    private static async Task<DataValue[]> ReadBatchOnceAsync(
        ISession session,
        IReadOnlyList<ReadRequest> requests,
        CancellationToken cancellationToken)
    {
        var nodesToRead = new ReadValueIdCollection(requests.Select(request => new ReadValueId
        {
            NodeId = request.NodeId,
            AttributeId = request.AttributeId
        }));

        var response = await session.ReadAsync(
            requestHeader: null,
            maxAge: 0,
            timestampsToReturn: TimestampsToReturn.Neither,
            nodesToRead: nodesToRead,
            ct: cancellationToken).ConfigureAwait(false);

        return response.Results.ToArray();
    }

    private static void ApplyAttribute(LiveTypeModelNode target, uint attributeId, DataValue value)
    {
        if (StatusCode.IsBad(value.StatusCode))
        {
            return;
        }

        var raw = value.WrappedValue.Value;
        switch (attributeId)
        {
            case Attributes.NodeClass:
                target.NodeClass = raw switch
                {
                    NodeClass nodeClass => nodeClass,
                    int nodeClassValue => (NodeClass)nodeClassValue,
                    uint nodeClassValue => (NodeClass)nodeClassValue,
                    _ => target.NodeClass
                };
                break;
            case Attributes.BrowseName:
                if (raw is QualifiedName browseName)
                {
                    target.BrowseName = browseName;
                }
                break;
            case Attributes.DisplayName:
                if (raw is LocalizedText displayName)
                {
                    target.DisplayName = displayName;
                }
                break;
            case Attributes.DataType:
                if (raw is NodeId dataType && !NodeId.IsNull(dataType))
                {
                    target.DataType = dataType;
                }
                break;
            case Attributes.ValueRank:
                if (raw is int valueRank)
                {
                    target.ValueRank = valueRank;
                }
                break;
            case Attributes.ArrayDimensions:
                target.ArrayDimensions = raw switch
                {
                    uint[] dimensions => dimensions,
                    UInt32Collection dimensions => dimensions.ToArray(),
                    _ => target.ArrayDimensions
                };
                break;
        }
    }

    private static LiveTypeModelNode GetOrCreate(Dictionary<NodeId, LiveTypeModelNode> discovered, NodeId nodeId)
    {
        if (!discovered.TryGetValue(nodeId, out var node))
        {
            node = new LiveTypeModelNode { NodeId = nodeId };
            discovered[nodeId] = node;
        }

        return node;
    }

    private static List<NodeId> DrainQueue(Queue<NodeId> queue)
    {
        var count = queue.Count;
        var result = new List<NodeId>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(queue.Dequeue());
        }

        return result;
    }

    private static bool IsTypeNodeClass(NodeClass nodeClass) =>
        nodeClass is NodeClass.ObjectType or NodeClass.VariableType or NodeClass.DataType or NodeClass.ReferenceType;

    private static NamespaceTable CloneNamespaceTable(NamespaceTable source)
    {
        var table = new NamespaceTable();
        for (var i = 0; i < source.Count; i++)
        {
            table.GetIndexOrAppend(source.GetString((uint)i));
        }

        return table;
    }

    private static int GetBrowseBatchSize(ISession session)
    {
        var operationLimit = session.OperationLimits?.MaxNodesPerBrowse ?? 0;
        return CapBatchSize(operationLimit, DefaultMaxNodesPerBrowse);
    }

    private static int GetReadBatchSize(ISession session)
    {
        var operationLimit = session.OperationLimits?.MaxNodesPerRead ?? 0;
        return CapBatchSize(operationLimit, DefaultMaxNodesPerRead);
    }

    private static int CapBatchSize(uint operationLimit, int fallback)
    {
        // OPC UA Part 6 encoding limits apply to every decoded array, including service request arrays.
        var effectiveOperationLimit = operationLimit > 0 ? (int)Math.Min(operationLimit, int.MaxValue) : fallback;
        return Math.Max(1, Math.Min(effectiveOperationLimit, DefaultMaxArrayLength));
    }

    private static uint GetRequestedMaxReferencesPerNode() =>
        Math.Min(DefaultMaxReferencesPerNode, DefaultMaxArrayLength);

    private static bool HasContinuationPoint(BrowseResult result) =>
        result.ContinuationPoint is { Length: > 0 };

    private static void ThrowIfEncodingLimitResult(StatusCode statusCode)
    {
        if (statusCode.Code == StatusCodes.BadEncodingLimitsExceeded)
        {
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
        }
    }

    private static bool IsBadEncodingLimitsExceeded(Exception exception) =>
        exception is ServiceResultException serviceResultException &&
        serviceResultException.StatusCode == StatusCodes.BadEncodingLimitsExceeded;

    private static readonly uint[] AttributeIds =
    [
        Attributes.NodeClass,
        Attributes.BrowseName,
        Attributes.DisplayName,
        Attributes.DataType,
        Attributes.ValueRank,
        Attributes.ArrayDimensions
    ];

    private sealed record RawReference(
        NodeId SourceId,
        NodeId ReferenceTypeId,
        NodeId TargetId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeClass NodeClass,
        NodeId? TypeDefinitionId);

    private sealed record ReadRequest(NodeId NodeId, uint AttributeId);
}

/// <summary>
/// Raw type model browsed from a live OPC UA server, ready to be materialized into a NodeState model.
/// </summary>
public sealed class LiveTypeModel
{
    public required IReadOnlyList<LiveTypeModelNode> Nodes { get; init; }
    public required NamespaceTable NamespaceUris { get; init; }
}

/// <summary>
/// A single node discovered while browsing the server type hierarchy.
/// </summary>
public sealed class LiveTypeModelNode
{
    public required NodeId NodeId { get; init; }
    public QualifiedName BrowseName { get; set; } = QualifiedName.Null;
    public LocalizedText DisplayName { get; set; } = LocalizedText.Null;
    public NodeClass NodeClass { get; set; } = NodeClass.Unspecified;
    public NodeId? SuperTypeId { get; set; }
    public NodeId? TypeDefinitionId { get; set; }
    public NodeId? ModellingRuleId { get; set; }
    public NodeId? DataType { get; set; }
    public int? ValueRank { get; set; }
    public IReadOnlyList<uint> ArrayDimensions { get; set; } = [];
    public List<NodeId> InterfaceIds { get; } = [];
    public List<LiveTypeChild> Children { get; } = [];
}

/// <summary>
/// A forward hierarchical instance-declaration member edge from a parent node to a child node.
/// </summary>
public sealed record LiveTypeChild(NodeId ReferenceTypeId, NodeId ChildId);
