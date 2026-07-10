using System.Text.Json;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Reporting;

public sealed class JsonReporter : IReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly NodeIdDisplayFormatter? _nodeIdFormatter;

    /// <summary>
    /// Creates a JSON reporter. When a <paramref name="nodeIdFormatter"/> is supplied a readable
    /// <c>browseName</c> field (e.g. <c>5:ProductionPrograms</c>) is emitted alongside the raw
    /// <c>nodeId</c>, keeping the output machine-parseable while easier to read.
    /// </summary>
    public JsonReporter(NodeIdDisplayFormatter? nodeIdFormatter = null)
    {
        _nodeIdFormatter = nodeIdFormatter;
    }

    public void Report(ValidationReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        var payload = new
        {
            summary = new
            {
                totalNodes = report.TotalNodes,
                totalFindings = report.TotalFindings,
                bySeverity = new
                {
                    information = report.InformationCount,
                    warning = report.WarningCount,
                    error = report.ErrorCount
                }
            },
            findings = report.Findings.Select(finding => new
            {
                ruleId = finding.RuleId,
                severity = FormatSeverity(finding.Severity),
                nodeId = FormatNodeId(finding.NodeId),
                browseName = _nodeIdFormatter?.TryGetBrowseName(finding.NodeId),
                browsePath = finding.BrowsePath,
                message = finding.Message,
                details = finding.Details,
                referenceUrl = FindingReferenceResolver.Resolve(finding),
                declaringTypeNamespaceUri = finding.DeclaringTypeNamespaceUri,
                declaringTypeReferenceUrl = finding.DeclaringTypeReferenceUrl
            })
        };

        writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static string FormatSeverity(Severity severity) =>
        severity.ToString().ToLowerInvariant();

    private static string FormatNodeId(Opc.Ua.NodeId nodeId) =>
        nodeId.ToString();
}
