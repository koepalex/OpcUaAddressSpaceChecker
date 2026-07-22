using Opc.Ua;

namespace OpcUaAddressSpaceChecker.OpcUa;

/// <summary>
/// Materialized live instance node discovered from an OPC UA server address space.
/// </summary>
public sealed class LiveNode
{
    public required NodeId NodeId { get; init; }
    public QualifiedName BrowseName { get; set; } = QualifiedName.Null;
    public LocalizedText DisplayName { get; set; } = LocalizedText.Null;
    public NodeClass NodeClass { get; set; } = NodeClass.Unspecified;
    public NodeId? TypeDefinitionId { get; set; }
    public NodeId? DataType { get; set; }
    public int? ValueRank { get; set; }
    public IReadOnlyList<uint> ArrayDimensions { get; set; } = [];
    public StatusCode? BrowseStatusCode { get; set; }
    public IReadOnlyDictionary<uint, StatusCode> AttributeStatusCodes { get; set; } =
        new Dictionary<uint, StatusCode>();
    public List<LiveReference> ForwardHierarchicalReferences { get; } = [];
    public List<LiveNode> Children { get; } = [];

    public bool HasStatusCode(uint statusCode) =>
        BrowseStatusCode?.Code == statusCode ||
        AttributeStatusCodes.Values.Any(status => status.Code == statusCode);
}

/// <summary>
/// Forward hierarchical reference from one live node to another.
/// </summary>
public sealed record LiveReference(
    NodeId ReferenceTypeId,
    NodeId TargetId,
    QualifiedName BrowseName,
    LocalizedText DisplayName,
    NodeClass NodeClass);

/// <summary>
/// Result of browsing a live address space: the materialized nodes plus a concrete absolute
/// BrowsePath per node (index -> <c>namespaceIndex:BrowseName</c> path from the Objects root),
/// used by reporters to show where a finding actually lives in the address space.
/// </summary>
public sealed record AddressSpaceSnapshot(
    IReadOnlyCollection<LiveNode> Nodes,
    IReadOnlyDictionary<NodeId, string> BrowsePathsByNodeId)
{
    public int BrowseAccessDeniedCount =>
        Nodes.Count(node => node.BrowseStatusCode?.Code == StatusCodes.BadUserAccessDenied);

    public int BadNodeIdUnknownCount =>
        Nodes.Count(node => node.HasStatusCode(StatusCodes.BadNodeIdUnknown));
}
