using OpcUaAddressSpaceChecker.Commands;
using OpcUaAddressSpaceChecker.Reporting;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Reporting;

public sealed class RuleReferenceCatalogTests
{
    private const string FallbackRemediation =
        "Review the finding details and align the node with the referenced OPC UA specification.";

    [Fact]
    public void Every_registered_rule_has_a_catalog_entry()
    {
        var registry = new RuleRegistry();
        registry.AutoDiscover(typeof(CheckCommand).Assembly);

        Assert.NotEmpty(registry.Rules);

        foreach (var rule in registry.Rules)
        {
            var reference = RuleReferenceCatalog.Resolve(rule.RuleId);

            Assert.False(
                string.IsNullOrWhiteSpace(reference.Remediation),
                $"Rule '{rule.RuleId}' has no remediation text in RuleReferenceCatalog.");
            Assert.NotEqual(FallbackRemediation, reference.Remediation);
            Assert.False(
                string.IsNullOrWhiteSpace(reference.ReferenceUrl),
                $"Rule '{rule.RuleId}' has no reference URL in RuleReferenceCatalog.");
        }
    }

    [Fact]
    public void Resolve_returns_fallback_for_unknown_rule()
    {
        var reference = RuleReferenceCatalog.Resolve("DOES-NOT-EXIST");

        Assert.Equal(FallbackRemediation, reference.Remediation);
        Assert.Equal(CompanionSpecCatalog.ReferenceRootUrl, reference.ReferenceUrl);
    }

    [Theory]
    [InlineData("GEN-01", "https://reference.opcfoundation.org/specs/OPC-10000-3/6.4.4.4.1")]
    [InlineData("GEN-14", "https://reference.opcfoundation.org/specs/OPC-10000-3/5.2.4")]
    public void Core_rules_resolve_to_per_section_deep_links(string ruleId, string expectedUrl)
    {
        var reference = RuleReferenceCatalog.Resolve(ruleId);

        Assert.Equal(expectedUrl, reference.ReferenceUrl);
    }

    [Fact]
    public void Companion_spec_rules_keep_their_namespace_reference_links()
    {
        var reference = RuleReferenceCatalog.Resolve("DI-01");

        Assert.Equal("https://reference.opcfoundation.org/DI/docs/", reference.ReferenceUrl);
    }
}
