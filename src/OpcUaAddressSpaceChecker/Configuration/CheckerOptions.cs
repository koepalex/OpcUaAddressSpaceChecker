using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Configuration;

/// <summary>
/// Configuration options for an address space validation run.
/// </summary>
public class CheckerOptions : OpcUaClientOptions
{
    /// <summary>
    /// Explicit NodeSet2 XML files to load.
    /// </summary>
    public string[] NodesetPaths { get; set; } = [];

    /// <summary>
    /// Directories searched for companion specification NodeSet2 XML files when NodeSet2 overrides are supplied.
    /// </summary>
    public string[] NodesetSearchDirs { get; set; } = [];

    /// <summary>
    /// Optional ObjectType or VariableType ExpandedNodeId used to limit validation to instances of
    /// that type or its subtypes.
    /// </summary>
    public string? TargetTypeId { get; set; }

    /// <summary>
    /// Output format: console, json, sarif, or markdown.
    /// </summary>
    public string OutputFormat { get; set; } = "console";

    /// <summary>
    /// Optional output file path. Console output writes to stdout when unset.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Minimum severity included in the final result.
    /// </summary>
    public string SeverityThreshold { get; set; } = "warning";

    /// <summary>
    /// Rule IDs to include. Empty means include all discovered rules.
    /// </summary>
    public string[] IncludeRuleIds { get; set; } = [];

    /// <summary>
    /// Rule IDs to exclude from the run.
    /// </summary>
    public string[] ExcludeRuleIds { get; set; } = [];

    /// <summary>
    /// Optional path to the appsettings.json config file (suppressed BrowsePaths and per-rule
    /// enable/severity overrides). When null, the file is auto-discovered; built-in defaults apply
    /// when no file is found.
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Requested validation-view completeness policy: auto, complete, or restricted.
    /// </summary>
    public string ViewCompleteness { get; set; } = "auto";

    /// <summary>
    /// Fail before validation when the effective validation view is restricted.
    /// </summary>
    public bool RequireCompleteView { get; set; }

    /// <summary>
    /// Promote undeclared instance-specific children from advisory to Warning.
    /// </summary>
    public bool StrictTypeCoverage { get; set; }
}
