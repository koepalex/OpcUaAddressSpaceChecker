using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class MissingMandatoryChildRule : IValidationRule
{
    public string RuleId => "GEN-01";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Mandatory InstanceDeclarations must be present on instances at the declared BrowsePath.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        foreach (var declaration in declarations.Where(GenericRuleHelpers.IsMandatory))
        {
            if (GenericRuleHelpers.IsSuppressedByMissingOptionalAncestor(node, declaration, declarations) ||
                GenericRuleHelpers.BrowsePathExists(node, declaration.BrowsePath))
            {
                continue;
            }

            yield return new ValidationFinding(
                RuleId,
                Severity,
                node.NodeId,
                GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                "Mandatory child is missing.",
                $"Expected declaration {GenericRuleHelpers.FormatBrowseName(declaration.BrowseName)} with NodeClass {declaration.NodeClass}.");
        }
    }
}
