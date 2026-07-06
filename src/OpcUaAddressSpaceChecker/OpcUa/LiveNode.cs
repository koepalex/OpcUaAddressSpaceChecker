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
    public List<LiveReference> ForwardHierarchicalReferences { get; } = [];
    public List<LiveNode> Children { get; } = [];
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
