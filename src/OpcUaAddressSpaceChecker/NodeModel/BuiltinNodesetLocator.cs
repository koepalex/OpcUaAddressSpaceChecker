using System.Xml;

namespace OpcUaAddressSpaceChecker.NodeModel;

/// <summary>
/// Locates standard and custom NodeSet2 XML files by OPC UA ModelUri.
/// </summary>
public sealed class BuiltinNodesetLocator
{
    private static readonly IReadOnlyDictionary<string, string> WellKnownRelativePaths =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["http://opcfoundation.org/UA/"] = @"Schema\Opc.Ua.NodeSet2.xml",
            ["http://opcfoundation.org/UA/DI/"] = @"DI\Opc.Ua.Di.NodeSet2.xml",
            ["http://opcfoundation.org/UA/IA/"] = @"IA\Opc.Ua.IA.NodeSet2.xml",
            ["http://opcfoundation.org/UA/Machinery/"] = @"Machinery\Opc.Ua.Machinery.NodeSet2.xml",
            ["http://opcfoundation.org/UA/Pumps/"] = @"Pumps\Opc.Ua.Pumps.NodeSet2.xml"
        };

    private readonly Dictionary<string, IReadOnlyDictionary<string, LocatedNodeset>> _scanCache =
        new(StringComparer.OrdinalIgnoreCase);

    public string? TryLocate(string modelUri, IEnumerable<string>? searchDirectories = null)
    {
        if (string.IsNullOrWhiteSpace(modelUri))
        {
            throw new ArgumentException("ModelUri must not be empty.", nameof(modelUri));
        }

        var directories = NormalizeSearchDirectories(searchDirectories);

        if (WellKnownRelativePaths.TryGetValue(modelUri, out var relativePath))
        {
            foreach (var directory in directories)
            {
                var candidate = Path.GetFullPath(Path.Combine(directory, relativePath));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (var map in directories.Select(GetOrScanDirectory))
        {
            if (map.TryGetValue(modelUri, out var located))
            {
                return located.Path;
            }
        }

        return null;
    }

    public IReadOnlyDictionary<string, string> Discover(IEnumerable<string>? searchDirectories = null)
    {
        var result = new Dictionary<string, LocatedNodeset>(StringComparer.Ordinal);
        foreach (var map in NormalizeSearchDirectories(searchDirectories).Select(GetOrScanDirectory))
        {
            foreach (var item in map)
            {
                AddOrReplaceWithNewest(result, item.Key, item.Value);
            }
        }

        return result.ToDictionary(item => item.Key, item => item.Value.Path, StringComparer.Ordinal);
    }

    private IReadOnlyDictionary<string, LocatedNodeset> GetOrScanDirectory(string directory)
    {
        var normalized = Path.GetFullPath(directory);
        if (_scanCache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var discovered = ScanDirectory(normalized);
        _scanCache[normalized] = discovered;
        return discovered;
    }

    private static IReadOnlyDictionary<string, LocatedNodeset> ScanDirectory(string directory)
    {
        var discovered = new Dictionary<string, LocatedNodeset>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return discovered;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*NodeSet2.xml", SearchOption.AllDirectories))
        {
            IReadOnlyList<NodesetModelHeader> models;
            try
            {
                models = NodesetHeaderReader.ReadModels(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
            {
                continue;
            }

            foreach (var model in models)
            {
                AddOrReplaceWithNewest(
                    discovered,
                    model.ModelUri,
                    new LocatedNodeset(Path.GetFullPath(path), model.PublicationDate));
            }
        }

        return discovered;
    }

    private static void AddOrReplaceWithNewest(
        Dictionary<string, LocatedNodeset> target,
        string modelUri,
        LocatedNodeset candidate)
    {
        if (!target.TryGetValue(modelUri, out var existing) ||
            IsNewer(candidate.PublicationDate, existing.PublicationDate))
        {
            target[modelUri] = candidate;
        }
    }

    private static bool IsNewer(DateTimeOffset? candidate, DateTimeOffset? existing)
    {
        if (candidate.HasValue && existing.HasValue)
        {
            return candidate.Value > existing.Value;
        }

        return candidate.HasValue && !existing.HasValue;
    }

    private static IReadOnlyList<string> NormalizeSearchDirectories(IEnumerable<string>? searchDirectories)
    {
        return (searchDirectories ?? [])
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record LocatedNodeset(string Path, DateTimeOffset? PublicationDate);
}
