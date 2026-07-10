using Opc.Ua;

namespace OpcUaAddressSpaceChecker.NodeModel;

/// <summary>
/// Query index over loaded NodeSet2 type definitions and their inherited instance declarations.
/// </summary>
public sealed class NodesetModelIndex
{
    private static readonly NodeId HasSubtype = new(45);
    private static readonly NodeId HasTypeDefinition = new(40);
    private static readonly NodeId HasInterface = new(17603);

    private static readonly NodeId HasComponent = new(47);
    private static readonly NodeId HasProperty = new(46);
    private static readonly NodeId Organizes = new(35);

    private static readonly NodeId ModellingRuleMandatory = new(78);
    private static readonly NodeId ModellingRuleOptional = new(80);
    private static readonly NodeId ModellingRuleExposesItsArray = new(83);
    private static readonly NodeId ModellingRuleOptionalPlaceholder = new(11508);
    private static readonly NodeId ModellingRuleMandatoryPlaceholder = new(11510);

    private static readonly NodeId[] HierarchicalReferenceRoots = [HasComponent, HasProperty, Organizes];

    private readonly NodeStateCollection _nodes;
    private readonly NamespaceTable _namespaceUris;
    private readonly ISystemContext _context;
    private readonly Dictionary<NodeId, NodeState> _nodesById;
    private readonly Dictionary<NodeId, NodeState> _typesById;

    public NodesetModelIndex(LoadedNodesets loadedNodesets)
        : this(loadedNodesets.Nodes, loadedNodesets.NamespaceUris, loadedNodesets.Context)
    {
    }

