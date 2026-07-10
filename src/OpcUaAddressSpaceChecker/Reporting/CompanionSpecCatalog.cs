namespace OpcUaAddressSpaceChecker.Reporting;

/// <summary>
/// Report-only presentation metadata for a companion specification namespace:
/// a human-readable display name and an OPC Foundation online reference URL.
/// </summary>
public readonly record struct CompanionSpecInfo(string DisplayName, string ReferenceUrl);

/// <summary>
/// Maps OPC UA namespace URIs to companion-specification display names and OPC Foundation
/// reference-website links (see https://reference.opcfoundation.org). Used by report formats
/// that group findings per namespace; not part of the validation model.
/// </summary>
public static class CompanionSpecCatalog
{
    /// <summary>
    /// Root of the OPC Foundation online reference site. Used as the fallback reference URL
    /// for unmapped namespaces.
    /// </summary>
    public const string ReferenceRootUrl = "https://reference.opcfoundation.org";

    private static readonly IReadOnlyDictionary<string, CompanionSpecInfo> Catalog =
        new Dictionary<string, CompanionSpecInfo>(StringComparer.Ordinal)
        {
            ["http://opcfoundation.org/UA/"] = new(
                "OPC UA Core",
                $"{ReferenceRootUrl}/Core/docs/"),
            ["http://opcfoundation.org/UA/DI/"] = new(
                "Devices (DI)",
                $"{ReferenceRootUrl}/DI/docs/"),
            ["http://opcfoundation.org/UA/IA/"] = new(
                "Industrial Automation (IA)",
                $"{ReferenceRootUrl}/IA/docs/"),
            ["http://opcfoundation.org/UA/Machinery/"] = new(
                "Machinery",
                $"{ReferenceRootUrl}/Machinery/docs/"),
            ["http://opcfoundation.org/UA/Pumps/"] = new(
                "Pumps",
                $"{ReferenceRootUrl}/Pumps/docs/")
        };

    /// <summary>
    /// Resolves the display name and reference URL for a namespace URI. Unmapped URIs fall back
    /// to the raw URI as the display name and the reference-site root URL.
    /// </summary>
    public static CompanionSpecInfo Resolve(string namespaceUri)
    {
        if (!string.IsNullOrWhiteSpace(namespaceUri) &&
            Catalog.TryGetValue(namespaceUri, out var info))
        {
            return info;
        }

        var displayName = string.IsNullOrWhiteSpace(namespaceUri) ? "(unknown namespace)" : namespaceUri;
        return new CompanionSpecInfo(displayName, ReferenceRootUrl);
    }
}
