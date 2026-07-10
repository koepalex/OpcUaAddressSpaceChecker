namespace OpcUaAddressSpaceChecker.Tests;

/// <summary>
/// Resolves the root directory of the OPC UA Companion specification NodeSet2 files used by the
/// NodeSet2-backed tests. CI (and any environment without the local clone) sets the
/// <c>UA_NODESET_DIR</c> environment variable to a checkout of <c>OPCFoundation/UA-Nodeset</c>;
/// local development falls back to the conventional clone at <c>C:\ode\UA-Nodeset</c>.
/// </summary>
internal static class NodesetTestData
{
    /// <summary>Environment variable pointing at a UA-Nodeset checkout (used by CI).</summary>
    public const string DirectoryEnvironmentVariable = "UA_NODESET_DIR";

    private const string DefaultDirectory = @"C:\ode\UA-Nodeset";

    /// <summary>
    /// The nodeset root directory: <c>UA_NODESET_DIR</c> when set and non-empty, otherwise the
    /// local default clone path.
    /// </summary>
    public static string Root =>
        Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable) is { Length: > 0 } dir
            ? dir
            : DefaultDirectory;
}
