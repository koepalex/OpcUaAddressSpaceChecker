using Opc.Ua;
using OpcUaAddressSpaceChecker.Configuration;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Configuration;

public sealed class FindingFilterTests
{
    private static ValidationFinding Finding(string ruleId, Severity severity, NodeId nodeId, string browsePath) =>
        new(ruleId, severity, nodeId, browsePath, "message", null);

    [Fact]
    public void Apply_suppresses_findings_at_or_below_configured_path_using_snapshot()
    {
        var suppressedNode = new NodeId(1, 0);
        var keptNode = new NodeId(2, 0);
        var findings = new[]
        {
            Finding("GEN-01", Severity.Error, suppressedNode, "irrelevant"),
            Finding("GEN-01", Severity.Error, keptNode, "irrelevant"),
        };
        var paths = new Dictionary<NodeId, string>
        {
            [suppressedNode] = "0:Server/0:ServerDiagnostics/0:SessionsDiagnosticsSummary/0:SessionArray",
            [keptNode] = "0:Objects/2:Machine",
        };

        var result = FindingFilter.Apply(findings, paths, CheckerConfig.CreateDefault());

        Assert.Equal(1, result.SuppressedCount);
        Assert.Single(result.Findings);
        Assert.Equal(keptNode, result.Findings[0].NodeId);
    }

    [Fact]
    public void Apply_falls_back_to_finding_browsepath_when_node_absent_from_snapshot()
    {
        var node = new NodeId(9, 0);
        var findings = new[]
        {
            Finding("GEN-01", Severity.Error, node, "0:Server/0:ServerDiagnostics/0:SessionsDiagnosticsSummary"),
        };

        var result = FindingFilter.Apply(findings, new Dictionary<NodeId, string>(), CheckerConfig.CreateDefault());

        Assert.Equal(1, result.SuppressedCount);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Apply_overrides_severity_for_configured_rule_and_leaves_others()
    {
        var node = new NodeId(3, 0);
        var findings = new[]
        {
            Finding("GEN-09", Severity.Error, node, "0:Objects/2:A"),
            Finding("GEN-05", Severity.Warning, node, "0:Objects/2:A"),
        };
        var config = CheckerConfigLoader.Parse("""
        { "Rules": { "GEN-09": { "Severity": "Warning" } } }
        """);

        var result = FindingFilter.Apply(findings, new Dictionary<NodeId, string>(), config);

        var gen09 = Assert.Single(result.Findings, f => f.RuleId == "GEN-09");
        Assert.Equal(Severity.Warning, gen09.Severity);
        var gen05 = Assert.Single(result.Findings, f => f.RuleId == "GEN-05");
        Assert.Equal(Severity.Warning, gen05.Severity); // unchanged (its own default)
        Assert.Equal(0, result.SuppressedCount);
    }

    [Fact]
    public void Apply_with_no_suppressed_paths_keeps_all_findings()
    {
        var node = new NodeId(4, 0);
        var config = new CheckerConfig().Normalize(); // empty: no suppression, no overrides
        var findings = new[] { Finding("GEN-01", Severity.Error, node, "0:Server/0:X") };

        var result = FindingFilter.Apply(findings, new Dictionary<NodeId, string>(), config);

        Assert.Single(result.Findings);
        Assert.Equal(0, result.SuppressedCount);
    }
}