    public NodesetModelIndex(
        NodeStateCollection nodes,
        NamespaceTable namespaceUris,
        ISystemContext context)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        _namespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _nodesById = _nodes
            .Where(node => !NodeId.IsNull(node.NodeId))
            .GroupBy(node => node.NodeId)
            .ToDictionary(group => group.Key, group => group.First());
        _typesById = _nodesById.Values
            .Where(IsTypeNode)
            .ToDictionary(node => node.NodeId, node => node);
        NamespaceMap = BuildNamespaceMap(namespaceUris);
    }

    public IReadOnlyDictionary<ushort, string> NamespaceMap { get; }

    public IReadOnlyDictionary<NodeId, NodeState> TypesById => _typesById;

    public bool TryGetNode(NodeId nodeId, out NodeState? node) =>
        _nodesById.TryGetValue(nodeId, out node);

    /// <summary>
    /// Resolves the NodeSet2 <c>Documentation</c> element (an OPC Foundation online reference URL)
    /// captured on import for a node, when present and non-blank.
    /// </summary>
    public bool TryGetDocumentation(NodeId nodeId, out string? documentation)
    {
        documentation = null;
        if (_nodesById.TryGetValue(nodeId, out var node) && !string.IsNullOrWhiteSpace(node.NodeSetDocumentation))
        {
            documentation = node.NodeSetDocumentation;
            return true;
        }

        return false;
    }

    public bool TryGetType(NodeId typeId, out NodeState? typeNode) =>
        _typesById.TryGetValue(typeId, out typeNode);

    public bool TryGetTypeDefinition(NodeId instanceId, out NodeState? typeDefinition)
    {
        typeDefinition = null;
        if (!_nodesById.TryGetValue(instanceId, out var node))
        {
            return false;
        }

        return TryGetTypeDefinition(node, out typeDefinition);
    }

    public bool TryGetTypeDefinition(NodeState instanceNode, out NodeState? typeDefinition)
    {
        ArgumentNullException.ThrowIfNull(instanceNode);

        typeDefinition = null;
        var typeDefinitionId = GetTypeDefinitionId(instanceNode);
        if (NodeId.IsNull(typeDefinitionId))
        {
            return false;
        }

        return _typesById.TryGetValue(typeDefinitionId, out typeDefinition);
    }

    public IReadOnlyList<NodeState> GetSupertypeChain(NodeId typeId)
    {
        if (!_typesById.TryGetValue(typeId, out var typeNode))
        {
            return [];
        }

        var leafToRoot = new List<NodeState>();
        var seen = new HashSet<NodeId>();
        var current = typeNode;

        while (current != null && seen.Add(current.NodeId))
        {
            leafToRoot.Add(current);
            var superTypeId = GetSuperTypeId(current);
            if (NodeId.IsNull(superTypeId) || !_typesById.TryGetValue(superTypeId, out current))
            {
                break;
            }
        }

        leafToRoot.Reverse();
        return leafToRoot;
    }

    public IReadOnlyList<InstanceDeclaration> GetInstanceDeclarations(NodeId typeId)
    {
        var declarations = BuildMergedDeclarations(typeId, forceMandatory: false, []);
        return declarations.Values
            .OrderBy(declaration => FormatBrowsePath(declaration.BrowsePath), StringComparer.Ordinal)
            .ToArray();
    }

    private Dictionary<string, InstanceDeclaration> BuildMergedDeclarations(
        NodeId typeId,
        bool forceMandatory,
        HashSet<NodeId> activeTypes)
    {
        var merged = new Dictionary<string, InstanceDeclaration>(StringComparer.Ordinal);
        if (!_typesById.ContainsKey(typeId) || !activeTypes.Add(typeId))
        {
            return merged;
        }

        try
        {
            foreach (var typeNode in GetSupertypeChain(typeId))
            {
                foreach (var declaration in CollectOwnDeclarations(typeNode, forceMandatory))
                {
                    AddOrOverride(merged, declaration);
                }

                foreach (var interfaceId in GetInterfaceIds(typeNode))
                {
                    var interfaceDeclarations = BuildMergedDeclarations(interfaceId, forceMandatory: true, activeTypes);
                    foreach (var declaration in interfaceDeclarations.Values)
                    {
                        AddOrOverride(merged, declaration);
                    }
                }
            }
        }
        finally
        {
            activeTypes.Remove(typeId);
        }

        return merged;
    }

    private IReadOnlyList<InstanceDeclaration> CollectOwnDeclarations(NodeState typeNode, bool forceMandatory)
    {
        var result = new List<InstanceDeclaration>();
        CollectChildDeclarations(typeNode, [], forceMandatory, result, []);
        return result;
    }

    private void CollectChildDeclarations(
        NodeState parent,
        IReadOnlyList<QualifiedName> parentBrowsePath,
        bool forceMandatory,
        List<InstanceDeclaration> result,
        HashSet<NodeId> activeNodes)
    {
        if (!activeNodes.Add(parent.NodeId))
        {
            return;
        }

        try
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(_context, children);
            var referencesByTarget = GetForwardReferencesByTarget(parent);

            foreach (var child in children)
            {
                var modellingRuleId = forceMandatory ? ModellingRuleMandatory : child.ModellingRuleId;
                var referenceTypeId = referencesByTarget.TryGetValue(child.NodeId, out var resolvedReferenceTypeId)
                    ? resolvedReferenceTypeId
                    : child.ReferenceTypeId;

                if (NodeId.IsNull(modellingRuleId) || !IsHierarchicalReferenceType(referenceTypeId))
                {
                    continue;
                }

                var browsePath = parentBrowsePath.Concat([child.BrowseName]).ToArray();
                result.Add(new InstanceDeclaration(
                    child.NodeId,
                    browsePath,
                    child.BrowseName,
                    child.NodeClass,
                    NodeId.IsNull(child.TypeDefinitionId) ? null : child.TypeDefinitionId,
                    modellingRuleId,
                    referenceTypeId));

                CollectChildDeclarations(child, browsePath, forceMandatory, result, activeNodes);
            }
        }
        finally
        {
            activeNodes.Remove(parent.NodeId);
        }
    }

    private IEnumerable<NodeId> GetInterfaceIds(NodeState typeNode)
    {
        var references = new List<IReference>();
        typeNode.GetReferences(_context, references, HasInterface, false);

        foreach (var reference in references)
        {
            var targetId = ExpandedNodeId.ToNodeId(reference.TargetId, _namespaceUris);
            if (!NodeId.IsNull(targetId))
            {
                yield return targetId;
            }
        }
    }

    private Dictionary<NodeId, NodeId> GetForwardReferencesByTarget(NodeState parent)
    {
        var references = new List<IReference>();
        parent.GetReferences(_context, references);

        var result = new Dictionary<NodeId, NodeId>();
        foreach (var reference in references.Where(reference => !reference.IsInverse))
        {
            var targetId = ExpandedNodeId.ToNodeId(reference.TargetId, _namespaceUris);
            if (!NodeId.IsNull(targetId) && !NodeId.IsNull(reference.ReferenceTypeId))
            {
                result[targetId] = reference.ReferenceTypeId;
            }
        }

        return result;
    }

    private bool IsHierarchicalReferenceType(NodeId? referenceTypeId)
    {
        if (NodeId.IsNull(referenceTypeId))
        {
            return false;
        }

        return HierarchicalReferenceRoots.Any(root => IsSameOrSubtype(referenceTypeId, root));
    }

    public bool IsSameOrSubtype(NodeId typeId, NodeId expectedSupertypeId)
    {
        if (typeId == expectedSupertypeId)
        {
            return true;
        }

        var current = typeId;
        var seen = new HashSet<NodeId>();
        while (!NodeId.IsNull(current) && seen.Add(current))
        {
            if (!_typesById.TryGetValue(current, out var currentNode))
            {
                return false;
            }

            var superTypeId = GetSuperTypeId(currentNode);
            if (NodeId.IsNull(superTypeId))
            {
                return false;
            }

            if (superTypeId == expectedSupertypeId)
            {
                return true;
            }

            current = superTypeId;
        }

        return false;
    }

    private static void AddOrOverride(
        Dictionary<string, InstanceDeclaration> declarations,
        InstanceDeclaration candidate)
    {
        var key = FormatBrowsePath(candidate.BrowsePath);
        if (!declarations.TryGetValue(key, out var existing))
        {
            declarations[key] = candidate;
            return;
        }

        if (IsRelaxingOverride(existing.ModellingRuleId, candidate.ModellingRuleId))
        {
            return;
        }

        if (!CanOverride(existing.ModellingRuleId, candidate.ModellingRuleId))
        {
            return;
        }

        declarations[key] = candidate;
    }

    private static bool IsRelaxingOverride(NodeId existingModellingRuleId, NodeId candidateModellingRuleId) =>
        existingModellingRuleId == ModellingRuleMandatory && candidateModellingRuleId == ModellingRuleOptional ||
        existingModellingRuleId == ModellingRuleMandatoryPlaceholder &&
        candidateModellingRuleId == ModellingRuleOptionalPlaceholder;

    private static bool CanOverride(NodeId existingModellingRuleId, NodeId candidateModellingRuleId)
    {
        if (existingModellingRuleId == candidateModellingRuleId)
        {
            return true;
        }

        if (existingModellingRuleId == ModellingRuleOptional && candidateModellingRuleId == ModellingRuleMandatory)
        {
            return true;
        }

        if (existingModellingRuleId == ModellingRuleOptionalPlaceholder &&
            candidateModellingRuleId == ModellingRuleMandatoryPlaceholder)
        {
            return true;
        }

        return IsKnownModellingRule(existingModellingRuleId) && IsKnownModellingRule(candidateModellingRuleId);
    }

    private static bool IsKnownModellingRule(NodeId modellingRuleId) =>
        modellingRuleId == ModellingRuleMandatory ||
        modellingRuleId == ModellingRuleOptional ||
        modellingRuleId == ModellingRuleExposesItsArray ||
        modellingRuleId == ModellingRuleOptionalPlaceholder ||
        modellingRuleId == ModellingRuleMandatoryPlaceholder;

    private static NodeId? GetTypeDefinitionId(NodeState instanceNode)
    {
        if (instanceNode is BaseInstanceState instanceState && !NodeId.IsNull(instanceState.TypeDefinitionId))
        {
            return instanceState.TypeDefinitionId;
        }

        var references = new List<IReference>();
        instanceNode.GetReferences(null, references, HasTypeDefinition, false);
        return ExpandedNodeId.ToNodeId(references.FirstOrDefault()?.TargetId, null);
    }

    private static NodeId? GetSuperTypeId(NodeState typeNode)
    {
        if (typeNode is BaseTypeState baseTypeState && !NodeId.IsNull(baseTypeState.SuperTypeId))
        {
            return baseTypeState.SuperTypeId;
        }

        var references = new List<IReference>();
        typeNode.GetReferences(null, references, HasSubtype, true);
        return ExpandedNodeId.ToNodeId(references.FirstOrDefault()?.TargetId, null);
    }

    private static bool IsTypeNode(NodeState node) =>
        node.NodeClass is NodeClass.ObjectType or NodeClass.VariableType or NodeClass.DataType or NodeClass.ReferenceType;

    private static IReadOnlyDictionary<ushort, string> BuildNamespaceMap(NamespaceTable namespaceUris)
    {
        var result = new Dictionary<ushort, string>();
        for (ushort i = 0; i < namespaceUris.Count; i++)
        {
            result[i] = namespaceUris.GetString(i);
        }

        return result;
    }

    private static string FormatBrowsePath(IEnumerable<QualifiedName> browsePath) =>
        string.Join("/", browsePath.Select(name => $"{name.NamespaceIndex}:{name.Name}"));
}
