using Opc.Ua;

namespace OpcUaAddressSpaceChecker.NodeModel;

/// <summary>
/// Structural declaration inherited from an OPC UA type definition hierarchy.
/// </summary>
public sealed record InstanceDeclaration(
    NodeId NodeId,
    IReadOnlyList<QualifiedName> BrowsePath,
    QualifiedName BrowseName,
    NodeClass NodeClass,
    NodeId? TypeDefinitionId,
    NodeId ModellingRuleId,
    NodeId ReferenceTypeId);
