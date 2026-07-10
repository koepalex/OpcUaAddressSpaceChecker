using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Reporting;

/// <summary>
/// Resolves the single best reference URL for a finding, shared by all report formats so the
/// preference order stays consistent: the declaring type's deep documentation link (from the
/// NodeSet2 <c>Documentation</c> element), then the declaring companion specification's namespace
/// docs link (via <see cref="CompanionSpecCatalog"/>), then the per-rule link (via
/// <see cref="RuleReferenceCatalog"/>).
/// </summary>
internal static class FindingReferenceResolver
{
    public static string Resolve(ValidationFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        if (!string.IsNullOrWhiteSpace(finding.DeclaringTypeReferenceUrl))
        {
            return finding.DeclaringTypeReferenceUrl;
        }

        if (!string.IsNullOrWhiteSpace(finding.DeclaringTypeNamespaceUri))
        {
            return CompanionSpecCatalog.Resolve(finding.DeclaringTypeNamespaceUri).ReferenceUrl;
        }

        return RuleReferenceCatalog.Resolve(finding.RuleId).ReferenceUrl;
    }
}
