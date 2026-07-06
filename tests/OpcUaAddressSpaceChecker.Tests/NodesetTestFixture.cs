using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests;

public sealed class NodesetTestFixture
{
    public const string UaModelUri = "http://opcfoundation.org/UA/";
    public const string DiModelUri = "http://opcfoundation.org/UA/DI/";
    public const string IaModelUri = "http://opcfoundation.org/UA/IA/";
    public const string MachineryModelUri = "http://opcfoundation.org/UA/Machinery/";
    public const string PumpsModelUri = "http://opcfoundation.org/UA/Pumps/";

    private const string NodesetDirectory = @"C:\ode\UA-Nodeset";

    public NodesetTestFixture()
    {
        var seeds = new[]
        {
            Path.Combine(NodesetDirectory, @"Schema\Opc.Ua.NodeSet2.xml"),
            Path.Combine(NodesetDirectory, @"DI\Opc.Ua.Di.NodeSet2.xml"),
            Path.Combine(NodesetDirectory, @"IA\Opc.Ua.IA.NodeSet2.xml"),
            Path.Combine(NodesetDirectory, @"Machinery\Opc.Ua.Machinery.NodeSet2.xml"),
            Path.Combine(NodesetDirectory, @"Pumps\Opc.Ua.Pumps.NodeSet2.xml")
        };

        var loadOrder = new NodesetDependencyResolver()
            .ResolveLoadOrder(seeds, [NodesetDirectory]);

        Loaded = new NodesetLoader().Load(loadOrder);
        Model = new NodesetModelIndex(Loaded);
        Session = TestSessionProxy.Create(Model);
        Context = new ValidationContext(Model, Session, string.Empty, NullLogger.Instance);
    }

    public LoadedNodesets Loaded { get; }
    public NodesetModelIndex Model { get; }
    public ISession Session { get; }
    public ValidationContext Context { get; }

    public ushort NamespaceIndex(string modelUri)
    {
        foreach (var item in Model.NamespaceMap)
        {
            if (string.Equals(item.Value, modelUri, StringComparison.Ordinal))
            {
                return item.Key;
            }
        }

        throw new InvalidOperationException($"Model URI '{modelUri}' was not loaded.");
    }

    public QualifiedName BrowseName(string modelUri, string name) =>
        new(name, NamespaceIndex(modelUri));

    public NodeId NodeId(string modelUri, uint numericIdentifier) =>
        new(numericIdentifier, NamespaceIndex(modelUri));

    public NodeState FindType(string modelUri, string browseName) =>
        Model.TypesById.Values.Single(type =>
            string.Equals(type.BrowseName.Name, browseName, StringComparison.Ordinal) &&
            Model.NamespaceMap.TryGetValue(type.NodeId.NamespaceIndex, out var uri) &&
            string.Equals(uri, modelUri, StringComparison.Ordinal));

    public InstanceDeclaration Declaration(NodeId typeId, params string[] path) =>
        Model.GetInstanceDeclarations(typeId).Single(declaration =>
            declaration.BrowsePath.Select(segment => segment.Name).SequenceEqual(path, StringComparer.Ordinal));

    private class TestSessionProxy : DispatchProxy
    {
        private NamespaceTable _namespaceUris = null!;

        public static ISession Create(NodesetModelIndex model)
        {
            var proxy = DispatchProxy.Create<ISession, TestSessionProxy>();
            var testProxy = (TestSessionProxy)(object)proxy;
            testProxy._namespaceUris = CreateNamespaceTable(model);
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_NamespaceUris")
            {
                return _namespaceUris;
            }

            if (targetMethod?.Name == "get_OperationLimits")
            {
                return new OperationLimits();
            }

            if (targetMethod?.ReturnType == typeof(void))
            {
                return null;
            }

            if (targetMethod?.ReturnType.IsValueType == true)
            {
                return Activator.CreateInstance(targetMethod.ReturnType);
            }

            throw new NotSupportedException($"The unit-test session does not implement {targetMethod?.Name}.");
        }

        private static NamespaceTable CreateNamespaceTable(NodesetModelIndex model)
        {
            var table = new NamespaceTable();
            foreach (var item in model.NamespaceMap.OrderBy(item => item.Key))
            {
                table.GetIndexOrAppend(item.Value);
            }

            return table;
        }
    }
}

