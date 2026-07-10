using Opc.Ua;
using OpcUaAddressSpaceChecker.Reporting;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Reporting;

public sealed class SarifReporterTests
{
    [Fact]
    public void Report_emits_rule_help_uri_and_result_reference_url()
    {
        var findings = new[]
        {
            new ValidationFinding(
                "GEN-01",
                Severity.Error,
                new NodeId("Element0", 2),
                "Element0/NumberInList",
                "Mandatory child is missing.",
                "Placeholder instance Element0 is missing NumberInList.",
                "http://opcfoundation.org/UA/IA/",
                "https://reference.opcfoundation.org/IA/v101/docs/5.2.5")
        };

        var report = new ValidationReport(1, findings.Length, findings);
        var writer = new StringWriter();

        new SarifReporter().Report(report, writer);
        var output = writer.ToString();

        // Rule-level stable help link.
        Assert.Contains("\"helpUri\": \"https://reference.opcfoundation.org/specs/OPC-10000-3/6.4.4.4.1\"", output, StringComparison.Ordinal);

        // Per-result resolved reference (deep declaring-type documentation link).
        Assert.Contains("\"referenceUrl\": \"https://reference.opcfoundation.org/IA/v101/docs/5.2.5\"", output, StringComparison.Ordinal);
        Assert.Contains("\"declaringTypeReferenceUrl\": \"https://reference.opcfoundation.org/IA/v101/docs/5.2.5\"", output, StringComparison.Ordinal);
    }
}
