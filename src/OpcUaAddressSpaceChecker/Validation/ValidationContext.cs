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
        ILogger logger)
    {
        TypeModel = typeModel ?? throw new ArgumentNullException(nameof(typeModel));
        LiveSession = liveSession ?? throw new ArgumentNullException(nameof(liveSession));
        NamespaceUri = namespaceUri ?? string.Empty;
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public NodesetModelIndex TypeModel { get; }
    public ISession LiveSession { get; }
    public string NamespaceUri { get; }
    public ILogger Logger { get; }

    public string ResolveNamespaceUri(NodeId nodeId) =>
        ResolveNamespaceUri(nodeId.NamespaceIndex);

    public string ResolveNamespaceUri(ushort namespaceIndex) =>
        LiveSession.NamespaceUris.GetString(namespaceIndex) ?? string.Empty;

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
