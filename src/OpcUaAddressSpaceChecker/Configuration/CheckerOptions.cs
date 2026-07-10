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
}
