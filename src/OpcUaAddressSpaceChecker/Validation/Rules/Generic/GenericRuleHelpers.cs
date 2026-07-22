using System.Diagnostics.CodeAnalysis;
using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

internal static class GenericRuleHelpers
{
    internal static readonly NodeId ModellingRuleMandatory = new(78);
    internal static readonly NodeId ModellingRuleOptional = new(80);
    internal static readonly NodeId ModellingRuleExposesItsArray = new(83);
    internal static readonly NodeId ModellingRuleOptionalPlaceholder = new(11508);
    internal static readonly NodeId ModellingRuleMandatoryPlaceholder = new(11510);

    internal static readonly NodeId HasComponent = new(47);
    internal static readonly NodeId HasProperty = new(46);
    internal static readonly NodeId PropertyType = new(68);
    internal static readonly NodeId BaseDataVariableType = new(63);

    internal sealed record ChildLink(LiveNode Parent, LiveNode Child, LiveReference Reference);

    internal static bool IsObjectOrVariable(LiveNode node) =>
        node.NodeClass is NodeClass.Object or NodeClass.Variable;

    public static bool IsCoreNamespace(LiveNode node) =>
        node.NodeId.NamespaceIndex == 0;

    internal static bool HasUsableType(LiveNode node, ValidationContext context) =>
        IsObjectOrVariable(node) &&
        !NodeId.IsNull(node.TypeDefinitionId) &&
        context.GetInstanceDeclarations(node).Count > 0;

    internal static bool IsMandatory(InstanceDeclaration declaration) =>
        declaration.ModellingRuleId == ModellingRuleMandatory;

    internal static bool IsOptional(InstanceDeclaration declaration) =>
        declaration.ModellingRuleId == ModellingRuleOptional ||
        declaration.ModellingRuleId == ModellingRuleExposesItsArray;

    internal static bool IsPlaceholder(InstanceDeclaration declaration) =>
        IsOptionalPlaceholder(declaration) || IsMandatoryPlaceholder(declaration);

    internal static bool IsOptionalPlaceholder(InstanceDeclaration declaration) =>
        declaration.ModellingRuleId == ModellingRuleOptionalPlaceholder;

    internal static bool IsMandatoryPlaceholder(InstanceDeclaration declaration) =>
        declaration.ModellingRuleId == ModellingRuleMandatoryPlaceholder;

    internal static IReadOnlyList<InstanceDeclaration> ConcreteDeclarations(ValidationContext context, LiveNode node) =>
        context.GetInstanceDeclarations(node).Where(declaration => !IsPlaceholder(declaration)).ToArray();

    internal static IReadOnlyList<InstanceDeclaration> DirectDeclarations(ValidationContext context, LiveNode node) =>
        context.GetInstanceDeclarations(node).Where(declaration => declaration.BrowsePath.Count == 1).ToArray();

    internal static IReadOnlyList<ChildLink> GetChildLinks(LiveNode parent)
    {
        var links = new List<ChildLink>();

        foreach (var reference in parent.ForwardHierarchicalReferences)
        {
            foreach (var child in parent.Children.Where(child => child.NodeId == reference.TargetId))
            {
                links.Add(new ChildLink(parent, child, reference));
            }
        }

        return links;
    }

    /// <summary>
    /// Declaration-driven BrowsePath traversal. Each segment is resolved against the node's
    /// InstanceDeclaration set: placeholder segments (OptionalPlaceholder/MandatoryPlaceholder,
    /// stored verbatim as e.g. <c>0:&lt;OrderedObject&gt;</c>) are matched by fulfillment
    /// (ReferenceType + TypeDefinition compatibility) against every concrete child rather than by
    /// literal BrowseName; non-placeholder segments match literally. Passing an empty declaration
    /// set therefore performs purely literal matching.
    /// </summary>
    internal static IReadOnlyList<ChildLink> FindChildrenByBrowsePath(
        ValidationContext context,
        LiveNode root,
        IReadOnlyList<InstanceDeclaration> declarations,
        IReadOnlyList<QualifiedName> browsePath)
    {
        if (browsePath.Count == 0)
        {
            return [];
        }

        IReadOnlyList<LiveNode> currentParents = [root];
        IReadOnlyList<ChildLink> currentMatches = [];

        for (var depth = 0; depth < browsePath.Count; depth++)
        {
            var prefix = browsePath.Take(depth + 1).ToArray();
            var segmentDeclaration = declarations.FirstOrDefault(declaration => BrowsePathEquals(declaration.BrowsePath, prefix));
            var placeholderSegment = segmentDeclaration != null && IsPlaceholder(segmentDeclaration);

            var nextMatches = new List<ChildLink>();
            foreach (var parent in currentParents)
            {
                if (placeholderSegment)
                {
                    nextMatches.AddRange(GetChildLinks(parent).Where(link =>
                        IsReferenceTypeCompatible(context, link.Reference.ReferenceTypeId, segmentDeclaration!.ReferenceTypeId) &&
                        IsTypeCompatible(context, link.Child.TypeDefinitionId, segmentDeclaration.TypeDefinitionId)));
                }
                else
                {
                    nextMatches.AddRange(GetChildLinks(parent).Where(link => BrowseNameEquals(link.Child.BrowseName, browsePath[depth])));
                }
            }

            currentMatches = nextMatches;
            currentParents = nextMatches.Select(match => match.Child).ToArray();
            if (currentParents.Count == 0)
            {
                break;
            }
        }

        return currentMatches;
    }

