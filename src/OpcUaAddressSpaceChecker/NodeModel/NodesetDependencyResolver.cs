namespace OpcUaAddressSpaceChecker.NodeModel;

/// <summary>
/// Resolves NodeSet2 XML dependencies from Models/RequiredModel headers and returns dependency-first load order.
/// </summary>
public sealed class NodesetDependencyResolver
{
    private readonly BuiltinNodesetLocator _locator;

    public NodesetDependencyResolver(BuiltinNodesetLocator? locator = null)
    {
        _locator = locator ?? new BuiltinNodesetLocator();
    }

    public IReadOnlyList<string> ResolveLoadOrder(
        IEnumerable<string> nodesetPaths,
        IEnumerable<string>? searchDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(nodesetPaths);

        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var fileByModelUri = new Dictionary<string, string>(StringComparer.Ordinal);
        var seedModelUris = new List<string>();

        foreach (var path in nodesetPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            AddNodeset(Path.GetFullPath(path), graph, fileByModelUri, seedModelUris);
        }

        var queue = new Queue<string>(seedModelUris);
        while (queue.Count > 0)
        {
            var modelUri = queue.Dequeue();
            if (!graph.TryGetValue(modelUri, out var requiredModelUris))
            {
                continue;
            }

            foreach (var requiredModelUri in requiredModelUris)
            {
                if (fileByModelUri.ContainsKey(requiredModelUri))
                {
                    continue;
                }

                var dependencyPath = _locator.TryLocate(requiredModelUri, searchDirectories);
                if (dependencyPath == null)
                {
                    throw new NodesetDependencyNotFoundException(modelUri, requiredModelUri);
                }

                var addedModelUris = AddNodeset(Path.GetFullPath(dependencyPath), graph, fileByModelUri);
                foreach (var addedModelUri in addedModelUris)
                {
                    queue.Enqueue(addedModelUri);
                }
            }
        }

        return SortDependencyFirst(seedModelUris, graph, fileByModelUri);
    }

    private static IReadOnlyList<string> AddNodeset(
        string path,
        Dictionary<string, HashSet<string>> graph,
        Dictionary<string, string> fileByModelUri,
        List<string>? seedModelUris = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("NodeSet2 XML file was not found.", path);
        }

        var models = NodesetHeaderReader.ReadModels(path);
        if (models.Count == 0)
        {
            throw new InvalidDataException($"NodeSet2 XML file '{path}' does not contain a Models header.");
        }

        var modelUris = new List<string>(models.Count);
        foreach (var model in models)
        {
            fileByModelUri[model.ModelUri] = path;
            graph[model.ModelUri] = new HashSet<string>(model.RequiredModelUris, StringComparer.Ordinal);
            seedModelUris?.Add(model.ModelUri);
            modelUris.Add(model.ModelUri);
        }

        return modelUris;
    }

    private static IReadOnlyList<string> SortDependencyFirst(
        IReadOnlyCollection<string> seedModelUris,
        IReadOnlyDictionary<string, HashSet<string>> graph,
        IReadOnlyDictionary<string, string> fileByModelUri)
    {
        var orderedModelUris = new List<string>();
        var visitState = new Dictionary<string, VisitState>(StringComparer.Ordinal);

        foreach (var modelUri in seedModelUris)
        {
            Visit(modelUri, graph, visitState, orderedModelUris, []);
        }

        var orderedFiles = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var modelUri in orderedModelUris)
        {
            if (fileByModelUri.TryGetValue(modelUri, out var path) && seenFiles.Add(path))
            {
                orderedFiles.Add(path);
            }
        }

        return orderedFiles;
    }

    private static void Visit(
        string modelUri,
        IReadOnlyDictionary<string, HashSet<string>> graph,
        Dictionary<string, VisitState> visitState,
        List<string> orderedModelUris,
        Stack<string> activePath)
    {
        if (visitState.TryGetValue(modelUri, out var state))
        {
            if (state == VisitState.Visiting)
            {
                throw new InvalidDataException(
                    $"NodeSet dependency cycle detected: {string.Join(" -> ", activePath.Reverse())} -> {modelUri}");
            }

            return;
        }

        visitState[modelUri] = VisitState.Visiting;
        activePath.Push(modelUri);

        if (graph.TryGetValue(modelUri, out var requiredModelUris))
        {
            foreach (var requiredModelUri in requiredModelUris.Order(StringComparer.Ordinal))
            {
                Visit(requiredModelUri, graph, visitState, orderedModelUris, activePath);
            }
        }

        activePath.Pop();
        visitState[modelUri] = VisitState.Visited;
        orderedModelUris.Add(modelUri);
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }
}

public sealed class NodesetDependencyNotFoundException : InvalidOperationException
{
    public NodesetDependencyNotFoundException(string dependentModelUri, string requiredModelUri)
        : base($"NodeSet dependency '{requiredModelUri}' required by '{dependentModelUri}' could not be located.")
    {
        DependentModelUri = dependentModelUri;
        RequiredModelUri = requiredModelUri;
    }

    public string DependentModelUri { get; }
    public string RequiredModelUri { get; }
}
