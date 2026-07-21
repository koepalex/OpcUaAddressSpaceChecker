using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Validation;

public sealed class TypeDefinitionSelectorTests
{
    private const string ModelUri = "http://example.org/UA/Types/";
    private const string TargetTypeText = "nsu=http://example.org/UA/Types/;i=1000";

    [Fact]
    public void Select_matches_exact_type_across_different_namespace_indexes()
    {
        var fixture = CreateFixture();
        var node = CreateNode(1, fixture.LiveTargetTypeId);

        var selection = TypeDefinitionSelector.Select(
            ExpandedNodeId.Parse(TargetTypeText),
            [node],
            fixture.LiveNamespaceUris,
            fixture.Model);

        Assert.True(selection.IsSuccess);
        Assert.Same(node, Assert.Single(selection.Nodes));
        Assert.Equal(fixture.ModelTargetTypeId, selection.ModelTypeId);
        Assert.NotEqual(fixture.ModelTargetTypeId.NamespaceIndex, fixture.LiveTargetTypeId.NamespaceIndex);
    }

    [Fact]
    public void Select_includes_subtypes()
    {
        var fixture = CreateFixture();
        var node = CreateNode(2, fixture.LiveSubtypeId);

        var selection = TypeDefinitionSelector.Select(
            ExpandedNodeId.Parse(TargetTypeText),
            [node],
            fixture.LiveNamespaceUris,
            fixture.Model);

        Assert.True(selection.IsSuccess);
        Assert.Same(node, Assert.Single(selection.Nodes));
    }

    [Fact]
    public void Select_excludes_unrelated_types_and_fails_when_no_instances_match()
    {
        var fixture = CreateFixture();
        var unrelated = CreateNode(3, fixture.LiveUnrelatedTypeId);

        var selection = TypeDefinitionSelector.Select(
            ExpandedNodeId.Parse(TargetTypeText),
            [unrelated],
            fixture.LiveNamespaceUris,
            fixture.Model);

        Assert.Equal(TypeDefinitionSelectionStatus.NoMatchingInstances, selection.Status);
        Assert.Empty(selection.Nodes);
    }

    [Fact]
    public void Select_fails_when_requested_type_is_not_in_model()
    {
        var fixture = CreateFixture();

        var selection = TypeDefinitionSelector.Select(
            ExpandedNodeId.Parse("nsu=http://example.org/UA/Types/;i=9999"),
            [CreateNode(4, fixture.LiveTargetTypeId)],
            fixture.LiveNamespaceUris,
            fixture.Model);

        Assert.Equal(TypeDefinitionSelectionStatus.TypeNotFound, selection.Status);
        Assert.Empty(selection.Nodes);
    }

    [Fact]
    public void Select_rejects_data_types()
    {
        var fixture = CreateFixture();

        var selection = TypeDefinitionSelector.Select(
            ExpandedNodeId.Parse("nsu=http://example.org/UA/Types/;i=1003"),
            [CreateNode(5, fixture.LiveTargetTypeId)],
            fixture.LiveNamespaceUris,
            fixture.Model);

        Assert.Equal(TypeDefinitionSelectionStatus.InvalidTypeId, selection.Status);
        Assert.Empty(selection.Nodes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid_text")]
    [InlineData("svr=1;nsu=http://example.org/UA/Types/;i=1000")]
    public void TryParse_rejects_invalid_or_remote_type_ids(string value)
    {
        var parsed = TypeDefinitionSelector.TryParse(value, out var typeId, out var errorMessage);

        Assert.False(parsed);
        Assert.True(typeId.IsNull);
        Assert.NotNull(errorMessage);
    }

    private static LiveNode CreateNode(uint id, NodeId typeDefinitionId) =>
        new()
        {
            NodeId = new NodeId(id, 2),
            BrowseName = new QualifiedName($"Node{id}", 2),
            NodeClass = NodeClass.Object,
            TypeDefinitionId = typeDefinitionId
        };

    private static SelectorFixture CreateFixture()
    {
        var modelNamespaceUris = new NamespaceTable();
        modelNamespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
        var modelNamespaceIndex = modelNamespaceUris.GetIndexOrAppend(ModelUri);

        var targetTypeId = new NodeId(1000u, modelNamespaceIndex);
        var subtypeId = new NodeId(1001u, modelNamespaceIndex);
        var unrelatedTypeId = new NodeId(1002u, modelNamespaceIndex);
        var dataTypeId = new NodeId(1003u, modelNamespaceIndex);

        var model = LiveNodesetModel.Build(new LiveTypeModel
        {
            NamespaceUris = modelNamespaceUris,
            Nodes =
            [
                new LiveTypeModelNode
                {
                    NodeId = ObjectTypeIds.BaseObjectType,
                    BrowseName = new QualifiedName("BaseObjectType"),
                    NodeClass = NodeClass.ObjectType
                },
                new LiveTypeModelNode
                {
                    NodeId = targetTypeId,
                    BrowseName = new QualifiedName("TargetType", modelNamespaceIndex),
                    NodeClass = NodeClass.ObjectType,
                    SuperTypeId = ObjectTypeIds.BaseObjectType
                },
                new LiveTypeModelNode
                {
                    NodeId = subtypeId,
                    BrowseName = new QualifiedName("TargetSubtype", modelNamespaceIndex),
                    NodeClass = NodeClass.ObjectType,
                    SuperTypeId = targetTypeId
                },
                new LiveTypeModelNode
                {
                    NodeId = unrelatedTypeId,
                    BrowseName = new QualifiedName("UnrelatedType", modelNamespaceIndex),
                    NodeClass = NodeClass.ObjectType,
                    SuperTypeId = ObjectTypeIds.BaseObjectType
                },
                new LiveTypeModelNode
                {
                    NodeId = dataTypeId,
                    BrowseName = new QualifiedName("CustomDataType", modelNamespaceIndex),
                    NodeClass = NodeClass.DataType
                }
            ]
        });

        var liveNamespaceUris = new NamespaceTable();
        liveNamespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
        liveNamespaceUris.GetIndexOrAppend("http://example.org/UA/Other/");
        var liveNamespaceIndex = liveNamespaceUris.GetIndexOrAppend(ModelUri);

        return new SelectorFixture(
            model,
            liveNamespaceUris,
            targetTypeId,
            new NodeId(targetTypeId.Identifier, liveNamespaceIndex),
            new NodeId(subtypeId.Identifier, liveNamespaceIndex),
            new NodeId(unrelatedTypeId.Identifier, liveNamespaceIndex));
    }

    private sealed record SelectorFixture(
        NodesetModelIndex Model,
        NamespaceTable LiveNamespaceUris,
        NodeId ModelTargetTypeId,
        NodeId LiveTargetTypeId,
        NodeId LiveSubtypeId,
        NodeId LiveUnrelatedTypeId);
}
