using Opc.Ua;
using OpcUaAddressSpaceChecker.Reporting;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Reporting;

public sealed class JsonReporterTests
{
    [Fact]
    public void Report_emits_declaring_type_reference_and_resolved_url()
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
                "https://reference.opcfoundation.org/IA/v101/docs/5.2.5"),
            new ValidationFinding(
                "GEN-10",
                Severity.Warning,
                new NodeId("Widget", 0),
                "Objects/Widget",
                "Object instance is missing a HasTypeDefinition.",
                null)
        };

        var report = new ValidationReport(2, findings.Length, findings);
        var writer = new StringWriter();

        new JsonReporter().Report(report, writer);
        var output = writer.ToString();

        // Deep declaring-type documentation link is surfaced and preferred as referenceUrl.
        Assert.Contains("\"referenceUrl\": \"https://reference.opcfoundation.org/IA/v101/docs/5.2.5\"", output, StringComparison.Ordinal);
        Assert.Contains("\"declaringTypeNamespaceUri\": \"http://opcfoundation.org/UA/IA/\"", output, StringComparison.Ordinal);
        Assert.Contains("\"declaringTypeReferenceUrl\": \"https://reference.opcfoundation.org/IA/v101/docs/5.2.5\"", output, StringComparison.Ordinal);

        // A finding without declaring-type metadata falls back to the per-rule link.
        Assert.Contains("\"referenceUrl\": \"https://reference.opcfoundation.org/specs/OPC-10000-3/6.4.1\"", output, StringComparison.Ordinal);
    }
}
