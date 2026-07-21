using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation;

public sealed class PlacementRegistry
{
    private const int MaxBrowseDepth = 256;
    private const int MaxVisitedNodes = 100_000;
    private const uint RequestedMaxReferencesPerNode = 1_000;

    private static readonly IReadOnlyList<PlacementEntry> Entries =
    [
        new(
            CompanionSpecRuleHelpers.DiModelUri,
            "ComponentType",
            CompanionSpecRuleHelpers.DiModelUri,
            5001,
            "DeviceSet"),
        new(
            CompanionSpecRuleHelpers.MachineryModelUri,
            "MachineType",
            CompanionSpecRuleHelpers.MachineryModelUri,
            1001,
            "Machines"),
        new(
            CompanionSpecRuleHelpers.MachineryModelUri,
            "MachineryItemType",
            CompanionSpecRuleHelpers.MachineryModelUri,
            1001,
            "Machines"),
        new(
            CompanionSpecRuleHelpers.MachineryModelUri,
            "BaseMachineType",
            CompanionSpecRuleHelpers.MachineryModelUri,
            1001,
            "Machines")
    ];

    public IReadOnlyList<PlacementEntry> RegisteredEntries => Entries;

    public bool TryGetEntryForType(
        ValidationContext context,
        NodeId? typeDefinitionId,
        string modelUri,
        out PlacementEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(context);

        entry = Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.TypeAncestorModelUri, modelUri, StringComparison.Ordinal) &&
            CompanionSpecRuleHelpers.TypeDerivesFrom(
                context,
                typeDefinitionId,
                candidate.TypeAncestorModelUri,
                candidate.TypeAncestorName));

        return entry != null;
    }

    public bool IsReachableFromEntryPoint(
        ValidationContext context,
        PlacementEntry entry,
        NodeId targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(targetNodeId);

        if (!TryCreateLiveNodeId(
                context.LiveSession.NamespaceUris,
                entry.EntryPointModelUri,
                entry.EntryPointIdentifier,
                out var entryPointNodeId))
        {
            context.Logger.LogDebug(
                "Cannot evaluate placement for {TargetNodeId}: live server namespace array does not contain {ModelUri}.",
                targetNodeId,
                entry.EntryPointModelUri);
            return false;
        }

        return IsReachableByHierarchicalBrowse(context.LiveSession, entryPointNodeId, targetNodeId, context.Logger);
    }

    public static bool TryCreateLiveNodeId(
        NamespaceTable namespaceUris,
        string modelUri,
        uint numericIdentifier,
        out NodeId nodeId)
    {
        ArgumentNullException.ThrowIfNull(namespaceUris);

        nodeId = NodeId.Null;
        var namespaceIndex = namespaceUris.GetIndex(modelUri);
        if (namespaceIndex < 0 || namespaceIndex > ushort.MaxValue)
        {
            return false;
        }

        nodeId = new NodeId(numericIdentifier, (ushort)namespaceIndex);
        return true;
    }

    private static bool IsReachableByHierarchicalBrowse(
        ISession session,
        NodeId entryPointNodeId,
        NodeId targetNodeId,
        ILogger logger)
    {
        if (entryPointNodeId == targetNodeId)
        {
            return true;
        }

        var visited = new HashSet<NodeId> { entryPointNodeId };
        var queue = new Queue<NodeId>();
        queue.Enqueue(entryPointNodeId);

        for (var depth = 0; queue.Count > 0 && depth < MaxBrowseDepth && visited.Count < MaxVisitedNodes; depth++)
        {
            var levelCount = queue.Count;
            for (var i = 0; i < levelCount; i++)
            {
                var current = queue.Dequeue();
                foreach (var childId in BrowseForwardHierarchicalReferences(session, current, logger))
                {
                    if (childId == targetNodeId)
                    {
                        return true;
                    }

                    if (visited.Add(childId))
                    {
                        queue.Enqueue(childId);
                    }
                }
            }
        }

        if (visited.Count >= MaxVisitedNodes)
        {
            logger.LogDebug(
                "Stopped placement reachability browse from {EntryPointNodeId} after {VisitedCount} nodes.",
                entryPointNodeId,
                visited.Count);
        }

        return false;
    }

    private static IEnumerable<NodeId> BrowseForwardHierarchicalReferences(
        ISession session,
        NodeId sourceNodeId,
        ILogger logger)
    {
        var browseDescription = new BrowseDescription
        {
            NodeId = sourceNodeId,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = 0,
            ResultMask = (uint)BrowseResultMask.All
        };

        BrowseResultCollection results;

        try
        {
            var browseResponse = session.BrowseAsync(
                requestHeader: null,
                view: null,
                requestedMaxReferencesPerNode: RequestedMaxReferencesPerNode,
                nodesToBrowse: new BrowseDescriptionCollection { browseDescription },
                ct: CancellationToken.None).GetAwaiter().GetResult();
            results = browseResponse.Results;
        }
        catch (ServiceResultException ex)
        {
            logger.LogDebug(ex, "Browse failed while evaluating placement from {SourceNodeId}.", sourceNodeId);
            yield break;
        }

        if (results.Count == 0)
        {
            yield break;
        }

        foreach (var targetId in ReadBrowseResultTargets(session, results[0], logger))
        {
            yield return targetId;
        }
    }

    private static IEnumerable<NodeId> ReadBrowseResultTargets(
        ISession session,
        BrowseResult browseResult,
        ILogger logger)
    {
        if (StatusCode.IsBad(browseResult.StatusCode))
        {
            yield break;
        }

        foreach (var targetId in ConvertReferences(session.NamespaceUris, browseResult.References))
        {
            yield return targetId;
        }

        var continuationPoint = browseResult.ContinuationPoint;
        while (continuationPoint is { Length: > 0 })
        {
            BrowseResultCollection results;

            try
            {
                var browseNextResponse = session.BrowseNextAsync(
                    requestHeader: null,
                    releaseContinuationPoints: false,
                    continuationPoints: new ByteStringCollection { continuationPoint },
                    ct: CancellationToken.None).GetAwaiter().GetResult();
                results = browseNextResponse.Results;
            }
            catch (ServiceResultException ex)
            {
                logger.LogDebug(ex, "BrowseNext failed while evaluating placement.");
                yield break;
            }

            if (results.Count == 0 || StatusCode.IsBad(results[0].StatusCode))
            {
                yield break;
            }

            foreach (var targetId in ConvertReferences(session.NamespaceUris, results[0].References))
            {
                yield return targetId;
            }

            continuationPoint = results[0].ContinuationPoint;
        }
    }

    private static IEnumerable<NodeId> ConvertReferences(
        NamespaceTable namespaceUris,
        ReferenceDescriptionCollection references)
    {
        foreach (var reference in references)
        {
            var targetId = ExpandedNodeId.ToNodeId(reference.NodeId, namespaceUris);
            if (!NodeId.IsNull(targetId))
            {
                yield return targetId;
            }
        }
    }
}

