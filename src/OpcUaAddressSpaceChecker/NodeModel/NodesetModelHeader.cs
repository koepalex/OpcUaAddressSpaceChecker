namespace OpcUaAddressSpaceChecker.NodeModel;

/// <summary>
/// Model metadata read from the lightweight Models header in a NodeSet2 XML file.
/// </summary>
public sealed record NodesetModelHeader(
    string ModelUri,
    IReadOnlyList<string> RequiredModelUris,
    string? Version,
    DateTimeOffset? PublicationDate);
