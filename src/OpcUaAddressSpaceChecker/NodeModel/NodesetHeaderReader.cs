using System.Xml;
using System.Xml.Linq;

namespace OpcUaAddressSpaceChecker.NodeModel;

internal static class NodesetHeaderReader
{
    public static IReadOnlyList<NodesetModelHeader> ReadModels(string nodesetPath)
    {
        if (string.IsNullOrWhiteSpace(nodesetPath))
        {
            throw new ArgumentException("NodeSet path must not be empty.", nameof(nodesetPath));
        }

        using var stream = File.OpenRead(nodesetPath);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        });

        while (reader.Read())
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "Models" })
            {
                using var subtree = reader.ReadSubtree();
                var modelsElement = XElement.Load(subtree);
                return modelsElement
                    .Elements()
                    .Where(element => element.Name.LocalName == "Model")
                    .Select(ReadModel)
                    .ToArray();
            }
        }

        return [];
    }

    private static NodesetModelHeader ReadModel(XElement modelElement)
    {
        var modelUri = (string?)modelElement.Attribute("ModelUri");
        if (string.IsNullOrWhiteSpace(modelUri))
        {
            throw new InvalidDataException("NodeSet Models header contains a Model without a ModelUri attribute.");
        }

        var requiredModels = modelElement
            .Elements()
            .Where(element => element.Name.LocalName == "RequiredModel")
            .Select(element => (string?)element.Attribute("ModelUri"))
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new NodesetModelHeader(
            modelUri,
            requiredModels,
            (string?)modelElement.Attribute("Version"),
            ReadPublicationDate(modelElement));
    }

    private static DateTimeOffset? ReadPublicationDate(XElement modelElement)
    {
        var value = (string?)modelElement.Attribute("PublicationDate");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var publicationDate) ? publicationDate : null;
    }
}
