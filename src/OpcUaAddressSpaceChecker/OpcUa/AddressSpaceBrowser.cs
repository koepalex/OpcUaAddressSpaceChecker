using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace OpcUaAddressSpaceChecker.OpcUa;

/// <summary>
/// Browses a connected OPC UA server and materializes the live instance tree.
/// </summary>
public sealed class AddressSpaceBrowser
{
    private const int MaxSearchDepth = 256;
    private const int DefaultMaxNodesPerBrowse = 100;
    private const int DefaultMaxNodesPerRead = 1000;
    private const int DefaultMaxArrayLength = 65535;
    private const uint DefaultMaxReferencesPerNode = 1000;

    private readonly ILogger<AddressSpaceBrowser> _logger;
    private readonly OpcUaClient _client;

    public AddressSpaceBrowser(ILogger<AddressSpaceBrowser> logger, OpcUaClient client)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Fetches all live instance nodes reachable from the OPC UA Objects folder together with a
    /// concrete absolute BrowsePath per node (see <see cref="AddressSpaceSnapshot"/>).
    /// </summary>
    public async Task<AddressSpaceSnapshot> FetchAllNodesAsync(CancellationToken cancellationToken = default)
    {
        return await _client.ExecuteWithRetryAsync(async (session, ct) =>
        {
            var stopwatch = Stopwatch.StartNew();
            var discovered = new Dictionary<NodeId, DiscoveredNode>();
            var relationships = new List<RawReference>();
            var browseStatusCodes = new Dictionary<NodeId, StatusCode>();
            var queued = new Queue<NodeId>();
            var queuedOrVisited = new HashSet<NodeId>();
            var rootNodeId = ObjectIds.ObjectsFolder;

            discovered[rootNodeId] = new DiscoveredNode(rootNodeId);
            queued.Enqueue(rootNodeId);
            queuedOrVisited.Add(rootNodeId);

            var browseBatchSize = GetBrowseBatchSize(session);
            _logger.LogInformation("Browsing live address space from {RootNode} (batch size {BatchSize})...", rootNodeId, browseBatchSize);

            for (var depth = 0; queued.Count > 0 && depth < MaxSearchDepth; depth++)
            {
                ct.ThrowIfCancellationRequested();
                var level = DrainQueue(queued);

                _logger.LogDebug("Browse depth {Depth}: {Count} nodes queued", depth + 1, level.Count);

                var references = await BrowseHierarchicalReferencesAdaptiveAsync(
                    session,
                    level,
                    browseBatchSize,
                    browseStatusCodes,
                    ct).ConfigureAwait(false);

                foreach (var reference in references)
                {
                    relationships.Add(reference);

                    if (!discovered.TryGetValue(reference.TargetId, out var target))
                    {
                        target = new DiscoveredNode(reference.TargetId);
                        discovered[reference.TargetId] = target;
                    }

                    target.BrowseName = reference.BrowseName;
                    target.DisplayName = reference.DisplayName;
                    target.NodeClass = reference.NodeClass;
                    target.TypeDefinitionId ??= reference.TypeDefinitionId;

                    if (queuedOrVisited.Add(reference.TargetId))
                    {
                        queued.Enqueue(reference.TargetId);
                    }
                }
            }

            if (queued.Count > 0)
            {
                _logger.LogWarning(
                    "Stopped browse after reaching maximum depth {MaxDepth}; {Remaining} nodes were not expanded.",
                    MaxSearchDepth,
                    queued.Count);
            }

            var attributes = await ReadNodeAttributesAdaptiveAsync(
                session,
                discovered.Keys.ToArray(),
                GetReadBatchSize(session),
                ct).ConfigureAwait(false);

            var nodes = MaterializeNodes(discovered, relationships, attributes, browseStatusCodes);
            var browsePaths = BuildBrowsePaths(
                nodes,
                relationships.Select(relationship => (relationship.SourceId, relationship.TargetId)).ToArray(),
                rootNodeId);

            stopwatch.Stop();
            _logger.LogInformation("Fetched {Count} live nodes in {Duration}ms.", nodes.Count, stopwatch.ElapsedMilliseconds);

            return new AddressSpaceSnapshot(nodes, browsePaths);
        }, "FetchAllNodes", cancellationToken).ConfigureAwait(false);
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

    private async Task<List<RawReference>> BrowseHierarchicalReferencesAdaptiveAsync(
        ISession session,
        IReadOnlyList<NodeId> nodeIds,
        int initialBatchSize,
        IDictionary<NodeId, StatusCode> browseStatusCodes,
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
                references.AddRange(await BrowseBatchOnceAsync(
                    session,
                    batch,
                    browseStatusCodes,
                    cancellationToken).ConfigureAwait(false));
                offset += take;
            }
            catch (Exception ex) when (IsBadEncodingLimitsExceeded(ex) && batchSize > 1)
            {
                batchSize = Math.Max(1, batchSize / 2);
                _logger.LogWarning(
                    "Browse batch exceeded OPC UA encoding limits; retrying with batch size {BatchSize}.",
                    batchSize);
            }
        }

