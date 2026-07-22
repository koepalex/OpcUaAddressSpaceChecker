using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation;

public sealed class ValidationContext
{
    public ValidationContext(
        NodesetModelIndex typeModel,
        ISession liveSession,
        string namespaceUri,
        ILogger logger,
        ValidationRunMetadata? runMetadata = null)
    {
        TypeModel = typeModel ?? throw new ArgumentNullException(nameof(typeModel));
        LiveSession = liveSession ?? throw new ArgumentNullException(nameof(liveSession));
        NamespaceUri = namespaceUri ?? string.Empty;
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        RunMetadata = runMetadata ?? ValidationRunMetadata.Default;
    }

    public NodesetModelIndex TypeModel { get; }
    public ISession LiveSession { get; }
    public string NamespaceUri { get; }
    public ILogger Logger { get; }
    public ValidationRunMetadata RunMetadata { get; }
    public FindingConfidence AbsenceConfidence => RunMetadata.AbsenceConfidence;

    public string ResolveNamespaceUri(NodeId nodeId) =>
        ResolveNamespaceUri(nodeId.NamespaceIndex);

    public string ResolveNamespaceUri(ushort namespaceIndex) =>
        LiveSession.NamespaceUris.GetString(namespaceIndex) ?? string.Empty;

    /// <summary>
    /// Resolves the namespace URI for a namespace index in the loaded type model (as opposed to the
    /// live session). InstanceDeclaration NodeIds are type-model NodeIds, so their declaring companion
    /// specification is identified through the model namespace map, not the live namespace table.
    /// </summary>
    public string ResolveModelNamespaceUri(ushort namespaceIndex) =>
        TypeModel.NamespaceMap.TryGetValue(namespaceIndex, out var uri) ? uri : string.Empty;

    public bool TryGetTypeDefinition(LiveNode node, out NodeState? typeDefinition)
    {
        ArgumentNullException.ThrowIfNull(node);

        typeDefinition = null;
        if (NodeId.IsNull(node.TypeDefinitionId))
        {
            return false;
        }

        return TypeModel.TryGetType(node.TypeDefinitionId, out typeDefinition);
    }

    public IReadOnlyList<InstanceDeclaration> GetInstanceDeclarations(NodeId typeDefinitionId)
    {
        if (NodeId.IsNull(typeDefinitionId))
        {
            return [];
        }

        return TypeModel.GetInstanceDeclarations(typeDefinitionId);
    }

    public IReadOnlyList<InstanceDeclaration> GetInstanceDeclarations(LiveNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return NodeId.IsNull(node.TypeDefinitionId)
            ? []
            : TypeModel.GetInstanceDeclarations(node.TypeDefinitionId);
    }
}
