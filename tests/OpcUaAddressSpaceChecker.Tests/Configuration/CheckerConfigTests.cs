using Opc.Ua;
using OpcUaAddressSpaceChecker.Configuration;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Configuration;

public sealed class CheckerConfigTests
{
    [Fact]
    public void CreateDefault_suppresses_sessions_diagnostics_summary_and_enables_all_rules()
    {
        var config = CheckerConfig.CreateDefault();

        Assert.Contains(CheckerConfig.DefaultSessionsDiagnosticsSummaryPath, config.SuppressedBrowsePaths);
        Assert.Empty(config.GetDisabledRuleIds());
    }

    [Theory]
    [InlineData("0:Server/0:ServerDiagnostics/0:SessionsDiagnosticsSummary", true)]           // exact
    [InlineData("0:Server/0:ServerDiagnostics/0:SessionsDiagnosticsSummary/0:SessionArray", true)] // descendant
    [InlineData("0:Server/0:ServerDiagnostics", false)]                                        // ancestor only
    [InlineData("0:Server/0:ServerDiagnostics/0:SessionsDiagnosticsSummaryX", false)]          // prefix, not a segment boundary
    [InlineData("0:Objects/2:Foo", false)]
    [InlineData(null, false)]
    public void IsSuppressed_matches_at_or_below_configured_path(string? path, bool expected)
    {
        var config = CheckerConfig.CreateDefault();

        Assert.Equal(expected, config.IsSuppressed(path));
    }

    [Fact]
    public void Parse_reads_section_form_with_suppressions_and_rule_overrides()
    {
        const string json = """
        {
          "OpcUaAddressSpaceChecker": {
            "SuppressedBrowsePaths": [ "0:Server/0:ServerDiagnostics", "2:Line/2:Noise" ],
            "Rules": {
              "GEN-05": { "Enabled": false },
              "GEN-09": { "Severity": "Warning" }
            }
          }
        }
        """;

        var config = CheckerConfigLoader.Parse(json);

        Assert.Equal(new[] { "0:Server/0:ServerDiagnostics", "2:Line/2:Noise" }, config.SuppressedBrowsePaths);
        Assert.Equal(new[] { "GEN-05" }, config.GetDisabledRuleIds());
        Assert.True(config.TryGetSeverityOverride("GEN-09", out var severity));
        Assert.Equal(Severity.Warning, severity);
        Assert.False(config.TryGetSeverityOverride("GEN-05", out _));
    }

    [Fact]
    public void Parse_reads_root_form_and_is_rule_id_case_insensitive()
    {
        const string json = """
        {
          "SuppressedBrowsePaths": [ "0:A" ],
          "Rules": { "gen-09": { "Enabled": false, "Severity": "Error" } }
        }
        """;

        var config = CheckerConfigLoader.Parse(json);

        Assert.Contains("0:A", config.SuppressedBrowsePaths);
        Assert.True(config.TryGetSeverityOverride("GEN-09", out var severity));
        Assert.Equal(Severity.Error, severity);
        Assert.Contains("gen-09", config.GetDisabledRuleIds());
    }

    [Fact]
    public void Parse_empty_or_missing_json_returns_defaults()
    {
        var config = CheckerConfigLoader.Parse("   ");

        Assert.Contains(CheckerConfig.DefaultSessionsDiagnosticsSummaryPath, config.SuppressedBrowsePaths);
    }

    [Fact]
    public void ResolveConfigPath_throws_for_missing_explicit_path()
    {
        Assert.Throws<FileNotFoundException>(() =>
            CheckerConfigLoader.ResolveConfigPath(Path.Combine(Path.GetTempPath(), "does-not-exist-42.json")));
    }
}
