using Opc.Ua;

namespace OpcUaAddressSpaceChecker.Reporting;

/// <summary>
/// Renders a live-instance NodeId in a human-readable, copy-pasteable form
/// <c>&lt;ns:BrowseName&gt; (&lt;ExpandedNodeId&gt;)</c> — BrowseName first for readability, the real
/// ExpandedNodeId (numeric <c>i=</c> or native <c>s=</c>) retained as the precise, resolvable
/// identifier. Falls back to the bare NodeId string when the BrowseName is unknown, and keeps the
/// numeric identifier only as a fallback. Genuine String NodeIds keep their native <c>;s=</c> form.
/// </summary>
public sealed class NodeIdDisplayFormatter
{
    private readonly IReadOnlyDictionary<ushort, string> _namespaceUrisByIndex;
    private readonly IReadOnlyDictionary<NodeId, string> _browseNamesByNodeId;

    /// <summary>
    /// Creates a formatter from index-&gt;URI and NodeId-&gt;BrowseName snapshots so it stays
    /// independent of a live session and unit-testable.
    /// </summary>
    /// <param name="namespaceUrisByIndex">Namespace index to URI snapshot (used to build the ExpandedNodeId).</param>
    /// <param name="browseNamesByNodeId">NodeId to <c>namespaceIndex:BrowseName</c> snapshot.</param>
    public NodeIdDisplayFormatter(
        IReadOnlyDictionary<ushort, string> namespaceUrisByIndex,
        IReadOnlyDictionary<NodeId, string> browseNamesByNodeId)
    {
        ArgumentNullException.ThrowIfNull(namespaceUrisByIndex);
        ArgumentNullException.ThrowIfNull(browseNamesByNodeId);
        _namespaceUrisByIndex = namespaceUrisByIndex;
        _browseNamesByNodeId = browseNamesByNodeId;
    }

    /// <summary>
    /// Returns the name-annotated form <c>ns:BrowseName (ExpandedNodeId)</c> when the BrowseName is
    /// known, otherwise the ExpandedNodeId (or the bare NodeId when the namespace URI is unknown).
    /// </summary>
    public string Format(NodeId? nodeId)
    {
        if (NodeId.IsNull(nodeId))
        {
            return "<null>";
        }

        var expanded = FormatExpanded(nodeId!);
        var browseName = TryGetBrowseName(nodeId);
        return browseName is null ? expanded : $"{browseName} ({expanded})";
    }

    /// <summary>Returns the <c>namespaceIndex:BrowseName</c> label for a NodeId, or null when unknown.</summary>
    public string? TryGetBrowseName(NodeId? nodeId) =>
        nodeId is not null &&
        _browseNamesByNodeId.TryGetValue(nodeId, out var name) &&
        !string.IsNullOrWhiteSpace(name)
            ? name
            : null;

    private string FormatExpanded(NodeId nodeId) =>
        _namespaceUrisByIndex.TryGetValue(nodeId.NamespaceIndex, out var uri) && !string.IsNullOrWhiteSpace(uri)
            ? new ExpandedNodeId(nodeId, uri).ToString()
            : nodeId.ToString();
}
