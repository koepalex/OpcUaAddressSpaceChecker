using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Tests.OpcUa;

public sealed class AddressSpaceBrowserTests
{
    [Fact]
    public void BuildBrowsePaths_produces_concrete_absolute_paths_excluding_root()
    {
        var root = ObjectIds.ObjectsFolder;
        var machineTool = Node(1, 13, "FullMachineTool");
        var production = Node(2, 5, "Production");
        var job = Node(3, 13, "My Job1");
        var programs = Node(4, 5, "ProductionPrograms");

        var nodes = new List<LiveNode>
        {
            new() { NodeId = root, BrowseName = new QualifiedName("Objects", 0) },
            machineTool,
            production,
            job,
            programs
        };

        var edges = new List<(NodeId, NodeId)>
        {
            (root, machineTool.NodeId),
            (machineTool.NodeId, production.NodeId),
            (production.NodeId, job.NodeId),
            (job.NodeId, programs.NodeId)
        };

        var paths = AddressSpaceBrowser.BuildBrowsePaths(nodes, edges, root);

        Assert.Equal(
            "13:FullMachineTool/5:Production/13:My Job1/5:ProductionPrograms",
            paths[programs.NodeId]);
        Assert.Equal("13:FullMachineTool", paths[machineTool.NodeId]);
        Assert.False(paths.ContainsKey(root));
    }

    [Fact]
    public void BuildBrowsePaths_uses_first_discovered_parent_and_tolerates_cycles()
    {
        var root = ObjectIds.ObjectsFolder;
        var first = Node(10, 2, "First");
        var second = Node(11, 2, "Second");
        var leaf = Node(12, 2, "Leaf");

        var nodes = new List<LiveNode>
        {
            new() { NodeId = root, BrowseName = new QualifiedName("Objects", 0) },
            first,
            second,
            leaf
        };

        // leaf is reachable via both First (discovered first) and Second; a back-edge introduces a cycle.
        var edges = new List<(NodeId, NodeId)>
        {
            (root, first.NodeId),
            (root, second.NodeId),
            (first.NodeId, leaf.NodeId),
            (second.NodeId, leaf.NodeId),
            (leaf.NodeId, first.NodeId)
        };

        var paths = AddressSpaceBrowser.BuildBrowsePaths(nodes, edges, root);

        Assert.Equal("2:First/2:Leaf", paths[leaf.NodeId]);
    }

    [Fact]
    public void MaterializeNode_uses_good_read_values_and_browse_fallbacks_for_bad_reads()
    {
        var nodeId = new NodeId(42u, 2);
        var browsedName = new QualifiedName("BrowsedName", 2);
        var browsedDisplayName = new LocalizedText("Browsed display");
        var attributes = new Dictionary<uint, DataValue>
        {
            [Attributes.BrowseName] = Value(StatusCodes.BadNodeIdUnknown),
            [Attributes.DisplayName] = Value(StatusCodes.Good, new LocalizedText("Read display")),
            [Attributes.NodeClass] = Value(StatusCodes.BadUserAccessDenied),
            [Attributes.ValueRank] = Value(StatusCodes.Good, -1)
        };

        var node = AddressSpaceBrowser.MaterializeNode(
            nodeId,
            browsedName,
            browsedDisplayName,
            NodeClass.Variable,
            VariableTypeIds.BaseDataVariableType,
            attributes,
            StatusCodes.Good);

        Assert.Equal(browsedName, node.BrowseName);
        Assert.Equal("Read display", node.DisplayName.Text);
        Assert.Equal(NodeClass.Variable, node.NodeClass);
        Assert.Equal(-1, node.ValueRank);
        Assert.Equal(StatusCodes.BadNodeIdUnknown, node.AttributeStatusCodes[Attributes.BrowseName]);
        Assert.Equal(StatusCodes.BadUserAccessDenied, node.AttributeStatusCodes[Attributes.NodeClass]);
        Assert.Equal(StatusCodes.Good, node.BrowseStatusCode);
    }

    [Fact]
    public void MaterializeNode_all_bad_reads_preserve_reference_description_metadata()
    {
        var attributes = new Dictionary<uint, DataValue>
        {
            [Attributes.BrowseName] = Value(StatusCodes.BadNodeIdUnknown),
            [Attributes.DisplayName] = Value(StatusCodes.BadNodeIdUnknown),
            [Attributes.NodeClass] = Value(StatusCodes.BadNodeIdUnknown),
            [Attributes.DataType] = Value(StatusCodes.BadNodeIdUnknown),
            [Attributes.ValueRank] = Value(StatusCodes.BadNodeIdUnknown),
            [Attributes.ArrayDimensions] = Value(StatusCodes.BadNodeIdUnknown)
        };

        var node = AddressSpaceBrowser.MaterializeNode(
            new NodeId(43u, 2),
            new QualifiedName("Analog", 2),
            new LocalizedText("Analog"),
            NodeClass.Variable,
            VariableTypeIds.AnalogItemType,
            attributes,
            StatusCodes.BadNodeIdUnknown);

        Assert.Equal("Analog", node.BrowseName.Name);
        Assert.Equal("Analog", node.DisplayName.Text);
        Assert.Equal(NodeClass.Variable, node.NodeClass);
        Assert.Equal(6, node.AttributeStatusCodes.Count);
        Assert.True(node.HasStatusCode(StatusCodes.BadNodeIdUnknown));
    }

    [Fact]
    public void Snapshot_counts_only_browse_access_denials_as_restricted_view_evidence()
    {
        var readDenied = Node(50, 2, "ReadDenied");
        readDenied.AttributeStatusCodes = new Dictionary<uint, StatusCode>
        {
            [Attributes.DataType] = StatusCodes.BadUserAccessDenied
        };
        var browseDenied = Node(51, 2, "BrowseDenied");
        browseDenied.BrowseStatusCode = StatusCodes.BadUserAccessDenied;
        var snapshot = new AddressSpaceSnapshot([readDenied, browseDenied], new Dictionary<NodeId, string>());

        Assert.Equal(1, snapshot.BrowseAccessDeniedCount);
    }

    private static DataValue Value(StatusCode statusCode, object? value = null) =>
        new()
        {
            StatusCode = statusCode,
            WrappedValue = new Variant(value)
        };

    private static LiveNode Node(uint id, ushort ns, string name) =>
        new()
        {
            NodeId = new NodeId(id, ns),
            BrowseName = new QualifiedName(name, ns)
        };
}
