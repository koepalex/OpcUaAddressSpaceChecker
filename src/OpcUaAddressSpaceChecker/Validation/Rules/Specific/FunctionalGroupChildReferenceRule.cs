using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Specific;

public sealed class FunctionalGroupChildReferenceRule : IValidationRule
{
    public string RuleId => "DI-06";
    public string Category => "DI";
    public Severity Severity => Severity.Warning;
    public string Description => "DI FunctionalGroupType children use HasComponent or Organizes, not HasProperty.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        CompanionSpecRuleHelpers.TypeDerivesFrom(
            context,
            node.TypeDefinitionId,
            CompanionSpecRuleHelpers.DiModelUri,
            "FunctionalGroupType");

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        foreach (var childLink in CompanionSpecRuleHelpers.GetChildLinks(node))
        {
            var usesAllowedReference =
                CompanionSpecRuleHelpers.IsReferenceTypeCompatible(
                    context,
                    childLink.Reference.ReferenceTypeId,
                    CompanionSpecRuleHelpers.HasComponent) ||
                CompanionSpecRuleHelpers.IsReferenceTypeCompatible(
                    context,
                    childLink.Reference.ReferenceTypeId,
                    CompanionSpecRuleHelpers.Organizes);

            if (!usesAllowedReference)
            {
                yield return new ValidationFinding(
                    RuleId,
                    Severity.Warning,
                    childLink.Child.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/{CompanionSpecRuleHelpers.FormatNode(childLink.Child)}",
                    "Child below a DI FunctionalGroupType instance is not linked with HasComponent or Organizes.",
                    $"Actual reference type: {childLink.Reference.ReferenceTypeId}.");
            }
        }
    }
}
