namespace OpcUaAddressSpaceChecker.Validation;

public sealed record ValidationReport(
    int TotalNodes,
    int TotalFindings,
    IReadOnlyList<ValidationFinding> Findings)
{
    public ValidationRunMetadata RunMetadata { get; init; } = ValidationRunMetadata.Default;

    public int InformationCount => CountBySeverity(Severity.Information);
    public int WarningCount => CountBySeverity(Severity.Warning);
    public int ErrorCount => CountBySeverity(Severity.Error);
    public int ConfirmedCount => CountByConfidence(FindingConfidence.Confirmed);
    public int InconclusiveCount => CountByConfidence(FindingConfidence.Inconclusive);

    public IReadOnlyDictionary<Severity, int> SeverityCounts =>
        Enum.GetValues<Severity>().ToDictionary(severity => severity, CountBySeverity);

    public int CountBySeverity(Severity severity) =>
        Findings.Count(finding => finding.Severity == severity);

    public int CountByConfidence(FindingConfidence confidence) =>
        Findings.Count(finding => finding.Confidence == confidence);
}
