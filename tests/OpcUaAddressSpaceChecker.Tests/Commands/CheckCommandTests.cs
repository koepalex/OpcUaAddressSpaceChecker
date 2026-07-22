using OpcUaAddressSpaceChecker.Commands;
using Opc.Ua;
using OpcUaAddressSpaceChecker.Configuration;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Tests.Commands;

public sealed class CheckCommandTests
{
    [Fact]
    public void IsRuleSelected_applies_include_and_exclude_filters_to_synthetic_diagnostics()
    {
        Assert.True(CheckCommand.IsRuleSelected("ACCESS-01", [], []));
        Assert.False(CheckCommand.IsRuleSelected("ACCESS-01", ["GEN-01"], []));
        Assert.True(CheckCommand.IsRuleSelected("ACCESS-01", ["ACCESS-01"], []));
        Assert.False(CheckCommand.IsRuleSelected("ACCESS-01", [], ["ACCESS-01"]));
    }

    [Fact]
    public void CountBrowseAccessDenied_uses_selected_subtrees_and_excludes_suppressed_paths()
    {
        var selectedRoot = Node(1, "Selected");
        var selectedDenied = Node(2, "SelectedDenied", StatusCodes.BadUserAccessDenied);
        var suppressedDenied = Node(3, "SuppressedDenied", StatusCodes.BadUserAccessDenied);
        var unrelatedDenied = Node(4, "UnrelatedDenied", StatusCodes.BadUserAccessDenied);
        selectedRoot.Children.Add(selectedDenied);
        selectedRoot.Children.Add(suppressedDenied);
        var paths = new Dictionary<NodeId, string>
        {
            [selectedRoot.NodeId] = "2:Selected",
            [selectedDenied.NodeId] = "2:Selected/2:SelectedDenied",
            [suppressedDenied.NodeId] = "2:Selected/2:SuppressedDenied",
            [unrelatedDenied.NodeId] = "2:UnrelatedDenied"
        };
        var config = new CheckerConfig
        {
            SuppressedBrowsePaths = ["2:Selected/2:SuppressedDenied"]
        }.Normalize();

        var count = CheckCommand.CountBrowseAccessDenied([selectedRoot], paths, config);

        Assert.Equal(1, count);
    }

    private static LiveNode Node(uint id, string name, StatusCode? browseStatusCode = null) =>
        new()
        {
            NodeId = new NodeId(id, 2),
            BrowseName = new QualifiedName(name, 2),
            NodeClass = NodeClass.Object,
            BrowseStatusCode = browseStatusCode
        };
}
