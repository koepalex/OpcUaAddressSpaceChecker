namespace OpcUaAddressSpaceChecker.Reporting;

/// <summary>
/// Report-only presentation metadata for a validation rule: remediation guidance
/// ("how to solve") and an OPC Foundation online reference URL.
/// </summary>
public readonly record struct RuleReference(string Remediation, string ReferenceUrl);

/// <summary>
/// Maps rule IDs to remediation guidance and OPC Foundation reference links for report formats.
/// Remediation text mirrors the "How to fix" column of the README rules catalog so the report and
/// the documentation stay consistent; reference URLs are derived from <see cref="CompanionSpecCatalog"/>.
/// This is report-only presentation metadata and is intentionally kept out of the rule classes.
/// </summary>
public static class RuleReferenceCatalog
{
    private const string DiNamespace = "http://opcfoundation.org/UA/DI/";
    private const string MachineryNamespace = "http://opcfoundation.org/UA/Machinery/";
    private const string PumpsNamespace = "http://opcfoundation.org/UA/Pumps/";

    // Core (OPC UA base) rules cite specific sections of OPC 10000-3 (Address Space Model).
    // See https://reference.opcfoundation.org/specs/OPC-10000-3/ (the older v105/Core/docs/Part5/8.3.x
    // URLs from NodeSet2.xml have been renumbered to §8.4.x and now 404).
    private const string CoreSpecBase = "https://reference.opcfoundation.org/specs/OPC-10000-3/";
    private const string ServicesSpecBase = "https://reference.opcfoundation.org/specs/OPC-10000-4/";

    private static string CoreSpec(string section) => CoreSpecBase + section;
    private static string ServicesSpec(string section) => ServicesSpecBase + section;

    private static readonly IReadOnlyDictionary<string, RuleReference> Catalog = BuildCatalog();

    /// <summary>
    /// Resolves remediation guidance and a reference URL for a rule ID. Unknown rule IDs fall back to
    /// generic remediation text and the reference-site root URL, and never throw.
    /// </summary>
    public static RuleReference Resolve(string ruleId)
    {
        if (!string.IsNullOrWhiteSpace(ruleId) && Catalog.TryGetValue(ruleId, out var reference))
        {
            return reference;
        }

        return new RuleReference(
            "Review the finding details and align the node with the referenced OPC UA specification.",
            CompanionSpecCatalog.ReferenceRootUrl);
    }

    private static IReadOnlyDictionary<string, RuleReference> BuildCatalog()
    {
        var di = CompanionSpecCatalog.Resolve(DiNamespace).ReferenceUrl;
        var machinery = CompanionSpecCatalog.Resolve(MachineryNamespace).ReferenceUrl;
        var pumps = CompanionSpecCatalog.Resolve(PumpsNamespace).ReferenceUrl;

        return new Dictionary<string, RuleReference>(StringComparer.Ordinal)
        {
            ["ACCESS-01"] = new("Use credentials that expose the validation scope, or explicitly choose the appropriate --view-completeness policy.", ServicesSpec("5.9.2")),
            ["GEN-01"] = new("Add the missing child at the declared BrowsePath with the expected BrowseName, NodeClass, and type.", CoreSpec("6.4.4.4.1")),
            ["GEN-02"] = new("Change the child NodeClass or use the correct declaration path for the intended node.", CoreSpec("6.2.4")),
            ["GEN-03"] = new("Set the child's HasTypeDefinition to the declared type or a valid subtype.", CoreSpec("6.4.1")),
            ["GEN-04"] = new("Use a compatible DataType, ValueRank, and ArrayDimensions for the Variable.", CoreSpec("6.2.8")),
            ["GEN-05"] = new("No action is needed for an intentional extension. Define a subtype for reusable discovery contracts; investigate namespace collisions or misplaced standard children.", CoreSpec("6.4.3")),
            ["GEN-06"] = new("Add at least one child with a compatible ReferenceType and TypeDefinition below the placeholder parent.", CoreSpec("6.4.4.4.4")),
            ["GEN-07"] = new("Use a compatible ReferenceType and TypeDefinition for each placeholder child.", CoreSpec("6.4.4.4.4")),
            ["GEN-08"] = new("Link Properties with HasProperty, DataVariables with HasComponent, and match declared reference types.", CoreSpec("7")),
            ["GEN-09"] = new("Move the node under the full declared BrowsePath and add any required intermediate objects.", CoreSpec("6.4.2")),
            ["GEN-10"] = new("Add the missing HasTypeDefinition reference for non-core Object and Variable nodes.", CoreSpec("6.4.1")),
            ["GEN-11"] = new("Use a concrete subtype as the instance TypeDefinition.", CoreSpec("6.3")),
            ["GEN-12"] = new("Add the missing InputArguments or OutputArguments Property declared for the present Method.", CoreSpec("5.7")),
            ["GEN-13"] = new("Keep the inherited modelling rule or make the subtype stricter, not looser.", CoreSpec("6.4.4.2")),
            ["GEN-14"] = new("Use the namespace index that corresponds to the declaration namespace URI.", CoreSpec("5.2.4")),
            ["GEN-15"] = new("Fix or remove the stale Reference so its target NodeId can be browsed and read consistently.", ServicesSpec("5.9.2")),
            ["DI-01"] = new("Add Manufacturer, Model, HardwareRevision, SoftwareRevision, DeviceRevision, DeviceManual, SerialNumber, and RevisionCounter as DI properties.", di),
            ["DI-02"] = new("Organize the root DI component under DeviceSet or make it reachable from DeviceSet.", di),
            ["DI-03"] = new("Add at least one parameter Variable under ParameterSet and Method children under MethodSet when present.", di),
            ["DI-04"] = new("Add Locked, LockingClient, LockingUser, RemainingLockTime, InitLock, RenewLock, ExitLock, and BreakLock.", di),
            ["DI-05"] = new("Mandatory interface-derived declarations must be present; optional nameplate/support-info members are only required if the device exposes that metadata.", di),
            ["MACHINERY-01"] = new("Organize machine instances under the Machines entry point or make them hierarchically reachable from it.", machinery),
            ["PUMPS-01"] = new("Set the Configuration object TypeDefinition to ConfigurationGroupType or a valid subtype.", pumps),
            ["PUMPS-02"] = new("Move ConfigurationGroupType descendant nodes under the pump Configuration object.", pumps),
            ["PUMPS-03"] = new("Move Design and SystemRequirements below Configuration instead of directly below the pump.", pumps)
        };
    }
}
