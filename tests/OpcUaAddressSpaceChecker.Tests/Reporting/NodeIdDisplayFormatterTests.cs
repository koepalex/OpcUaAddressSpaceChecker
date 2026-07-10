using Opc.Ua;
using OpcUaAddressSpaceChecker.Reporting;

namespace OpcUaAddressSpaceChecker.Tests.Reporting;

public sealed class NodeIdDisplayFormatterTests
{
    private const string DiNamespace = "http://opcfoundation.org/UA/DI/";

    private static readonly IReadOnlyDictionary<ushort, string> NamespaceSnapshot =
        new Dictionary<ushort, string>
        {
            [0] = "http://opcfoundation.org/UA/",
            [2] = DiNamespace
        };

    [Fact]
    public void Format_annotates_numeric_nodeid_with_browsename_and_keeps_expanded_id()
    {
        var nodeId = new NodeId(58596u, 2);
        var formatter = new NodeIdDisplayFormatter(
            NamespaceSnapshot,
            new Dictionary<NodeId, string> { [nodeId] = "5:ProductionPrograms" });

        Assert.Equal($"5:ProductionPrograms (nsu={DiNamespace};i=58596)", formatter.Format(nodeId));
    }

    [Fact]
    public void Format_falls_back_to_expanded_id_when_browsename_unknown()
    {
        var nodeId = new NodeId(58596u, 2);
        var formatter = new NodeIdDisplayFormatter(NamespaceSnapshot, new Dictionary<NodeId, string>());

        Assert.Equal($"nsu={DiNamespace};i=58596", formatter.Format(nodeId));
    }

    [Fact]
    public void Format_falls_back_to_bare_nodeid_when_namespace_unknown()
    {
        var nodeId = new NodeId(58596u, 9);
        var formatter = new NodeIdDisplayFormatter(NamespaceSnapshot, new Dictionary<NodeId, string>());

        Assert.Equal("ns=9;i=58596", formatter.Format(nodeId));
    }

    [Fact]
    public void Format_keeps_native_string_identifier()
    {
        var nodeId = new NodeId("Device1", 2);
        var formatter = new NodeIdDisplayFormatter(
            NamespaceSnapshot,
            new Dictionary<NodeId, string> { [nodeId] = "2:Device1" });

        Assert.Equal($"2:Device1 (nsu={DiNamespace};s=Device1)", formatter.Format(nodeId));
    }

    [Fact]
    public void Format_handles_null_nodeid()
    {
        var formatter = new NodeIdDisplayFormatter(NamespaceSnapshot, new Dictionary<NodeId, string>());

        Assert.Equal("<null>", formatter.Format(null));
        Assert.Null(formatter.TryGetBrowseName(null));
    }
}
