using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Tests.NodeModel;

public sealed class LiveNodesetModelTests
{
    private const string CustomModelUri = "http://example.org/UA/Custom/";

    [Fact]
    public void Build_materializes_supertype_chain_and_subtype_relationship()
    {
        var index = LiveNodesetModel.Build(CreateSampleTypeModel());
        var customTypeId = new NodeId(1000u, 1);

        var chain = index.GetSupertypeChain(customTypeId).Select(node => node.NodeId).ToArray();

        Assert.Equal([ObjectTypeIds.BaseObjectType, customTypeId], chain);
        Assert.True(index.IsSameOrSubtype(customTypeId, ObjectTypeIds.BaseObjectType));
    }

    [Fact]
    public void Build_materializes_mandatory_instance_declaration_from_live_members()
    {
        var index = LiveNodesetModel.Build(CreateSampleTypeModel());
        var customTypeId = new NodeId(1000u, 1);

        var declarations = index.GetInstanceDeclarations(customTypeId);

        var declaration = Assert.Single(declarations);
        Assert.Equal("Signal", declaration.BrowseName.Name);
        Assert.Equal(NodeClass.Variable, declaration.NodeClass);
        Assert.Equal(new NodeId(78u), declaration.ModellingRuleId);
        Assert.True(index.TryGetNode(declaration.NodeId, out var declarationNode));
        var variable = Assert.IsAssignableFrom<BaseVariableState>(declarationNode);
        Assert.Equal(DataTypeIds.Double, variable.DataType);
    }

    [Fact]
    public void Build_namespace_map_mirrors_server_namespace_table()
    {
        var index = LiveNodesetModel.Build(CreateSampleTypeModel());

        Assert.Equal(Namespaces.OpcUa, index.NamespaceMap[0]);
        Assert.Equal(CustomModelUri, index.NamespaceMap[1]);
    }

    private static LiveTypeModel CreateSampleTypeModel()
    {
        var namespaceUris = new NamespaceTable();
        namespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
        namespaceUris.GetIndexOrAppend(CustomModelUri);

        var customTypeId = new NodeId(1000u, 1);
        var signalId = new NodeId(1001u, 1);

        var baseObjectType = new LiveTypeModelNode
        {
            NodeId = ObjectTypeIds.BaseObjectType,
            BrowseName = new QualifiedName("BaseObjectType"),
            NodeClass = NodeClass.ObjectType
        };

        var customType = new LiveTypeModelNode
        {
            NodeId = customTypeId,
            BrowseName = new QualifiedName("CustomType", 1),
            NodeClass = NodeClass.ObjectType,
            SuperTypeId = ObjectTypeIds.BaseObjectType
        };
        customType.Children.Add(new LiveTypeChild(ReferenceTypeIds.HasComponent, signalId));

        var signal = new LiveTypeModelNode
        {
            NodeId = signalId,
            BrowseName = new QualifiedName("Signal", 1),
            NodeClass = NodeClass.Variable,
            ModellingRuleId = new NodeId(78u),
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            DataType = DataTypeIds.Double,
            ValueRank = ValueRanks.Scalar
        };

        return new LiveTypeModel
        {
            Nodes = [baseObjectType, customType, signal],
            NamespaceUris = namespaceUris
        };
    }
}