internal static class LiveNodeFactory
{
    private static uint _nextId = 50_000;

    public static LiveNode Object(
        QualifiedName browseName,
        NodeId? typeDefinitionId = null,
        ushort? nodeNamespaceIndex = null) =>
        Node(browseName, NodeClass.Object, typeDefinitionId, nodeNamespaceIndex: nodeNamespaceIndex);

    public static LiveNode Variable(
        QualifiedName browseName,
        NodeId? typeDefinitionId = null,
        NodeId? dataType = null,
        int? valueRank = -1,
        ushort? nodeNamespaceIndex = null) =>
        Node(
            browseName,
            NodeClass.Variable,
            typeDefinitionId,
            dataType,
            valueRank,
            nodeNamespaceIndex);

    public static void AddChild(
        LiveNode parent,
        LiveNode child,
        NodeId? referenceTypeId = null)
    {
        parent.Children.Add(child);
        parent.ForwardHierarchicalReferences.Add(new LiveReference(
            referenceTypeId ?? ReferenceTypeIds.HasComponent,
            child.NodeId,
            child.BrowseName,
            child.DisplayName,
            child.NodeClass));
    }

    public static LiveNode AddDeclarationPath(
        LiveNode root,
        InstanceDeclaration declaration,
        IReadOnlyList<InstanceDeclaration> allDeclarations,
        NodesetModelIndex model)
    {
        var current = root;
        for (var i = 0; i < declaration.BrowsePath.Count; i++)
        {
            var segment = declaration.BrowsePath[i];
            var existing = current.Children.FirstOrDefault(child =>
                child.BrowseName.NamespaceIndex == segment.NamespaceIndex &&
                string.Equals(child.BrowseName.Name, segment.Name, StringComparison.Ordinal));
            if (existing != null)
            {
                current = existing;
                continue;
            }

            var prefix = declaration.BrowsePath.Take(i + 1).ToArray();
            var prefixDeclaration = allDeclarations.FirstOrDefault(candidate =>
                candidate.BrowsePath.Count == prefix.Length &&
                candidate.BrowsePath.Zip(prefix).All(pair =>
                    pair.First.NamespaceIndex == pair.Second.NamespaceIndex &&
                    string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)));

            var nodeClass = prefixDeclaration?.NodeClass ??
                            (i == declaration.BrowsePath.Count - 1 ? declaration.NodeClass : NodeClass.Object);
            var typeDefinitionId = prefixDeclaration?.TypeDefinitionId;
            var referenceTypeId = prefixDeclaration?.ReferenceTypeId ?? ReferenceTypeIds.HasComponent;
            var node = NodeFromDeclaration(segment, nodeClass, typeDefinitionId, prefixDeclaration, model);

            AddChild(current, node, referenceTypeId);
            current = node;
        }

        return current;
    }

    private static LiveNode NodeFromDeclaration(
        QualifiedName browseName,
        NodeClass nodeClass,
        NodeId? typeDefinitionId,
        InstanceDeclaration? declaration,
        NodesetModelIndex model)
    {
        if (nodeClass == NodeClass.Variable)
        {
            NodeId? dataType = null;
            int? valueRank = -1;
            if (declaration != null &&
                model.TryGetNode(declaration.NodeId, out var node) &&
                node is BaseVariableState variable)
            {
                dataType = variable.DataType;
                valueRank = variable.ValueRank;
            }

            return Variable(browseName, typeDefinitionId, dataType, valueRank);
        }

        return Object(browseName, typeDefinitionId);
    }

    private static LiveNode Node(
        QualifiedName browseName,
        NodeClass nodeClass,
        NodeId? typeDefinitionId = null,
        NodeId? dataType = null,
        int? valueRank = null,
        ushort? nodeNamespaceIndex = null) =>
        new()
        {
            NodeId = new NodeId(_nextId++, nodeNamespaceIndex ?? browseName.NamespaceIndex),
            BrowseName = browseName,
            DisplayName = browseName.Name,
            NodeClass = nodeClass,
            TypeDefinitionId = typeDefinitionId,
            DataType = dataType,
            ValueRank = valueRank
        };
}