    internal static bool BrowsePathExists(
        ValidationContext context,
        LiveNode root,
        IReadOnlyList<InstanceDeclaration> declarations,
        IReadOnlyList<QualifiedName> browsePath) =>
        FindChildrenByBrowsePath(context, root, declarations, browsePath).Count > 0;

    internal static IReadOnlyList<ChildLink> ResolveParentLinks(
        ValidationContext context,
        LiveNode root,
        IReadOnlyList<InstanceDeclaration> declarations,
        IReadOnlyList<QualifiedName> browsePath)
    {
        if (browsePath.Count <= 1)
        {
            return [new ChildLink(root, root, new LiveReference(new NodeId(0), root.NodeId, root.BrowseName, root.DisplayName, root.NodeClass))];
        }

        return FindChildrenByBrowsePath(context, root, declarations, browsePath.Take(browsePath.Count - 1).ToArray());
    }

    /// <summary>
    /// True when any ancestor prefix of the declaration's BrowsePath is itself a placeholder
    /// declaration, i.e. the declared child is only reachable through a placeholder segment.
    /// </summary>
    internal static bool CrossesPlaceholderAncestor(
        IReadOnlyList<InstanceDeclaration> declarations,
        InstanceDeclaration declaration)
    {
        for (var depth = 1; depth < declaration.BrowsePath.Count; depth++)
        {
            var prefix = declaration.BrowsePath.Take(depth).ToArray();
            var ancestor = declarations.FirstOrDefault(candidate => BrowsePathEquals(candidate.BrowsePath, prefix));
            if (ancestor != null && IsPlaceholder(ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// For a declaration whose BrowsePath crosses a placeholder ancestor, returns each concrete
    /// instance that fulfills the deepest placeholder prefix but is missing the remaining literal
    /// suffix. Empty when the path crosses no placeholder or every fulfilling instance is complete
    /// (or when the placeholder has no fulfilling instances at all).
    /// </summary>
    internal static IReadOnlyList<(LiveNode Instance, IReadOnlyList<QualifiedName> Suffix)> FindPlaceholderInstancesMissingChild(
        ValidationContext context,
        LiveNode node,
        IReadOnlyList<InstanceDeclaration> declarations,
        InstanceDeclaration declaration)
    {
        var placeholderDepth = -1;
        for (var depth = 1; depth < declaration.BrowsePath.Count; depth++)
        {
            var prefix = declaration.BrowsePath.Take(depth).ToArray();
            var ancestor = declarations.FirstOrDefault(candidate => BrowsePathEquals(candidate.BrowsePath, prefix));
            if (ancestor != null && IsPlaceholder(ancestor))
            {
                placeholderDepth = depth;
            }
        }

        if (placeholderDepth < 0)
        {
            return [];
        }

        var placeholderPrefix = declaration.BrowsePath.Take(placeholderDepth).ToArray();
        var suffix = declaration.BrowsePath.Skip(placeholderDepth).ToArray();

        var results = new List<(LiveNode, IReadOnlyList<QualifiedName>)>();
        foreach (var instance in FindChildrenByBrowsePath(context, node, declarations, placeholderPrefix)
                     .Select(link => link.Child)
                     .Distinct())
        {
            if (FindChildrenByBrowsePath(context, instance, [], suffix).Count == 0)
            {
                results.Add((instance, suffix));
            }
        }

        return results;
    }

    internal static bool IsSuppressedByMissingOptionalAncestor(
        ValidationContext context,
        LiveNode node,
        InstanceDeclaration declaration,
        IReadOnlyList<InstanceDeclaration> declarations)
    {
        for (var depth = 1; depth < declaration.BrowsePath.Count; depth++)
        {
            var prefix = declaration.BrowsePath.Take(depth).ToArray();
            var ancestorDeclaration = declarations.FirstOrDefault(candidate => BrowsePathEquals(candidate.BrowsePath, prefix));
            if (ancestorDeclaration == null)
            {
                continue;
            }

            if ((IsOptional(ancestorDeclaration) || IsOptionalPlaceholder(ancestorDeclaration)) &&
                !BrowsePathExists(context, node, declarations, prefix))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsSuppressedByMissingRequiredAncestor(
        ValidationContext context,
        LiveNode node,
        InstanceDeclaration declaration,
        IReadOnlyList<InstanceDeclaration> declarations)
    {
        for (var depth = 1; depth < declaration.BrowsePath.Count; depth++)
        {
            var prefix = declaration.BrowsePath.Take(depth).ToArray();
            var ancestorDeclaration = declarations.FirstOrDefault(candidate =>
                BrowsePathEquals(candidate.BrowsePath, prefix));
            if (ancestorDeclaration == null ||
                (!IsMandatory(ancestorDeclaration) && !IsMandatoryPlaceholder(ancestorDeclaration)))
            {
                continue;
            }

            if (!BrowsePathExists(context, node, declarations, prefix))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool BrowseNameEquals(QualifiedName left, QualifiedName right) =>
        left.NamespaceIndex == right.NamespaceIndex &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal);
    internal static bool BrowseNameMatchesNameOnly(QualifiedName left, QualifiedName right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal);

    internal static bool BrowsePathEquals(IReadOnlyList<QualifiedName> left, IReadOnlyList<QualifiedName> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => BrowseNameEquals(pair.First, pair.Second));

    internal static bool IsTypeCompatible(ValidationContext context, NodeId? actualTypeId, NodeId? declaredTypeId)
    {
        if (NodeId.IsNull(declaredTypeId))
        {
            return true;
        }

        if (NodeId.IsNull(actualTypeId))
        {
            return false;
        }

        return actualTypeId == declaredTypeId || context.TypeModel.IsSameOrSubtype(actualTypeId, declaredTypeId);
    }

    internal static bool IsReferenceTypeCompatible(ValidationContext context, NodeId actualReferenceTypeId, NodeId expectedReferenceTypeId) =>
        actualReferenceTypeId == expectedReferenceTypeId ||
        context.TypeModel.IsSameOrSubtype(actualReferenceTypeId, expectedReferenceTypeId);

    internal static bool IsCoveredByPlaceholder(
        ValidationContext context,
        ChildLink child,
        IEnumerable<InstanceDeclaration> placeholderDeclarations) =>
        placeholderDeclarations.Any(declaration =>
            IsReferenceTypeCompatible(context, child.Reference.ReferenceTypeId, declaration.ReferenceTypeId) &&
            IsTypeCompatible(context, child.Child.TypeDefinitionId, declaration.TypeDefinitionId));

    internal static bool TryGetDeclarationVariable(
        ValidationContext context,
        InstanceDeclaration declaration,
        [NotNullWhen(true)] out BaseVariableState? variable)
    {
        variable = null;
        if (!context.TypeModel.TryGetNode(declaration.NodeId, out var declarationNode) ||
            declarationNode is not BaseVariableState variableState)
        {
            return false;
        }

        variable = variableState;
        return true;
    }

    internal readonly record struct DeclaringTypeReference(string? NamespaceUri, string? ReferenceUrl);

    /// <summary>
    /// Resolves reference metadata for a declaration-bound finding: the declaring companion
    /// specification's namespace URI (where the declaration lives) and, when available, a deep
    /// documentation URL captured from the NodeSet2 <c>Documentation</c> element of the declaration
    /// node or the type that declares it (the immediate parent declaration's TypeDefinition, or the
    /// validated node's TypeDefinition for a direct child).
    /// </summary>
    internal static DeclaringTypeReference ResolveDeclaringTypeReference(
        ValidationContext context,
        LiveNode node,
        InstanceDeclaration declaration,
        IReadOnlyList<InstanceDeclaration> declarations)
    {
        var namespaceUri = context.ResolveModelNamespaceUri(declaration.NodeId.NamespaceIndex);

        string? referenceUrl = null;
        if (context.TypeModel.TryGetDocumentation(declaration.NodeId, out var declarationDoc))
        {
            referenceUrl = declarationDoc;
        }
        else
        {
            var declaringTypeId = ResolveDeclaringTypeId(node, declaration, declarations);
            if (!NodeId.IsNull(declaringTypeId) &&
                context.TypeModel.TryGetDocumentation(declaringTypeId!, out var typeDoc))
            {
                referenceUrl = typeDoc;
            }
        }

        return new DeclaringTypeReference(
            string.IsNullOrWhiteSpace(namespaceUri) ? null : namespaceUri,
            referenceUrl);
    }

    internal static NodeId? ResolveDeclaringTypeId(
        LiveNode node,
        InstanceDeclaration declaration,
        IReadOnlyList<InstanceDeclaration> declarations)
    {
        if (declaration.BrowsePath.Count <= 1)
        {
            return node.TypeDefinitionId;
        }

        var parentPath = declaration.BrowsePath.Take(declaration.BrowsePath.Count - 1).ToArray();
        var parent = declarations.FirstOrDefault(candidate => BrowsePathEquals(candidate.BrowsePath, parentPath));
        return parent?.TypeDefinitionId ?? node.TypeDefinitionId;
    }

    internal static string FormatBrowsePath(IEnumerable<QualifiedName> browsePath) =>
        string.Join("/", browsePath.Select(FormatBrowseName));

    internal static string FormatBrowseName(QualifiedName browseName) =>
        $"{browseName.NamespaceIndex}:{browseName.Name}";

    internal static string FormatNodeId(NodeId? nodeId) =>
        NodeId.IsNull(nodeId) ? "<null>" : nodeId.ToString();

    /// <summary>
    /// Formats a type-model NodeId as an ExpandedNodeId (namespace URI + identifier) so identifiers
    /// such as <c>i=58</c> are unambiguous across companion specifications. Falls back to the bare
    /// NodeId when the namespace URI cannot be resolved from the type model.
    /// </summary>
    internal static string FormatExpandedNodeId(ValidationContext context, NodeId? nodeId)
    {
        if (NodeId.IsNull(nodeId))
        {
            return "<null>";
        }

        var uri = context.ResolveModelNamespaceUri(nodeId!.NamespaceIndex);
        return string.IsNullOrWhiteSpace(uri)
            ? nodeId.ToString()
            : new ExpandedNodeId(nodeId, uri).ToString();
    }

    /// <summary>
    /// Formats any model NodeId for finding details as "&lt;ns:BrowseName&gt; (&lt;ExpandedNodeId&gt;)"
    /// when the node's BrowseName is known in the type model, otherwise its ExpandedNodeId, falling
    /// back to the bare NodeId. Preferred over <see cref="FormatNodeId"/>/<see cref="FormatExpandedNodeId"/>
    /// in Evidence so numeric identifiers such as <c>i=23518</c> read as
    /// <c>0:OrderedListType (nsu=...;i=23518)</c>. Genuine String identifiers keep their native
    /// <c>;s=</c> form via <see cref="FormatExpandedNodeId"/>.
    /// </summary>
    internal static string FormatNode(ValidationContext context, NodeId? nodeId)
    {
        if (NodeId.IsNull(nodeId))
        {
            return "<null>";
        }

        var expanded = FormatExpandedNodeId(context, nodeId);
        if (context.TypeModel.TryGetNode(nodeId!, out var node) &&
            node is not null &&
            !QualifiedName.IsNull(node.BrowseName))
        {
            return $"{FormatBrowseName(node.BrowseName)} ({expanded})";
        }

        return expanded;
    }

    /// <summary>
    /// Formats a type definition for finding details as "&lt;BrowseName&gt; (&lt;ExpandedNodeId&gt;)"
    /// when the type node is known, otherwise just its ExpandedNodeId. Used to name the type whose
    /// declaration a rule is checking against.
    /// </summary>
    internal static string FormatType(ValidationContext context, NodeId? typeId)
    {
        if (NodeId.IsNull(typeId))
        {
            return "<unknown type>";
        }

        var expanded = FormatExpandedNodeId(context, typeId);
        if (context.TypeModel.TryGetNode(typeId!, out var typeNode) &&
            typeNode is not null &&
            !QualifiedName.IsNull(typeNode.BrowseName))
        {
            return $"{FormatBrowseName(typeNode.BrowseName)} ({expanded})";
        }

        return expanded;
    }
}
