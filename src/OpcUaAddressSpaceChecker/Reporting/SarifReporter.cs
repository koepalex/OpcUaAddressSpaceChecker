using System.Reflection;
using System.Text.Json;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Reporting;

public sealed class SarifReporter : IReporter
{
    private const string ToolName = "OpcUaAddressSpaceChecker";
    private const string InformationUri = "https://github.com/koepalex/OpcUaAddressSpaceChecker";
    private const string SarifSchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";

    private static readonly string ToolVersion = GetToolVersion();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly NodeIdDisplayFormatter? _nodeIdFormatter;

    /// <summary>
    /// Creates a SARIF reporter. When a <paramref name="nodeIdFormatter"/> is supplied a readable
    /// <c>browseName</c> property is emitted alongside the raw <c>nodeId</c>, keeping the SARIF
    /// output schema-valid and machine-parseable while easier to read.
    /// </summary>
    public SarifReporter(NodeIdDisplayFormatter? nodeIdFormatter = null)
    {
        _nodeIdFormatter = nodeIdFormatter;
    }

    public void Report(ValidationReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        var rules = report.Findings
            .GroupBy(finding => finding.RuleId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                id = group.Key,
                name = group.Key,
                shortDescription = new
                {
                    text = $"OPC UA validation rule {group.Key}"
                },
                helpUri = RuleReferenceCatalog.Resolve(group.Key).ReferenceUrl,
                defaultConfiguration = new
                {
                    level = MapLevel(GetHighestSeverity(group))
                }
            })
            .ToArray();

        var results = report.Findings
            .Select(finding => new
            {
                ruleId = finding.RuleId,
                level = MapLevel(finding.Severity),
                message = new
                {
                    text = finding.Message
                },
                locations = new[]
                {
                    new
                    {
                        logicalLocations = new[]
                        {
                            new
                            {
                                name = GetLogicalLocationName(finding),
                                fullyQualifiedName = GetFullyQualifiedName(finding),
                                kind = "object"
                            }
                        },
                        properties = new
                        {
                            nodeId = FormatNodeId(finding.NodeId),
                            browseName = _nodeIdFormatter?.TryGetBrowseName(finding.NodeId),
                            browsePath = finding.BrowsePath
                        }
                    }
                },
                properties = new
                {
                    details = finding.Details,
                    referenceUrl = FindingReferenceResolver.Resolve(finding),
                    declaringTypeNamespaceUri = finding.DeclaringTypeNamespaceUri,
                    declaringTypeReferenceUrl = finding.DeclaringTypeReferenceUrl
                }
            })
            .ToArray();

        var run = new
        {
            tool = new
            {
                driver = new
                {
                    name = ToolName,
                    version = ToolVersion,
                    informationUri = InformationUri,
                    rules
                }
            },
            results
        };

        var root = new Dictionary<string, object?>
        {
            ["$schema"] = SarifSchemaUri,
            ["version"] = "2.1.0",
            ["runs"] = new[] { run }
        };

        writer.WriteLine(JsonSerializer.Serialize(root, JsonOptions));
    }

    private static Severity GetHighestSeverity(IEnumerable<ValidationFinding> findings)
    {
        var highestSeverity = Severity.Information;

        foreach (var finding in findings)
        {
            if ((int)finding.Severity > (int)highestSeverity)
            {
                highestSeverity = finding.Severity;
            }
        }

        return highestSeverity;
    }

    private static string MapLevel(Severity severity) =>
        severity switch
        {
            Severity.Error => "error",
            Severity.Warning => "warning",
            Severity.Information => "note",
            _ => "none"
        };

    private static string GetLogicalLocationName(ValidationFinding finding)
    {
        var browsePath = Clean(finding.BrowsePath);
        return string.IsNullOrWhiteSpace(browsePath)
            ? FormatNodeId(finding.NodeId)
            : browsePath;
    }

    private static string GetFullyQualifiedName(ValidationFinding finding)
    {
        var nodeId = FormatNodeId(finding.NodeId);
        var browsePath = Clean(finding.BrowsePath);

        if (string.IsNullOrWhiteSpace(browsePath))
        {
            return nodeId;
        }

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return browsePath;
        }

        return $"{browsePath} [{nodeId}]";
    }

    private static string FormatNodeId(Opc.Ua.NodeId nodeId) =>
        nodeId.ToString();

    private static string Clean(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');

    private static string GetToolVersion()
    {
        var assembly = typeof(SarifReporter).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString()
            : informationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }

        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex < 0 ? version : version[..metadataIndex];
    }
}
