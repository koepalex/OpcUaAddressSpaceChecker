using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation;

public enum TypeDefinitionSelectionStatus
{
    Success,
    InvalidTypeId,
    TypeNotFound,
    NoMatchingInstances
}

public sealed record TypeDefinitionSelection(
    TypeDefinitionSelectionStatus Status,
    ExpandedNodeId? RequestedTypeId,
    NodeId? ModelTypeId,
    IReadOnlyList<LiveNode> Nodes,
    string? ErrorMessage)
{
    public bool IsSuccess => Status == TypeDefinitionSelectionStatus.Success;
}

public static class TypeDefinitionSelector
{
    public static bool TryParse(
        string requestedTypeId,
        out ExpandedNodeId expandedTypeId,
        out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(requestedTypeId) ||
            !ExpandedNodeId.TryParse(requestedTypeId, out expandedTypeId) ||
            expandedTypeId.IsNull ||
            expandedTypeId.ServerIndex != 0)
        {
            expandedTypeId = ExpandedNodeId.Null;
            errorMessage =
                $"Invalid type ExpandedNodeId '{requestedTypeId}'. Use a local ExpandedNodeId such as " +
                "'nsu=http://opcfoundation.org/UA/Pumps/;i=1052'.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    public static TypeDefinitionSelection Select(
        ExpandedNodeId expandedTypeId,
        IEnumerable<LiveNode> liveNodes,
        NamespaceTable liveNamespaceUris,
        NodesetModelIndex typeModel)
    {
        ArgumentNullException.ThrowIfNull(expandedTypeId);
        ArgumentNullException.ThrowIfNull(liveNodes);
        ArgumentNullException.ThrowIfNull(liveNamespaceUris);
        ArgumentNullException.ThrowIfNull(typeModel);

        if (expandedTypeId.IsNull || expandedTypeId.ServerIndex != 0)
        {
            return Failure(
                TypeDefinitionSelectionStatus.InvalidTypeId,
                $"Invalid local type ExpandedNodeId '{expandedTypeId}'.");
        }

        var typeResolved = string.IsNullOrEmpty(expandedTypeId.NamespaceUri)
            ? typeModel.TryMapTypeId(expandedTypeId.InnerNodeId, liveNamespaceUris, out var modelTypeId)
            : typeModel.TryResolveTypeId(expandedTypeId, out modelTypeId);

        if (!typeResolved)
        {
            return new TypeDefinitionSelection(
                TypeDefinitionSelectionStatus.TypeNotFound,
                expandedTypeId,
                null,
                [],
                $"Requested type '{expandedTypeId}' was not found in the loaded type model.");
        }

        if (!typeModel.TryGetType(modelTypeId, out var requestedType) ||
            requestedType?.NodeClass is not (NodeClass.ObjectType or NodeClass.VariableType))
        {
            return new TypeDefinitionSelection(
                TypeDefinitionSelectionStatus.InvalidTypeId,
                expandedTypeId,
                modelTypeId,
                [],
                $"Requested type '{expandedTypeId}' is not an ObjectType or VariableType.");
        }

        var matches = liveNodes
            .Where(node =>
                !NodeId.IsNull(node.TypeDefinitionId) &&
                typeModel.TryMapTypeId(node.TypeDefinitionId!, liveNamespaceUris, out var actualModelTypeId) &&
                typeModel.IsSameOrSubtype(actualModelTypeId, modelTypeId))
            .ToArray();

        if (matches.Length == 0)
        {
            return new TypeDefinitionSelection(
                TypeDefinitionSelectionStatus.NoMatchingInstances,
                expandedTypeId,
                modelTypeId,
                [],
                $"No instances of requested type '{expandedTypeId}' or its subtypes were found.");
        }

        return new TypeDefinitionSelection(
            TypeDefinitionSelectionStatus.Success,
            expandedTypeId,
            modelTypeId,
            matches,
            null);
    }

    private static TypeDefinitionSelection Failure(
        TypeDefinitionSelectionStatus status,
        string errorMessage) =>
        new(status, null, null, [], errorMessage);
}
