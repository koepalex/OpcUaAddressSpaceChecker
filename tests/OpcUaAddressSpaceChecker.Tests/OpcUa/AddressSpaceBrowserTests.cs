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

    private static LiveNode Node(uint id, ushort ns, string name) =>
        new()
        {
            NodeId = new NodeId(id, ns),
            BrowseName = new QualifiedName(name, ns)
        };
}
