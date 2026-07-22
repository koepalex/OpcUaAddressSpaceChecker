using Opc.Ua;
using OpcUaAddressSpaceChecker.Reporting;
using OpcUaAddressSpaceChecker.Validation;
using OpcUaAddressSpaceChecker.OpcUa;

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

        var report = new ValidationReport(1, findings.Length, findings)
        {
            RunMetadata = ValidationViewPolicy.Evaluate(
                AuthenticationMode.UserName,
                ViewCompletenessRequest.Auto,
                accessDeniedCount: 0)
        };
        var writer = new StringWriter();

        new SarifReporter().Report(report, writer);
        var output = writer.ToString();

        // Rule-level stable help link.
        Assert.Contains("\"helpUri\": \"https://reference.opcfoundation.org/specs/OPC-10000-3/6.4.4.4.1\"", output, StringComparison.Ordinal);

        // Per-result resolved reference (deep declaring-type documentation link).
        Assert.Contains("\"referenceUrl\": \"https://reference.opcfoundation.org/IA/v101/docs/5.2.5\"", output, StringComparison.Ordinal);
        Assert.Contains("\"declaringTypeReferenceUrl\": \"https://reference.opcfoundation.org/IA/v101/docs/5.2.5\"", output, StringComparison.Ordinal);
        Assert.Contains("\"confidence\": \"confirmed\"", output, StringComparison.Ordinal);
        Assert.Contains("\"effectiveViewState\": \"AssumedComplete\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_demotes_inconclusive_results_to_note_and_preserves_original_severity()
    {
        var finding = new ValidationFinding(
            "GEN-01",
            Severity.Error,
            new NodeId("Device", 2),
            "2:Device/2:SerialNumber",
            "Mandatory child is missing.",
            Confidence: FindingConfidence.Inconclusive);
        var writer = new StringWriter();

        new SarifReporter().Report(new ValidationReport(1, 1, [finding]), writer);
        var output = writer.ToString();

        Assert.Contains("\"level\": \"note\"", output, StringComparison.Ordinal);
        Assert.Contains("\"originalSeverity\": \"error\"", output, StringComparison.Ordinal);
        Assert.Contains("\"confidence\": \"inconclusive\"", output, StringComparison.Ordinal);
    }
}
