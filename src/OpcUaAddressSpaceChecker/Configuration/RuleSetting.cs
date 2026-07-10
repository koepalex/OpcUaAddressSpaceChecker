namespace OpcUaAddressSpaceChecker.Configuration;

/// <summary>
/// Per-rule configuration entry from the config file. A rule that is not listed keeps its default
/// (enabled, at its own declared severity).
/// </summary>
public sealed class RuleSetting
{
    /// <summary>
    /// Whether the rule runs. Defaults to <c>true</c> when omitted from the config file.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional severity override: <c>Information</c>, <c>Warning</c>, or <c>Error</c>. When unset
    /// the rule's own declared severity is used.
    /// </summary>
    public string? Severity { get; set; }
}
