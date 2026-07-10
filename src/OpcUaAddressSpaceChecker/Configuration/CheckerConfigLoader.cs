using System.Text.Json;

namespace OpcUaAddressSpaceChecker.Configuration;

/// <summary>
/// Loads <see cref="CheckerConfig"/> from an <c>appsettings.json</c>-style JSON file. The config may
/// live at the root of the document or under an <c>OpcUaAddressSpaceChecker</c> section (the usual
/// appsettings convention). When no file is found, the built-in defaults
/// (<see cref="CheckerConfig.CreateDefault"/>) are returned.
/// </summary>
public static class CheckerConfigLoader
{
    /// <summary>The appsettings section name recognized in addition to the document root.</summary>
    public const string SectionName = "OpcUaAddressSpaceChecker";

    /// <summary>The conventional config file name searched for during discovery.</summary>
    public const string DefaultFileName = "appsettings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Resolves the config file to use, honoring an explicit path first, then the current working
    /// directory, then the directory next to the executable. Returns <c>null</c> when none exists.
    /// An explicitly requested path that does not exist throws <see cref="FileNotFoundException"/>.
    /// </summary>
    public static string? ResolveConfigPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"Config file not found: {explicitPath}", explicitPath);
            }

            return Path.GetFullPath(explicitPath);
        }

        foreach (var candidate in EnumerateDefaultCandidates())
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// Loads and normalizes the configuration. When <paramref name="path"/> is <c>null</c> the
    /// built-in defaults are returned.
    /// </summary>
    public static CheckerConfig Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CheckerConfig.CreateDefault();
        }

        var json = File.ReadAllText(path);
        return Parse(json);
    }

    /// <summary>
    /// Parses configuration from a JSON string (root document or <c>OpcUaAddressSpaceChecker</c>
    /// section). Exposed for testing.
    /// </summary>
    public static CheckerConfig Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CheckerConfig.CreateDefault();
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(SectionName, out var section) &&
            section.ValueKind == JsonValueKind.Object)
        {
            root = section;
        }

        var config = root.Deserialize<CheckerConfig>(SerializerOptions) ?? new CheckerConfig();
        return config.Normalize();
    }

    private static IEnumerable<string> EnumerateDefaultCandidates()
    {
        yield return Path.Combine(Directory.GetCurrentDirectory(), DefaultFileName);

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            yield return Path.Combine(baseDir, DefaultFileName);
        }
    }
}
