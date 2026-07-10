using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Configuration;

/// <summary>
/// User-editable configuration (typically loaded from <c>appsettings.json</c>) controlling which
/// findings are reported. It supports two independent controls:
/// <list type="bullet">
/// <item>A suppression list of absolute OPC UA BrowsePaths whose findings are dropped from the
/// report (formatted as <c>namespaceIndex:BrowseName</c> segments joined by <c>/</c>, matching the
/// concrete absolute BrowsePath produced by the address-space browser).</item>
/// <item>Per-rule enable/disable and severity override.</item>
/// </list>
/// Defaults (see <see cref="CreateDefault"/>): every rule enabled at its own declared severity, and
/// the noisy <c>0:Server/0:ServerDiagnostics/0:SessionsDiagnosticsSummary</c> subtree suppressed.
/// </summary>
public sealed class CheckerConfig
{
    /// <summary>
    /// The default suppressed BrowsePath applied when no config file is present: the live server
    /// session-diagnostics summary subtree, which is instance data rather than a modelling concern.
    /// </summary>
    public const string DefaultSessionsDiagnosticsSummaryPath =
        "0:Server/0:ServerDiagnostics/0:SessionsDiagnosticsSummary";

    /// <summary>
    /// Absolute BrowsePaths whose findings (and the findings of any descendant node) are suppressed.
    /// </summary>
    public List<string> SuppressedBrowsePaths { get; set; } = [];

    /// <summary>
    /// Per-rule overrides keyed by RuleId (e.g. <c>GEN-09</c>). Rules absent from this map keep their
    /// defaults: enabled, at their own declared severity.
    /// </summary>
    public Dictionary<string, RuleSetting> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the built-in default configuration used when no config file is found.
    /// </summary>
    public static CheckerConfig CreateDefault() => new()
    {
        SuppressedBrowsePaths = [DefaultSessionsDiagnosticsSummaryPath],
        Rules = new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Normalizes the deserialized state: trims/deduplicates suppressed paths and rebuilds the rule
    /// map with a case-insensitive comparer so RuleId lookups are robust. Returns <c>this</c>.
    /// </summary>
    public CheckerConfig Normalize()
    {
        SuppressedBrowsePaths = SuppressedBrowsePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('/'))
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (Rules.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            Rules = new Dictionary<string, RuleSetting>(Rules, StringComparer.OrdinalIgnoreCase);
        }

        return this;
    }

    /// <summary>
    /// Rule IDs explicitly disabled in the config file.
    /// </summary>
    public IReadOnlyCollection<string> GetDisabledRuleIds() =>
        Rules.Where(entry => !entry.Value.Enabled).Select(entry => entry.Key).ToArray();

    /// <summary>
    /// Resolves an explicit severity override for a rule, if configured and parseable.
    /// </summary>
    public bool TryGetSeverityOverride(string ruleId, out Severity severity)
    {
        severity = default;
        return Rules.TryGetValue(ruleId, out var setting) &&
               !string.IsNullOrWhiteSpace(setting.Severity) &&
               TryParseSeverity(setting.Severity, out severity);
    }

    /// <summary>
    /// True when the given absolute BrowsePath is at, or below, any suppressed path.
    /// </summary>
    public bool IsSuppressed(string? absoluteBrowsePath)
    {
        if (string.IsNullOrEmpty(absoluteBrowsePath))
        {
            return false;
        }

        foreach (var suppressed in SuppressedBrowsePaths)
        {
            if (string.Equals(absoluteBrowsePath, suppressed, StringComparison.Ordinal) ||
                absoluteBrowsePath.StartsWith(suppressed + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a severity token (case-insensitive): <c>Information</c>, <c>Warning</c>, <c>Error</c>.
    /// </summary>
    public static bool TryParseSeverity(string? value, out Severity severity)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "information":
                severity = Severity.Information;
                return true;
            case "warning":
                severity = Severity.Warning;
                return true;
            case "error":
                severity = Severity.Error;
                return true;
            default:
                severity = default;
                return false;
        }
    }
}
