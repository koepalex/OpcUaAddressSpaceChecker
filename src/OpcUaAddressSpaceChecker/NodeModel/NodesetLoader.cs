using Opc.Ua;
using Opc.Ua.Export;

namespace OpcUaAddressSpaceChecker.NodeModel;

/// <summary>
/// Loads NodeSet2 XML files into a shared OPC UA type model.
/// </summary>
public sealed class NodesetLoader
{
    public LoadedNodesets Load(IEnumerable<string> nodesetPaths)
    {
        ArgumentNullException.ThrowIfNull(nodesetPaths);

        var namespaceUris = new NamespaceTable();

        var context = new SystemContext(new NodesetTelemetryContext())
        {
            NamespaceUris = namespaceUris
        };

        var nodes = new NodeStateCollection();
        var loadedPaths = new List<string>();

        foreach (var path in nodesetPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var fullPath = Path.GetFullPath(path);
            using var stream = File.OpenRead(fullPath);
            var nodeSet = UANodeSet.Read(stream)
                ?? throw new InvalidDataException($"NodeSet2 XML file '{fullPath}' could not be read.");

            if (namespaceUris.Count == 0 && nodeSet.NamespaceUris is { Length: > 0 })
            {
                namespaceUris.Append(Namespaces.OpcUa);
            }

            if (nodeSet.NamespaceUris != null)
            {
                foreach (var namespaceUri in nodeSet.NamespaceUris.Where(uri => !string.IsNullOrWhiteSpace(uri)))
                {
                    namespaceUris.GetIndexOrAppend(namespaceUri);
                }
            }

            nodeSet.Import(context, nodes, linkParentChild: true);
            loadedPaths.Add(fullPath);
        }

        return new LoadedNodesets(nodes, namespaceUris, context, loadedPaths);
    }
}

public sealed record LoadedNodesets(
    NodeStateCollection Nodes,
    NamespaceTable NamespaceUris,
    SystemContext Context,
    IReadOnlyList<string> LoadedPaths);
