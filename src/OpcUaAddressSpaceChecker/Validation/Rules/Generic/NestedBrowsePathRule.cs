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
                yield return new ValidationFinding(
                    RuleId,
                    Severity,
                    directChild.Child.NodeId,
                    GenericRuleHelpers.FormatBrowseName(directChild.Child.BrowseName),
                    "Child appears directly under the instance but is declared at a nested BrowsePath.",
                    $"Expected path {GenericRuleHelpers.FormatBrowsePath(nestedDeclaration.BrowsePath)}.");
            }
        }

        foreach (var nestedDeclaration in nestedDeclarations.Where(GenericRuleHelpers.IsMandatory))
        {
            if (GenericRuleHelpers.IsSuppressedByMissingOptionalAncestor(node, nestedDeclaration, declarations) ||
                GenericRuleHelpers.BrowsePathExists(node, nestedDeclaration.BrowsePath))
            {
                continue;
            }

            var parentPath = nestedDeclaration.BrowsePath.Take(nestedDeclaration.BrowsePath.Count - 1).ToArray();
            if (!GenericRuleHelpers.BrowsePathExists(node, parentPath))
            {
                continue;
            }

            yield return new ValidationFinding(
                RuleId,
                Severity,
                node.NodeId,
                GenericRuleHelpers.FormatBrowsePath(nestedDeclaration.BrowsePath),
                "Nested mandatory child is missing below its declared parent.",
                $"Parent path {GenericRuleHelpers.FormatBrowsePath(parentPath)} exists, but {GenericRuleHelpers.FormatBrowseName(nestedDeclaration.BrowseName)} is absent below it.");
        }
    }
}
