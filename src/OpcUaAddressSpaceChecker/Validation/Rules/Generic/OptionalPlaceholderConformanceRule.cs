using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class OptionalPlaceholderConformanceRule : IValidationRule
{
    public string RuleId => "GEN-07";
    public string Category => "Generic";
    public Severity Severity => Severity.Warning;
    public string Description => "Children that instantiate OptionalPlaceholder declarations must match declared type and reference constraints.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        foreach (var placeholder in declarations.Where(GenericRuleHelpers.IsOptionalPlaceholder))
        {
            IReadOnlyList<LiveNode> parentNodes = placeholder.BrowsePath.Count == 1
                ? [node]
                : GenericRuleHelpers.ResolveParentLinks(node, placeholder.BrowsePath).Select(link => link.Child).ToArray();

            foreach (var parent in parentNodes)
            {
                var concreteSiblings = GetConcreteSiblings(declarations, placeholder).ToArray();
                foreach (var child in GenericRuleHelpers.GetChildLinks(parent))
                {
                    if (concreteSiblings.Any(declaration => GenericRuleHelpers.BrowseNameEquals(child.Child.BrowseName, declaration.BrowseName)))
                    {
                        continue;
                    }

                    var typeCompatible = GenericRuleHelpers.IsTypeCompatible(context, child.Child.TypeDefinitionId, placeholder.TypeDefinitionId);
                    var referenceCompatible = GenericRuleHelpers.IsReferenceTypeCompatible(context, child.Reference.ReferenceTypeId, placeholder.ReferenceTypeId);

                    if (typeCompatible && !referenceCompatible)
                    {
                        yield return new ValidationFinding(
                            RuleId,
                            Severity,
                            child.Child.NodeId,
                            GenericRuleHelpers.FormatBrowsePath(placeholder.BrowsePath),
                            "OptionalPlaceholder child uses an incompatible reference type.",
                            $"Expected {GenericRuleHelpers.FormatNodeId(placeholder.ReferenceTypeId)} or a subtype; actual {GenericRuleHelpers.FormatNodeId(child.Reference.ReferenceTypeId)}.");
                    }
                    else if (referenceCompatible && !typeCompatible)
                    {
                        yield return new ValidationFinding(
                            RuleId,
                            Severity,
                            child.Child.NodeId,
                            GenericRuleHelpers.FormatBrowsePath(placeholder.BrowsePath),
                            "OptionalPlaceholder child uses an incompatible TypeDefinition.",
                            $"Expected {GenericRuleHelpers.FormatNodeId(placeholder.TypeDefinitionId)} or a subtype; actual {GenericRuleHelpers.FormatNodeId(child.Child.TypeDefinitionId)}.");
                    }
                }
            }
        }
    }

    private static IEnumerable<InstanceDeclaration> GetConcreteSiblings(
        IReadOnlyList<InstanceDeclaration> declarations,
        InstanceDeclaration placeholder) =>
        declarations.Where(declaration =>
            !GenericRuleHelpers.IsPlaceholder(declaration) &&
            declaration.BrowsePath.Count == placeholder.BrowsePath.Count &&
            declaration.BrowsePath.Take(declaration.BrowsePath.Count - 1)
                .SequenceEqual(placeholder.BrowsePath.Take(placeholder.BrowsePath.Count - 1), QualifiedNameComparer.Instance));

    private sealed class QualifiedNameComparer : IEqualityComparer<QualifiedName>
    {
        internal static readonly QualifiedNameComparer Instance = new();

        public bool Equals(QualifiedName? x, QualifiedName? y) =>
            x is not null && y is not null && GenericRuleHelpers.BrowseNameEquals(x, y);

        public int GetHashCode(QualifiedName obj) =>
            HashCode.Combine(obj.NamespaceIndex, obj.Name);
    }
}
