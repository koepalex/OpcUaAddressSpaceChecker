using Opc.Ua;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Configuration;

/// <summary>
/// Applies <see cref="CheckerConfig"/> post-processing to a run's findings: per-rule severity
/// overrides and BrowsePath-based suppression. Pure and side-effect free so it is unit-testable in
/// isolation from a live server.
/// </summary>
public static class FindingFilter
{
    /// <summary>The outcome of applying the config to a set of findings.</summary>
    /// <param name="Findings">The retained findings, with any severity overrides applied.</param>
    /// <param name="SuppressedCount">How many findings were dropped by BrowsePath suppression.</param>
    public sealed record Result(IReadOnlyList<ValidationFinding> Findings, int SuppressedCount);

    /// <summary>
    /// Applies severity overrides first (so suppression and later threshold filtering see the
    /// overridden severity), then removes findings whose node's absolute BrowsePath is at or below a
    /// suppressed path.
    /// </summary>
    /// <param name="findings">The raw findings from the validation run.</param>
    /// <param name="browsePathsByNodeId">Absolute BrowsePath per NodeId from the address-space snapshot.</param>
    /// <param name="config">The active configuration.</param>
    public static Result Apply(
        IEnumerable<ValidationFinding> findings,
        IReadOnlyDictionary<NodeId, string> browsePathsByNodeId,
        CheckerConfig config,
        bool strictTypeCoverage = false)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(browsePathsByNodeId);
        ArgumentNullException.ThrowIfNull(config);

        var retained = new List<ValidationFinding>();
        var suppressed = 0;

        foreach (var finding in findings)
        {
            if (IsSuppressed(finding, browsePathsByNodeId, config))
            {
                suppressed++;
                continue;
            }

            retained.Add(ApplySeverityOverride(finding, config, strictTypeCoverage));
        }

        return new Result(retained, suppressed);
    }

    private static bool IsSuppressed(
        ValidationFinding finding,
        IReadOnlyDictionary<NodeId, string> browsePathsByNodeId,
        CheckerConfig config)
    {
        if (config.SuppressedBrowsePaths.Count == 0)
        {
            return false;
        }

        // Prefer the concrete absolute path from the snapshot; fall back to the finding's own
        // BrowsePath (already an absolute path for node-rooted findings) when the node is not in the
        // live snapshot (e.g. a type-model node).
        var path = browsePathsByNodeId.TryGetValue(finding.NodeId, out var absolute)
            ? absolute
            : finding.BrowsePath;

        return config.IsSuppressed(path);
    }

    private static ValidationFinding ApplySeverityOverride(
        ValidationFinding finding,
        CheckerConfig config,
        bool strictTypeCoverage)
    {
        if (config.TryGetSeverityOverride(finding.RuleId, out var configuredSeverity))
        {
            return configuredSeverity != finding.Severity
                ? finding with { Severity = configuredSeverity }
                : finding;
        }

        return strictTypeCoverage &&
               string.Equals(finding.RuleId, "GEN-05", StringComparison.OrdinalIgnoreCase) &&
               finding.Severity == Severity.Information
            ? finding with { Severity = Severity.Warning }
            : finding;
    }
}
