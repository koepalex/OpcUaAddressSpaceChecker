using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class NestedBrowsePathRule : IValidationRule
{
    public string RuleId => "GEN-09";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Nested InstanceDeclarations must appear at their declared structural BrowsePath, not directly below the instance root.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context) && node.Children.Count > 0;

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        var directDeclarations = declarations
            .Where(declaration => declaration.BrowsePath.Count == 1)
            .ToArray();
        var nestedDeclarations = declarations
            .Where(declaration => declaration.BrowsePath.Count > 1)
            .ToArray();

        foreach (var directChild in GenericRuleHelpers.GetChildLinks(node))
        {
            foreach (var nestedDeclaration in nestedDeclarations.Where(declaration =>
                         GenericRuleHelpers.BrowseNameMatchesNameOnly(declaration.BrowseName, directChild.Child.BrowseName) &&
                         !directDeclarations.Any(directDeclaration => GenericRuleHelpers.BrowseNameEquals(directDeclaration.BrowseName, directChild.Child.BrowseName))))
            {
                var directRef = GenericRuleHelpers.ResolveDeclaringTypeReference(context, node, nestedDeclaration, declarations);
                yield return new ValidationFinding(
                    RuleId,
                    Severity,
                    directChild.Child.NodeId,
                    GenericRuleHelpers.FormatBrowseName(directChild.Child.BrowseName),
                    "Child appears directly under the instance but is declared at a nested BrowsePath.",
                    $"Expected path {GenericRuleHelpers.FormatBrowsePath(nestedDeclaration.BrowsePath)}.",
                    directRef.NamespaceUri,
                    directRef.ReferenceUrl);
            }
        }

        foreach (var nestedDeclaration in nestedDeclarations.Where(GenericRuleHelpers.IsMandatory))
        {
            if (GenericRuleHelpers.IsSuppressedByMissingOptionalAncestor(context, node, nestedDeclaration, declarations) ||
                GenericRuleHelpers.BrowsePathExists(context, node, declarations, nestedDeclaration.BrowsePath))
            {
                continue;
            }

            var declaringRef = GenericRuleHelpers.ResolveDeclaringTypeReference(context, node, nestedDeclaration, declarations);

            if (GenericRuleHelpers.CrossesPlaceholderAncestor(declarations, nestedDeclaration))
            {
                foreach (var (instance, suffix) in GenericRuleHelpers.FindPlaceholderInstancesMissingChild(context, node, declarations, nestedDeclaration))
                {
                    var instancePath = new List<QualifiedName> { instance.BrowseName };
                    instancePath.AddRange(suffix);

                    yield return new ValidationFinding(
                        RuleId,
                        Severity,
                        instance.NodeId,
                        GenericRuleHelpers.FormatBrowsePath(instancePath),
                        "Nested mandatory child is missing below its declared parent.",
                        $"Placeholder instance {GenericRuleHelpers.FormatBrowseName(instance.BrowseName)} exists, but {GenericRuleHelpers.FormatBrowseName(nestedDeclaration.BrowseName)} is absent below it.",
                        declaringRef.NamespaceUri,
                        declaringRef.ReferenceUrl);
                }

                continue;
            }

            var parentPath = nestedDeclaration.BrowsePath.Take(nestedDeclaration.BrowsePath.Count - 1).ToArray();
            if (!GenericRuleHelpers.BrowsePathExists(context, node, declarations, parentPath))
            {
                continue;
            }

            yield return new ValidationFinding(
                RuleId,
                Severity,
                node.NodeId,
                GenericRuleHelpers.FormatBrowsePath(nestedDeclaration.BrowsePath),
                "Nested mandatory child is missing below its declared parent.",
                $"Parent path {GenericRuleHelpers.FormatBrowsePath(parentPath)} exists, but {GenericRuleHelpers.FormatBrowseName(nestedDeclaration.BrowseName)} is absent below it.",
                declaringRef.NamespaceUri,
                declaringRef.ReferenceUrl);
        }
    }
}
