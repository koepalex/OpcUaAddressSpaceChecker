using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Reporting;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Reporting;

public sealed class ConsoleTableReporterTests
{
    [Fact]
    public void Report_emits_view_and_confidence_summary()
    {
        var finding = new ValidationFinding(
            "GEN-01",
            Severity.Error,
            new NodeId(1u, 2),
            "2:Device/2:SerialNumber",
            "Mandatory child is missing.",
            Confidence: FindingConfidence.Inconclusive);
        var report = new ValidationReport(1, 1, [finding])
        {
            RunMetadata = new ValidationRunMetadata(
                AuthenticationMode.Anonymous,
                ViewCompletenessRequest.Auto,
                ValidationViewState.Restricted,
                "Anonymous view.",
                0)
        };
        var writer = new StringWriter();

        new ConsoleTableReporter().Report(report, writer);
        var output = writer.ToString();

        Assert.Contains("inconclusive=1", output, StringComparison.Ordinal);
        Assert.Contains("effective=Restricted", output, StringComparison.Ordinal);
        Assert.Contains("Confidence", output, StringComparison.Ordinal);
        Assert.Contains("Inconclusive", output, StringComparison.Ordinal);
    }
}
