using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Reporting;

public sealed class ConsoleTableReporter : IReporter
{
    private static readonly Severity[] SeverityOrder =
    [
        Severity.Error,
        Severity.Warning,
        Severity.Information
    ];

    private readonly NodeIdDisplayFormatter? _nodeIdFormatter;

    /// <summary>
    /// Creates a console reporter. When a <paramref name="nodeIdFormatter"/> is supplied the NodeId
    /// column is rendered as <c>ns:BrowseName (ExpandedNodeId)</c>; otherwise the bare NodeId is shown.
    /// </summary>
    public ConsoleTableReporter(NodeIdDisplayFormatter? nodeIdFormatter = null)
    {
        _nodeIdFormatter = nodeIdFormatter;
    }

    public void Report(ValidationReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine(
            $"Summary: totalNodes={report.TotalNodes}, totalFindings={report.TotalFindings}, errors={report.ErrorCount}, warnings={report.WarningCount}, information={report.InformationCount}");

        if (report.TotalFindings == 0)
        {
            writer.WriteLine("No findings.");
            return;
        }

        foreach (var severity in SeverityOrder)
        {
            var findings = report.Findings
                .Where(finding => finding.Severity == severity)
                .ToArray();

            if (findings.Length == 0)
            {
                continue;
            }

            writer.WriteLine();
            writer.WriteLine($"{FormatSeverity(severity)} findings ({findings.Length})");
            WriteTable(findings, writer);
        }
    }

    private void WriteTable(IReadOnlyCollection<ValidationFinding> findings, TextWriter writer)
    {
        var rows = findings
            .Select(finding => new[]
            {
                Clean(finding.RuleId),
                FormatSeverity(finding.Severity),
                Clean(FormatNodeId(finding.NodeId)),
                Clean(finding.BrowsePath),
                Clean(finding.Message)
            })
            .ToArray();

        var headers = new[] { "RuleId", "Severity", "NodeId", "BrowsePath", "Message" };
        var widths = Enumerable.Range(0, headers.Length)
            .Select(index => Math.Max(headers[index].Length, rows.Max(row => row[index].Length)))
            .ToArray();

        WriteRow(headers, widths, writer);
        WriteRow(widths.Select(width => new string('-', width)).ToArray(), widths, writer);

        foreach (var row in rows)
        {
            WriteRow(row, widths, writer);
        }
    }

    private static void WriteRow(IReadOnlyList<string> values, IReadOnlyList<int> widths, TextWriter writer)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                writer.Write("  ");
            }

            writer.Write(values[index].PadRight(widths[index]));
        }

        writer.WriteLine();
    }

    private static string FormatSeverity(Severity severity) =>
        severity.ToString().ToUpperInvariant();

    private string FormatNodeId(Opc.Ua.NodeId nodeId) =>
        _nodeIdFormatter?.Format(nodeId) ?? nodeId.ToString();

    private static string Clean(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');
}
