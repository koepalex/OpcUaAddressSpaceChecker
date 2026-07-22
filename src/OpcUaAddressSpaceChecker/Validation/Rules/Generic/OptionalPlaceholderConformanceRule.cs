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
        var optionalPlaceholders = declarations
            .Where(GenericRuleHelpers.IsOptionalPlaceholder)
            .GroupBy(
                placeholder => GenericRuleHelpers.FormatBrowsePath(
                    placeholder.BrowsePath.Take(placeholder.BrowsePath.Count - 1).ToArray()),
                StringComparer.Ordinal);

        foreach (var placeholderGroup in optionalPlaceholders)
        {
            var placeholders = placeholderGroup.ToArray();
            var representative = placeholders[0];
            IReadOnlyList<LiveNode> parentNodes = representative.BrowsePath.Count == 1
                ? [node]
                : GenericRuleHelpers.ResolveParentLinks(context, node, declarations, representative.BrowsePath)
                    .Select(link => link.Child)
                    .ToArray();
            var concreteSiblings = GetConcreteSiblings(declarations, representative).ToArray();

            foreach (var parent in parentNodes)
            {
                foreach (var child in GenericRuleHelpers.GetChildLinks(parent))
                {
                    if (concreteSiblings.Any(declaration => GenericRuleHelpers.BrowseNameEquals(child.Child.BrowseName, declaration.BrowseName)))
                    {
                        continue;
                    }

                    var candidates = placeholders
                        .Where(placeholder => placeholder.NodeClass == child.Child.NodeClass)
                        .Select(placeholder => new PlaceholderCandidate(
                            placeholder,
                            GenericRuleHelpers.IsTypeCompatible(context, child.Child.TypeDefinitionId, placeholder.TypeDefinitionId),
                            GenericRuleHelpers.IsReferenceTypeCompatible(context, child.Reference.ReferenceTypeId, placeholder.ReferenceTypeId)))
                        .ToArray();

                    if (candidates.Any(candidate => candidate.TypeCompatible && candidate.ReferenceCompatible))
                    {
                        continue;
                    }

                    var plausible = candidates
                        .Where(candidate => candidate.TypeCompatible && !candidate.ReferenceCompatible)
                        .OrderBy(candidate => candidate.Declaration.BrowseName.Name, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (plausible == null)
                    {
                        continue;
                    }

                    yield return new ValidationFinding(
                        RuleId,
                        Severity,
                        child.Child.NodeId,
                        GenericRuleHelpers.FormatBrowsePath(plausible.Declaration.BrowsePath),
                        "OptionalPlaceholder child uses an incompatible reference type.",
                        $"Expected {GenericRuleHelpers.FormatNode(context, plausible.Declaration.ReferenceTypeId)} or a subtype; actual {GenericRuleHelpers.FormatNode(context, child.Reference.ReferenceTypeId)}.");
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

    private sealed record PlaceholderCandidate(
        InstanceDeclaration Declaration,
        bool TypeCompatible,
        bool ReferenceCompatible);
}