public sealed record PlacementEntry(
    string TypeAncestorModelUri,
    string TypeAncestorName,
    string EntryPointModelUri,
    uint EntryPointIdentifier,
    string EntryPointBrowseName);

internal static class CompanionSpecRuleHelpers
{
    internal const string DiModelUri = "http://opcfoundation.org/UA/DI/";
    internal const string PumpsModelUri = "http://opcfoundation.org/UA/Pumps/";
    internal const string MachineryModelUri = "http://opcfoundation.org/UA/Machinery/";

    internal static readonly NodeId HasComponent = new(47);
    internal static readonly NodeId HasProperty = new(46);
    internal static readonly NodeId Organizes = new(35);
    internal static readonly NodeId BaseDataVariableType = new(63);

    internal sealed record ChildLink(LiveNode Parent, LiveNode Child, LiveReference Reference);

    internal static bool TypeDerivesFrom(
        ValidationContext context,
        NodeId? typeDefinitionId,
        string modelUri,
        string ancestorTypeName)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryFindType(context, modelUri, ancestorTypeName, out var ancestorType) ||
            !TryMapToModelTypeId(context, typeDefinitionId, out var modelTypeId))
        {
            return false;
        }

        return modelTypeId == ancestorType.NodeId ||
               context.TypeModel.IsSameOrSubtype(modelTypeId, ancestorType.NodeId);
    }

    internal static bool TypeDerivesFrom(
        ValidationContext context,
        NodeId? typeDefinitionId,
        string modelUri,
        uint ancestorNumericIdentifier)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryResolveModelNodeId(context, modelUri, ancestorNumericIdentifier, out var ancestorTypeId) ||
            !context.TypeModel.TryGetType(ancestorTypeId, out _) ||
            !TryMapToModelTypeId(context, typeDefinitionId, out var modelTypeId))
        {
            return false;
        }

        return modelTypeId == ancestorTypeId ||
               context.TypeModel.IsSameOrSubtype(modelTypeId, ancestorTypeId);
    }

    internal static bool IsTypeCompatible(
        ValidationContext context,
        NodeId? actualTypeDefinitionId,
        string expectedModelUri,
        uint expectedTypeNumericIdentifier)
    {
        if (!TryResolveModelNodeId(context, expectedModelUri, expectedTypeNumericIdentifier, out var expectedTypeId))
        {
            return false;
        }

        return IsTypeCompatible(context, actualTypeDefinitionId, expectedTypeId);
    }

    internal static bool IsTypeCompatible(
        ValidationContext context,
        NodeId? actualTypeDefinitionId,
        NodeId expectedModelTypeId)
    {
        if (!TryMapToModelTypeId(context, actualTypeDefinitionId, out var actualModelTypeId))
        {
            return false;
        }

        return actualModelTypeId == expectedModelTypeId ||
               context.TypeModel.IsSameOrSubtype(actualModelTypeId, expectedModelTypeId);
    }

    internal static bool TryFindType(
        ValidationContext context,
        string modelUri,
        string typeName,
        out NodeState typeNode)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var candidate in context.TypeModel.TypesById.Values)
        {
            if (string.Equals(candidate.BrowseName.Name, typeName, StringComparison.Ordinal) &&
                string.Equals(ResolveModelUri(context.TypeModel, candidate.NodeId), modelUri, StringComparison.Ordinal))
            {
                typeNode = candidate;
                return true;
            }
        }

        typeNode = null!;
        return false;
    }

    internal static bool TryResolveModelNodeId(
        ValidationContext context,
        string modelUri,
        uint numericIdentifier,
        out NodeId nodeId)
    {
        ArgumentNullException.ThrowIfNull(context);

        nodeId = NodeId.Null;
        if (!TryGetModelNamespaceIndex(context.TypeModel, modelUri, out var namespaceIndex))
        {
            return false;
        }

        nodeId = new NodeId(numericIdentifier, namespaceIndex);
        return true;
    }

    internal static IReadOnlyList<ChildLink> GetChildLinks(LiveNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var links = new List<ChildLink>();
        foreach (var reference in parent.ForwardHierarchicalReferences)
        {
            links.AddRange(parent.Children
                .Where(child => child.NodeId == reference.TargetId)
                .Select(child => new ChildLink(parent, child, reference)));
        }

        return links;
    }

    internal static IReadOnlyList<ChildLink> FindDirectChildren(
        LiveNode parent,
        string browseName,
        ValidationContext context,
        string? expectedModelUri = null) =>
        GetChildLinks(parent)
            .Where(link => BrowseNameMatches(link.Child.BrowseName, browseName, context, expectedModelUri))
            .ToArray();

    internal static IReadOnlyList<ChildLink> FindByBrowsePath(
        LiveNode root,
        IReadOnlyList<QualifiedName> browsePath,
        ValidationContext context,
        IReadOnlyList<InstanceDeclaration>? declarations = null)
    {
        if (browsePath.Count == 0)
        {
            return [];
        }

        IReadOnlyList<LiveNode> currentParents = [root];
        IReadOnlyList<ChildLink> currentMatches = [];

        for (var depth = 0; depth < browsePath.Count; depth++)
        {
            var segment = browsePath[depth];
            var prefix = browsePath.Take(depth + 1).ToArray();
            var segmentDeclaration = declarations?.FirstOrDefault(declaration =>
                Rules.Generic.GenericRuleHelpers.BrowsePathEquals(declaration.BrowsePath, prefix));
            var placeholderSegment = segmentDeclaration != null && Rules.Generic.GenericRuleHelpers.IsPlaceholder(segmentDeclaration);
            var expectedUri = ResolveModelUri(context.TypeModel, new NodeId(0, segment.NamespaceIndex));
            var nextMatches = new List<ChildLink>();

            foreach (var parent in currentParents)
            {
                if (placeholderSegment)
                {
                    nextMatches.AddRange(GetChildLinks(parent).Where(link =>
                        IsReferenceTypeCompatible(context, link.Reference.ReferenceTypeId, segmentDeclaration!.ReferenceTypeId) &&
                        (NodeId.IsNull(segmentDeclaration.TypeDefinitionId) ||
                         IsTypeCompatible(context, link.Child.TypeDefinitionId, segmentDeclaration.TypeDefinitionId!))));
                }
                else
                {
                    nextMatches.AddRange(GetChildLinks(parent).Where(link =>
                        BrowseNameMatches(link.Child.BrowseName, segment.Name, context, expectedUri)));
                }
            }

            currentMatches = nextMatches;
            currentParents = nextMatches.Select(match => match.Child).ToArray();
            if (currentParents.Count == 0)
            {
                break;
            }
        }

        return currentMatches;
    }

    internal static bool BrowsePathExists(
        LiveNode root,
        IReadOnlyList<QualifiedName> browsePath,
        ValidationContext context,
        IReadOnlyList<InstanceDeclaration>? declarations = null) =>
        FindByBrowsePath(root, browsePath, context, declarations).Count > 0;

    internal static bool IsReferenceTypeCompatible(
        ValidationContext context,
        NodeId actualReferenceTypeId,
        NodeId expectedReferenceTypeId) =>
        actualReferenceTypeId == expectedReferenceTypeId ||
        context.TypeModel.IsSameOrSubtype(actualReferenceTypeId, expectedReferenceTypeId);

    internal static bool BrowseNameMatches(
        QualifiedName liveBrowseName,
        string expectedName,
        ValidationContext context,
        string? expectedModelUri = null)
    {
        if (!string.Equals(liveBrowseName.Name, expectedName, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(expectedModelUri))
        {
            return true;
        }

        var liveUri = context.ResolveNamespaceUri(liveBrowseName.NamespaceIndex);
        return string.Equals(liveUri, expectedModelUri, StringComparison.Ordinal);
    }

    internal static string FormatBrowsePath(IEnumerable<QualifiedName> browsePath) =>
        string.Join("/", browsePath.Select(FormatBrowseName));

    internal static string FormatBrowseName(QualifiedName browseName) =>
        $"{browseName.NamespaceIndex}:{browseName.Name}";

    internal static string FormatNode(LiveNode node) =>
        string.IsNullOrEmpty(node.BrowseName.Name) ? node.NodeId.ToString() : FormatBrowseName(node.BrowseName);

    /// <summary>
    /// Formats a model/live NodeId for finding details as "&lt;ns:BrowseName&gt; (&lt;ExpandedNodeId&gt;)"
    /// when the node's BrowseName is known in the type model, otherwise its ExpandedNodeId, falling
    /// back to the bare NodeId. Keeps numeric identifiers readable while retaining the precise id.
    /// </summary>
    internal static string FormatNode(ValidationContext context, NodeId? nodeId)
    {
        if (NodeId.IsNull(nodeId))
        {
            return "<null>";
        }

        var uri = context.TypeModel.NamespaceMap.TryGetValue(nodeId!.NamespaceIndex, out var namespaceUri)
            ? namespaceUri
            : string.Empty;
        var expanded = string.IsNullOrWhiteSpace(uri)
            ? nodeId.ToString()
            : new ExpandedNodeId(nodeId, uri).ToString();

        if (context.TypeModel.TryGetNode(nodeId, out var node) &&
            node is not null &&
            !QualifiedName.IsNull(node.BrowseName))
        {
            return $"{FormatBrowseName(node.BrowseName)} ({expanded})";
        }

        return expanded;
    }

    internal static string FormatNodeId(NodeId? nodeId) =>
        NodeId.IsNull(nodeId) ? "<null>" : nodeId.ToString();

    private static bool TryMapToModelTypeId(
        ValidationContext context,
        NodeId? liveOrModelTypeId,
        out NodeId modelTypeId)
    {
        modelTypeId = NodeId.Null;
        if (NodeId.IsNull(liveOrModelTypeId))
        {
            return false;
        }

        return context.TypeModel.TryMapTypeId(
            liveOrModelTypeId,
            context.LiveSession.NamespaceUris,
            out modelTypeId);
    }

    private static bool TryGetModelNamespaceIndex(
        NodesetModelIndex typeModel,
        string modelUri,
        out ushort namespaceIndex)
    {
        foreach (var item in typeModel.NamespaceMap)
        {
            if (string.Equals(item.Value, modelUri, StringComparison.Ordinal))
            {
                namespaceIndex = item.Key;
                return true;
            }
        }

        namespaceIndex = 0;
        return false;
    }

    private static string ResolveModelUri(NodesetModelIndex typeModel, NodeId nodeId) =>
        typeModel.NamespaceMap.TryGetValue(nodeId.NamespaceIndex, out var uri) ? uri : string.Empty;
}