        return references;
    }

    private async Task<List<RawReference>> BrowseBatchOnceAsync(
        ISession session,
        IReadOnlyList<NodeId> nodeIds,
        IDictionary<NodeId, StatusCode> browseStatusCodes,
        CancellationToken cancellationToken)
    {
        var descriptions = new BrowseDescriptionCollection(nodeIds.Select(CreateHierarchicalBrowseDescription));
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
            browseStatusCodes[nodeIds[i]] = result.StatusCode;

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
                    browseStatusCodes[sourceId] = result.StatusCode;
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

    private static BrowseDescription CreateHierarchicalBrowseDescription(NodeId nodeId)
    {
        return new BrowseDescription
        {
            NodeId = nodeId,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
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

            var referenceTypeId = ExpandedNodeId.ToNodeId(reference.ReferenceTypeId, namespaceUris) ?? ReferenceTypeIds.HierarchicalReferences;
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

    private async Task<IReadOnlyDictionary<NodeId, IReadOnlyDictionary<uint, DataValue>>> ReadNodeAttributesAdaptiveAsync(
        ISession session,
        IReadOnlyList<NodeId> nodeIds,
        int initialBatchSize,
        CancellationToken cancellationToken)
    {
        var attributes = new Dictionary<NodeId, Dictionary<uint, DataValue>>();
        var requests = new List<ReadRequest>(nodeIds.Count * AttributeIds.Length);

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
                    if (!attributes.TryGetValue(batch[i].NodeId, out var nodeAttributes))
                    {
                        nodeAttributes = [];
                        attributes[batch[i].NodeId] = nodeAttributes;
                    }

                    nodeAttributes[batch[i].AttributeId] = values[i];
                }

                offset += take;
            }
            catch (Exception ex) when (IsBadEncodingLimitsExceeded(ex) && batchSize > 1)
            {
                batchSize = Math.Max(1, batchSize / 2);
                _logger.LogWarning(
                    "Read batch exceeded OPC UA encoding limits; retrying with batch size {BatchSize}.",
                    batchSize);
            }
        }

        return attributes.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyDictionary<uint, DataValue>)entry.Value);
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

    /// <summary>
    /// Builds a concrete absolute BrowsePath (formatted as <c>namespaceIndex:BrowseName</c>
    /// segments joined by <c>/</c>) for every discovered node by walking its first-discovered
    /// (shortest-path) hierarchical parent chain up to, but excluding, the Objects root. Because the
    /// path is built from concrete BrowseNames, placeholder InstanceDeclaration segments (e.g.
    /// <c>0:&lt;OrderedObject&gt;</c>) are naturally replaced by the fulfilling instance names.
    /// </summary>
    internal static IReadOnlyDictionary<NodeId, string> BuildBrowsePaths(
        IReadOnlyList<LiveNode> nodes,
        IReadOnlyCollection<(NodeId SourceId, NodeId TargetId)> edges,
        NodeId rootNodeId)
    {
        var nodesById = nodes.ToDictionary(node => node.NodeId);

        var parentById = new Dictionary<NodeId, NodeId>();
        foreach (var edge in edges)
        {
            if (NodeId.IsNull(edge.TargetId) ||
                edge.TargetId == rootNodeId ||
                edge.SourceId == edge.TargetId)
            {
                continue;
            }

            // First occurrence wins: edges are appended in breadth-first order, so the first parent
            // seen for a target is on a shortest path from the root.
            parentById.TryAdd(edge.TargetId, edge.SourceId);
        }

        var pathsById = new Dictionary<NodeId, string>();
        foreach (var node in nodes)
        {
            if (node.NodeId == rootNodeId)
            {
                continue;
            }

            var segments = new List<string>();
            var visited = new HashSet<NodeId>();
            var current = node.NodeId;
            while (current is not null && current != rootNodeId && visited.Add(current))
            {
                if (!nodesById.TryGetValue(current, out var currentNode))
                {
                    break;
                }

                segments.Add(FormatSegment(currentNode));

                if (!parentById.TryGetValue(current, out var parent))
                {
                    break;
                }

                current = parent;
            }

            if (segments.Count > 0)
            {
                segments.Reverse();
                pathsById[node.NodeId] = string.Join("/", segments);
            }
        }

        return pathsById;
    }

    private static string FormatSegment(LiveNode node) =>
        string.IsNullOrEmpty(node.BrowseName.Name)
            ? node.NodeId.ToString()
            : $"{node.BrowseName.NamespaceIndex}:{node.BrowseName.Name}";

    private static List<LiveNode> MaterializeNodes(
        IReadOnlyDictionary<NodeId, DiscoveredNode> discovered,
        IReadOnlyCollection<RawReference> relationships,
        IReadOnlyDictionary<NodeId, IReadOnlyDictionary<uint, DataValue>> attributes,
        IReadOnlyDictionary<NodeId, StatusCode> browseStatusCodes)
    {
        var nodes = new Dictionary<NodeId, LiveNode>();

        foreach (var item in discovered.Values)
        {
            attributes.TryGetValue(item.NodeId, out var nodeAttributes);
            browseStatusCodes.TryGetValue(item.NodeId, out var browseStatusCode);
            nodes[item.NodeId] = MaterializeNode(
                item.NodeId,
                item.BrowseName,
                item.DisplayName,
                item.NodeClass,
                item.TypeDefinitionId,
                nodeAttributes,
                browseStatusCodes.ContainsKey(item.NodeId) ? browseStatusCode : null);
        }

        foreach (var relationship in relationships)
        {
            if (!nodes.TryGetValue(relationship.SourceId, out var source) ||
                !nodes.TryGetValue(relationship.TargetId, out var child))
            {
                continue;
            }

            source.ForwardHierarchicalReferences.Add(new LiveReference(
                relationship.ReferenceTypeId,
                relationship.TargetId,
                relationship.BrowseName,
                relationship.DisplayName,
                relationship.NodeClass));
            source.Children.Add(child);
        }

        var result = nodes.Values.ToList();
        result.Sort((x, y) => string.CompareOrdinal(x.NodeId.ToString(), y.NodeId.ToString()));
        return result;
    }

    internal static LiveNode MaterializeNode(
        NodeId nodeId,
        QualifiedName browsedBrowseName,
        LocalizedText browsedDisplayName,
        NodeClass browsedNodeClass,
        NodeId? typeDefinitionId,
        IReadOnlyDictionary<uint, DataValue>? attributes,
        StatusCode? browseStatusCode)
    {
        var statusCodes = attributes?.ToDictionary(entry => entry.Key, entry => entry.Value.StatusCode)
            ?? new Dictionary<uint, StatusCode>();

        return new LiveNode
        {
            NodeId = nodeId,
            BrowseName = ReadGoodValue<QualifiedName>(attributes, Attributes.BrowseName) ?? browsedBrowseName,
            DisplayName = ReadGoodValue<LocalizedText>(attributes, Attributes.DisplayName) ?? browsedDisplayName,
            NodeClass = ReadNodeClass(attributes) ?? browsedNodeClass,
            TypeDefinitionId = typeDefinitionId,
            DataType = ReadGoodValue<NodeId>(attributes, Attributes.DataType),
            ValueRank = ReadInt32Value(attributes, Attributes.ValueRank),
            ArrayDimensions = ReadArrayDimensions(attributes),
            BrowseStatusCode = browseStatusCode,
            AttributeStatusCodes = statusCodes
        };
    }

    private static T? ReadGoodValue<T>(
        IReadOnlyDictionary<uint, DataValue>? attributes,
        uint attributeId)
        where T : class
    {
        if (attributes == null ||
            !attributes.TryGetValue(attributeId, out var value) ||
            StatusCode.IsBad(value.StatusCode))
        {
            return null;
        }

        return value.WrappedValue.Value as T;
    }

    private static int? ReadInt32Value(
        IReadOnlyDictionary<uint, DataValue>? attributes,
        uint attributeId)
    {
        if (attributes == null ||
            !attributes.TryGetValue(attributeId, out var value) ||
            StatusCode.IsBad(value.StatusCode))
        {
            return null;
        }

        return value.WrappedValue.Value is int result ? result : null;
    }

    private static NodeClass? ReadNodeClass(IReadOnlyDictionary<uint, DataValue>? attributes)
    {
        if (attributes == null ||
            !attributes.TryGetValue(Attributes.NodeClass, out var value) ||
            StatusCode.IsBad(value.StatusCode))
        {
            return null;
        }

        return value.WrappedValue.Value switch
        {
            NodeClass nodeClass => nodeClass,
            int nodeClassValue => (NodeClass)nodeClassValue,
            uint nodeClassValue => (NodeClass)nodeClassValue,
            _ => null
        };
    }

    private static IReadOnlyList<uint> ReadArrayDimensions(
        IReadOnlyDictionary<uint, DataValue>? attributes)
    {
        if (attributes == null ||
            !attributes.TryGetValue(Attributes.ArrayDimensions, out var value) ||
            StatusCode.IsBad(value.StatusCode))
        {
            return [];
        }

        return value.WrappedValue.Value switch
        {
            uint[] dimensions => dimensions,
            UInt32Collection dimensions => dimensions.ToArray(),
            _ => []
        };
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

    private sealed record DiscoveredNode(NodeId NodeId)
    {
        public QualifiedName BrowseName { get; set; } = QualifiedName.Null;
        public LocalizedText DisplayName { get; set; } = LocalizedText.Null;
        public NodeClass NodeClass { get; set; } = NodeClass.Unspecified;
        public NodeId? TypeDefinitionId { get; set; }
    }

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
