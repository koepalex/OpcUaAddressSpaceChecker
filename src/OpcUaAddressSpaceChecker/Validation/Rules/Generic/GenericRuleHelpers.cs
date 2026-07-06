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

    internal static IReadOnlyList<ChildLink> FindChildrenByBrowsePath(LiveNode root, IReadOnlyList<QualifiedName> browsePath)
    {
        if (browsePath.Count == 0)
        {
            return [];
        }

        IReadOnlyList<LiveNode> currentParents = [root];
        IReadOnlyList<ChildLink> currentMatches = [];

        foreach (var segment in browsePath)
        {
            var nextMatches = new List<ChildLink>();
            foreach (var parent in currentParents)
            {
                nextMatches.AddRange(GetChildLinks(parent).Where(link => BrowseNameEquals(link.Child.BrowseName, segment)));
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

    internal static bool BrowsePathExists(LiveNode root, IReadOnlyList<QualifiedName> browsePath) =>
        FindChildrenByBrowsePath(root, browsePath).Count > 0;

    internal static IReadOnlyList<ChildLink> ResolveParentLinks(LiveNode root, IReadOnlyList<QualifiedName> browsePath)
    {
        if (browsePath.Count <= 1)
        {
            return [new ChildLink(root, root, new LiveReference(new NodeId(0), root.NodeId, root.BrowseName, root.DisplayName, root.NodeClass))];
        }

        return FindChildrenByBrowsePath(root, browsePath.Take(browsePath.Count - 1).ToArray());
    }

    internal static bool IsSuppressedByMissingOptionalAncestor(
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
                !BrowsePathExists(node, prefix))
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

    internal static string FormatBrowsePath(IEnumerable<QualifiedName> browsePath) =>
        string.Join("/", browsePath.Select(FormatBrowseName));

    internal static string FormatBrowseName(QualifiedName browseName) =>
        $"{browseName.NamespaceIndex}:{browseName.Name}";

    internal static string FormatNodeId(NodeId? nodeId) =>
        NodeId.IsNull(nodeId) ? "<null>" : nodeId.ToString();
}
