using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation;
using OpcUaAddressSpaceChecker.Validation.Rules.Generic;

namespace OpcUaAddressSpaceChecker.Tests.Validation;

public sealed class ValidationEngineTests(NodesetTestFixture fixture) : IClassFixture<NodesetTestFixture>
{
    [Fact]
    public async Task RunAsync_collapses_indistinguishable_findings_but_keeps_distinct_ones()
    {
        var node = LiveNodeFactory.Object(new QualifiedName("Node", 1), new NodeId(1, 1));

        // Emits the same finding twice (as happens when a subject node is reached via multiple
        // traversal paths) plus one finding that differs only in Details.
        var rule = new StubRule(
            "STUB-01",
            node.NodeId,
            duplicated: new ValidationFinding("STUB-01", Severity.Error, node.NodeId, "0:Node", "Same", "Same details"),
            distinct: new ValidationFinding("STUB-01", Severity.Error, node.NodeId, "0:Node", "Same", "Different details"));

        var registry = new RuleRegistry();
        registry.Register(rule);
        var engine = new ValidationEngine(registry, fixture.Model, NullLogger<ValidationEngine>.Instance);

        var report = await engine.RunAsync([node], fixture.Session);

        Assert.Equal(2, report.TotalFindings);
        Assert.Equal(2, report.Findings.Count);
        Assert.Single(report.Findings, finding => finding.Details == "Same details");
        Assert.Single(report.Findings, finding => finding.Details == "Different details");
    }

    [Fact]
    public async Task RunAsync_marks_absence_findings_inconclusive_for_restricted_view()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var node = LiveNodeFactory.Object(
            new QualifiedName("Device", fixture.NamespaceIndex(NodesetTestFixture.DiModelUri)),
            deviceType.NodeId);
        var registry = new RuleRegistry();
        registry.Register(new MissingMandatoryChildRule());
        var engine = new ValidationEngine(registry, fixture.Model, NullLogger<ValidationEngine>.Instance);
        var metadata = ValidationViewPolicy.Evaluate(
            AuthenticationMode.Anonymous,
            ViewCompletenessRequest.Auto,
            accessDeniedCount: 0);

        var report = await engine.RunAsync([node], fixture.Session, runMetadata: metadata);

        Assert.NotEmpty(report.Findings);
        Assert.All(report.Findings, finding => Assert.Equal(FindingConfidence.Inconclusive, finding.Confidence));
        Assert.Equal(report.TotalFindings, report.InconclusiveCount);
    }

    private sealed class StubRule(string ruleId, NodeId nodeId, ValidationFinding duplicated, ValidationFinding distinct)
        : IValidationRule
    {
        public string RuleId => ruleId;
        public string Category => "Test";
        public Severity Severity => Severity.Error;
        public string Description => "Stub rule for engine tests.";

        public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
            node.NodeId == nodeId;

        public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
        {
            yield return duplicated;
            yield return duplicated;
            yield return distinct;
        }
    }
}
