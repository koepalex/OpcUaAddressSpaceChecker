using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class ChildTypeDefinitionIncompatibilityRule : IValidationRule
{
    public string RuleId => "GEN-03";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Instance child TypeDefinitions must match or subtype the declared TypeDefinition.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        foreach (var declaration in GenericRuleHelpers.ConcreteDeclarations(context, node)
                     .Where(declaration => !NodeId.IsNull(declaration.TypeDefinitionId)))
        {
            foreach (var match in GenericRuleHelpers.FindChildrenByBrowsePath(node, declaration.BrowsePath))
            {
                if (GenericRuleHelpers.IsTypeCompatible(context, match.Child.TypeDefinitionId, declaration.TypeDefinitionId))
                {
                    continue;
                }

                yield return new ValidationFinding(
                    RuleId,
                    Severity,
                    match.Child.NodeId,
                    GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                    "Child TypeDefinition is not compatible with its InstanceDeclaration.",
                    $"Expected {GenericRuleHelpers.FormatNodeId(declaration.TypeDefinitionId)} or a subtype; actual {GenericRuleHelpers.FormatNodeId(match.Child.TypeDefinitionId)}.");
            }
        }
    }
}
