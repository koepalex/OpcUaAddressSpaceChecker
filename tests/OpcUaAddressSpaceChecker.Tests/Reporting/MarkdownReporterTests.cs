using Opc.Ua;
using OpcUaAddressSpaceChecker.Reporting;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Reporting;

public sealed class MarkdownReporterTests
{
    private const string CoreNamespace = "http://opcfoundation.org/UA/";
    private const string DiNamespace = "http://opcfoundation.org/UA/DI/";

    private static readonly IReadOnlyDictionary<ushort, string> NamespaceSnapshot =
        new Dictionary<ushort, string>
        {
            [0] = CoreNamespace,
            [2] = DiNamespace
        };

    [Fact]
    public void Report_groups_findings_per_namespace_with_reference_links_and_columns()
    {
        var findings = new[]
        {
            new ValidationFinding(
                "DI-01",
                Severity.Error,
                new NodeId("Device1", 2),
                "Objects/DeviceSet/Device1",
                "Missing mandatory DI nameplate property Manufacturer.",
                "Expected Manufacturer; actual none."),
            new ValidationFinding(
                "GEN-10",
                Severity.Warning,
                new NodeId("Widget", 0),
                "Objects/Widget",
                "Object instance is missing a HasTypeDefinition.",
                null)
        };

        var report = new ValidationReport(42, findings.Length, findings);
        var writer = new StringWriter();

        new MarkdownReporter(NamespaceSnapshot).Report(report, writer);
        var output = writer.ToString();

        Assert.Contains("# OPC UA Address Space Validation Report", output, StringComparison.Ordinal);
        Assert.Contains("**Nodes checked:** 42", output, StringComparison.Ordinal);

        Assert.Contains("## [Devices (DI)](https://reference.opcfoundation.org/DI/docs/)", output, StringComparison.Ordinal);
        Assert.Contains("## [OPC UA Core](https://reference.opcfoundation.org/Core/docs/)", output, StringComparison.Ordinal);

        Assert.Contains(
            "| BrowsePath | NodeId | Rule violated | Severity | Short description | How to solve | Evidence |",
            output,
            StringComparison.Ordinal);

        Assert.Contains("| Objects/DeviceSet/Device1 |", output, StringComparison.Ordinal);
        Assert.Contains("[DI-01](https://reference.opcfoundation.org/DI/docs/)", output, StringComparison.Ordinal);
        Assert.Contains("| Error |", output, StringComparison.Ordinal);
        Assert.Contains("Missing mandatory DI nameplate property Manufacturer.", output, StringComparison.Ordinal);
        Assert.Contains("Add Manufacturer, Model, HardwareRevision", output, StringComparison.Ordinal);
        Assert.Contains("Expected Manufacturer; actual none.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_escapes_pipes_and_newlines_in_cells()
    {
        var findings = new[]
        {
            new ValidationFinding(
                "GEN-05",
                Severity.Warning,
                new NodeId("Node", 2),
                "Objects/Node",
                "Value is a|b",
                "Line1\nLine2")
        };

        var report = new ValidationReport(1, findings.Length, findings);
        var writer = new StringWriter();

        new MarkdownReporter(NamespaceSnapshot).Report(report, writer);
        var output = writer.ToString();

        Assert.Contains("Value is a\\|b", output, StringComparison.Ordinal);
        Assert.Contains("Line1 Line2", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Line1\nLine2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_prefers_declaring_type_namespace_link_over_rule_id_link()
    {
        var findings = new[]
        {
            new ValidationFinding(
                "GEN-01",
                Severity.Error,
                new NodeId("Light0", 2),
                "Light0/SignalColor",
                "Mandatory child is missing.",
                "Placeholder instance Light0 is missing SignalColor.",
                "http://opcfoundation.org/UA/IA/"),
            new ValidationFinding(
                "GEN-01",
                Severity.Error,
                new NodeId("Widget", 0),
                "Objects/Widget/Something",
                "Mandatory child is missing.",
                null)
        };

        var report = new ValidationReport(2, findings.Length, findings);
        var writer = new StringWriter();

        new MarkdownReporter(NamespaceSnapshot).Report(report, writer);
        var output = writer.ToString();

        Assert.Contains("[GEN-01](https://reference.opcfoundation.org/IA/docs/)", output, StringComparison.Ordinal);
        Assert.Contains("[GEN-01](https://reference.opcfoundation.org/specs/OPC-10000-3/6.4.4.4.1)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_prefers_declaring_type_documentation_url_over_namespace_and_rule_links()
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

        new MarkdownReporter(NamespaceSnapshot).Report(report, writer);
        var output = writer.ToString();

        Assert.Contains("[GEN-01](https://reference.opcfoundation.org/IA/v101/docs/5.2.5)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("[GEN-01](https://reference.opcfoundation.org/IA/docs/)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_html_encodes_angle_brackets_so_placeholder_text_is_visible()
    {
        var findings = new[]
        {
            new ValidationFinding(
                "GEN-06",
                Severity.Error,
                new NodeId(58596, 2),
                "0:<OrderedObject>/5:ProductionPrograms/0:<OrderedObject>",
                "MandatoryPlaceholder declaration has no matching child.",
                "Expected at least one child with ReferenceType i=49 and TypeDefinition ns=5;i=59 or subtypes.")
        };

        var report = new ValidationReport(1, findings.Length, findings);
        var writer = new StringWriter();

        new MarkdownReporter(NamespaceSnapshot).Report(report, writer);
        var output = writer.ToString();

        Assert.Contains("&lt;OrderedObject&gt;", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<OrderedObject>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_uses_absolute_address_space_path_from_node_map_when_available()
    {
        var nodeId = new NodeId(58596, 2);
        var findings = new[]
        {
            new ValidationFinding(
                "GEN-06",
                Severity.Error,
                nodeId,
                "0:<OrderedObject>/5:ProductionPrograms/0:<OrderedObject>",
                "MandatoryPlaceholder declaration has no matching child.",
                "Expected at least one child with ReferenceType i=49 and TypeDefinition ns=5;i=59 or subtypes.")
        };

        var browsePaths = new Dictionary<NodeId, string>
        {
            [nodeId] = "13:FullMachineTool/5:Production/13:My Job1/5:ProductionPrograms"
        };

        var report = new ValidationReport(1, findings.Length, findings);
        var writer = new StringWriter();

        new MarkdownReporter(NamespaceSnapshot, browsePaths).Report(report, writer);
        var output = writer.ToString();

        // BrowsePath column shows the concrete address-space path, not the abstract declaration path.
        Assert.Contains("| 13:FullMachineTool/5:Production/13:My Job1/5:ProductionPrograms |", output, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderedObject", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_renders_nodeid_column_with_browsename_when_formatter_supplied()
    {
        var nodeId = new NodeId(58596u, 2);
        var findings = new[]
        {
            new ValidationFinding(
                "GEN-06",
                Severity.Error,
                nodeId,
                "Objects/Node",
                "MandatoryPlaceholder declaration has no matching child.",
                null)
        };

        var formatter = new NodeIdDisplayFormatter(
            NamespaceSnapshot,
            new Dictionary<NodeId, string> { [nodeId] = "5:ProductionPrograms" });

        var report = new ValidationReport(1, findings.Length, findings);
        var writer = new StringWriter();

        new MarkdownReporter(NamespaceSnapshot, nodeIdFormatter: formatter).Report(report, writer);
        var output = writer.ToString();

        Assert.Contains(
            "| 5:ProductionPrograms (nsu=http://opcfoundation.org/UA/DI/;i=58596) |",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Report_places_information_findings_in_their_own_optional_section()
    {
        var findings = new[]
        {
            new ValidationFinding(
                "DI-01",
                Severity.Error,
                new NodeId("Device1", 2),
                "Objects/DeviceSet/Device1",
                "Missing mandatory DI nameplate property Manufacturer.",
                null),
            new ValidationFinding(
                "DI-05",
                Severity.Information,
                new NodeId("Device1", 2),
                "Objects/DeviceSet/Device1/ManufacturerUri",
                "Optional interface member 'ManufacturerUri' is not implemented.",
                "Optional HasInterface-derived declaration path is not materialized.")
        };

        var report = new ValidationReport(1, findings.Length, findings);
        var writer = new StringWriter();

        new MarkdownReporter(NamespaceSnapshot).Report(report, writer);
        var output = writer.ToString();

        var infoSectionIndex = output.IndexOf(
            "## Optional members not implemented (informational)",
            StringComparison.Ordinal);
        Assert.True(infoSectionIndex > 0, "Expected a dedicated informational section.");

        // The optional-member message appears only after the informational section header, and the
        // error appears before it (violations first).
        var errorIndex = output.IndexOf("Missing mandatory DI nameplate property", StringComparison.Ordinal);
        var infoMessageIndex = output.IndexOf("Optional interface member 'ManufacturerUri'", StringComparison.Ordinal);
        Assert.True(errorIndex >= 0 && errorIndex < infoSectionIndex, "Error should render before the informational section.");
        Assert.True(infoMessageIndex > infoSectionIndex, "Informational finding should render inside the informational section.");
    }

    [Fact]
    public void Report_with_no_findings_emits_note_and_no_table()
    {
        var report = new ValidationReport(10, 0, []);
        var writer = new StringWriter();

        new MarkdownReporter(NamespaceSnapshot).Report(report, writer);
        var output = writer.ToString();

        Assert.Contains("_No findings", output, StringComparison.Ordinal);
        Assert.DoesNotContain("| BrowseName |", output, StringComparison.Ordinal);
    }
}
