using Opc.Ua;
using Microsoft.Extensions.Logging.Abstractions;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation;
using OpcUaAddressSpaceChecker.Validation.Rules.Generic;

namespace OpcUaAddressSpaceChecker.Tests.Validation;

public sealed class NodeAddressabilityRuleTests(NodesetTestFixture fixture) : IClassFixture<NodesetTestFixture>
{
    [Fact]
    public void Reports_browsed_target_that_returns_bad_node_id_unknown()
    {
        var node = Node(StatusCodes.BadNodeIdUnknown);
        var rule = new NodeAddressabilityRule();

        Assert.True(rule.Applies(node, null, fixture.Context));
        var finding = Assert.Single(rule.Validate(node, null, fixture.Context));
        Assert.Equal("GEN-15", finding.RuleId);
        Assert.Equal(FindingConfidence.Confirmed, finding.Confidence);
        Assert.Contains("BadNodeIdUnknown", finding.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_when_all_identity_attribute_reads_return_bad_node_id_unknown()
    {
        var node = Node(StatusCodes.Good);
        node.AttributeStatusCodes = new Dictionary<uint, StatusCode>
        {
            [Attributes.NodeClass] = StatusCodes.BadNodeIdUnknown,
            [Attributes.BrowseName] = StatusCodes.BadNodeIdUnknown,
            [Attributes.DisplayName] = StatusCodes.BadNodeIdUnknown
        };

        Assert.True(new NodeAddressabilityRule().Applies(node, null, fixture.Context));
    }

    [Fact]
    public void Does_not_treat_access_denied_as_addressability_failure()
    {
        var node = Node(StatusCodes.BadUserAccessDenied);
        node.AttributeStatusCodes = new Dictionary<uint, StatusCode>
        {
            [Attributes.NodeClass] = StatusCodes.BadNodeIdUnknown,
            [Attributes.BrowseName] = StatusCodes.BadNodeIdUnknown,
            [Attributes.DisplayName] = StatusCodes.BadNodeIdUnknown
        };

        Assert.False(new NodeAddressabilityRule().Applies(node, null, fixture.Context));
    }

    [Fact]
    public async Task Exclusive_addressability_rule_suppresses_normal_conformance_rules()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var node = Node(StatusCodes.BadNodeIdUnknown);
        node.TypeDefinitionId = deviceType.NodeId;
        var registry = new RuleRegistry();
        registry.Register(new MissingMandatoryChildRule());
        registry.Register(new NodeAddressabilityRule());
        var engine = new ValidationEngine(registry, fixture.Model, NullLogger<ValidationEngine>.Instance);

        var report = await engine.RunAsync([node], fixture.Session);

        var finding = Assert.Single(report.Findings);
        Assert.Equal("GEN-15", finding.RuleId);
    }

    private static LiveNode Node(StatusCode browseStatusCode) =>
        new()
        {
            NodeId = new NodeId(70_001u, 2),
            BrowseName = new QualifiedName("Analog", 2),
            DisplayName = "Analog",
            NodeClass = NodeClass.Variable,
            BrowseStatusCode = browseStatusCode
        };
}
