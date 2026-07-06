namespace OpcUaAddressSpaceChecker.Validation;

public sealed record ValidationReport(
    int TotalNodes,
    int TotalFindings,
    IReadOnlyList<ValidationFinding> Findings)
{
    public int InformationCount => CountBySeverity(Severity.Information);
    public int WarningCount => CountBySeverity(Severity.Warning);
    public int ErrorCount => CountBySeverity(Severity.Error);

    public IReadOnlyDictionary<Severity, int> SeverityCounts =>
        Enum.GetValues<Severity>().ToDictionary(severity => severity, CountBySeverity);

    public int CountBySeverity(Severity severity) =>
        Findings.Count(finding => finding.Severity == severity);
}
