using Opc.Ua;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Reporting;

/// <summary>
/// Renders a <see cref="ValidationReport"/> as a Markdown document that groups findings per
/// namespace (companion specification). Each namespace section links to its OPC Foundation
/// reference and lists findings in a GitHub-flavored Markdown table with columns BrowsePath,
/// NodeId, Rule violated, Severity, Short description, How to solve, and Evidence.
/// </summary>
public sealed class MarkdownReporter : IReporter
{
    private readonly IReadOnlyDictionary<ushort, string> _namespaceUrisByIndex;
    private readonly IReadOnlyDictionary<NodeId, string> _browsePathsByNodeId;
    private readonly NodeIdDisplayFormatter? _nodeIdFormatter;

    /// <summary>
    /// Creates a reporter with a namespace-index snapshot (index -> URI) used to resolve each
    /// finding's namespace. A snapshot keeps the reporter independent of a live session and unit-testable.
    /// </summary>
    /// <param name="namespaceUrisByIndex">Namespace index to URI snapshot.</param>
    /// <param name="browsePathsByNodeId">
    /// Optional concrete absolute BrowsePath per NodeId (from <see cref="OpcUa.AddressSpaceSnapshot"/>).
    /// When a finding's NodeId is present, its BrowsePath column shows the real address-space path
    /// (with placeholders resolved to concrete instances); otherwise the finding's declared
    /// BrowsePath is used as a fallback.
    /// </param>
    /// <param name="nodeIdFormatter">
    /// Optional formatter that renders the NodeId column as <c>ns:BrowseName (ExpandedNodeId)</c> for
    /// readability. Falls back to the bare NodeId string when not supplied.
    /// </param>
    public MarkdownReporter(
        IReadOnlyDictionary<ushort, string> namespaceUrisByIndex,
        IReadOnlyDictionary<NodeId, string>? browsePathsByNodeId = null,
        NodeIdDisplayFormatter? nodeIdFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(namespaceUrisByIndex);
        _namespaceUrisByIndex = namespaceUrisByIndex;
        _browsePathsByNodeId = browsePathsByNodeId ?? new Dictionary<NodeId, string>();
        _nodeIdFormatter = nodeIdFormatter;
    }

    public void Report(ValidationReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("# OPC UA Address Space Validation Report");
        writer.WriteLine();
        writer.WriteLine(
            $"**Nodes checked:** {report.TotalNodes} &nbsp;&nbsp; " +
            $"**Findings:** {report.TotalFindings} " +
            $"(Errors: {report.ErrorCount}, Warnings: {report.WarningCount}, Information: {report.InformationCount})");
        writer.WriteLine();

        if (report.Findings.Count == 0)
        {
            writer.WriteLine("_No findings at or above the configured severity threshold._");
            return;
        }

        var groups = report.Findings
            .GroupBy(finding => ResolveUri(finding.NodeId), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var spec = CompanionSpecCatalog.Resolve(group.Key);

            writer.WriteLine($"## [{EscapeCell(spec.DisplayName)}]({spec.ReferenceUrl})");
            writer.WriteLine();
            writer.WriteLine($"Namespace: `{EscapeCell(group.Key)}`");
            writer.WriteLine();
            writer.WriteLine("| BrowsePath | NodeId | Rule violated | Severity | Short description | How to solve | Evidence |");
            writer.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");

            foreach (var finding in group
                         .OrderBy(finding => (int)finding.Severity * -1)
                         .ThenBy(finding => finding.BrowsePath, StringComparer.Ordinal))
            {
                var reference = RuleReferenceCatalog.Resolve(finding.RuleId);
                var ruleCell = $"[{EscapeCell(finding.RuleId)}]({FindingReferenceResolver.Resolve(finding)})";
                var browsePath = ResolveBrowsePath(finding);

                writer.WriteLine(
                    $"| {EscapeCell(browsePath)} " +
                    $"| {EscapeCell(FormatNodeId(finding.NodeId))} " +
                    $"| {ruleCell} " +
                    $"| {finding.Severity} " +
                    $"| {EscapeCell(finding.Message)} " +
                    $"| {EscapeCell(reference.Remediation)} " +
                    $"| {EscapeCell(finding.Details ?? string.Empty)} |");
            }

            writer.WriteLine();
        }
    }

    private string ResolveUri(NodeId nodeId)
    {
        if (nodeId is not null && _namespaceUrisByIndex.TryGetValue(nodeId.NamespaceIndex, out var uri) &&
            !string.IsNullOrWhiteSpace(uri))
        {
            return uri;
        }

        return "(unknown namespace)";
    }

    private string ResolveBrowsePath(ValidationFinding finding)
    {
        if (finding.NodeId is not null &&
            _browsePathsByNodeId.TryGetValue(finding.NodeId, out var path) &&
            !string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return finding.BrowsePath;
    }

    private string FormatNodeId(NodeId nodeId) =>
        _nodeIdFormatter?.Format(nodeId) ?? nodeId.ToString();

    private static string EscapeCell(string value) =>
        Clean(value)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static string Clean(string value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
