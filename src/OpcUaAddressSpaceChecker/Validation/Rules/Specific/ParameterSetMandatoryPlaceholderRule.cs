using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Specific;

public sealed class ParameterSetMandatoryPlaceholderRule : IValidationRule
{
    public string RuleId => "DI-03";
    public string Category => "DI";
    public Severity Severity => Severity.Error;
    public string Description => "DI ParameterSet and MethodSet optional structures have required contents when present.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        CompanionSpecRuleHelpers.TypeDerivesFrom(
            context,
            node.TypeDefinitionId,
            CompanionSpecRuleHelpers.DiModelUri,
            "TopologyElementType");

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        foreach (var parameterSet in CompanionSpecRuleHelpers.FindDirectChildren(
                     node,
                     "ParameterSet",
                     context,
                     CompanionSpecRuleHelpers.DiModelUri))
        {
            var hasParameterVariable = CompanionSpecRuleHelpers.GetChildLinks(parameterSet.Child).Any(link =>
                link.Child.NodeClass == NodeClass.Variable &&
                CompanionSpecRuleHelpers.IsReferenceTypeCompatible(
                    context,
                    link.Reference.ReferenceTypeId,
                    CompanionSpecRuleHelpers.HasComponent) &&
                CompanionSpecRuleHelpers.IsTypeCompatible(
                    context,
                    link.Child.TypeDefinitionId,
                    CompanionSpecRuleHelpers.BaseDataVariableType));

            if (!hasParameterVariable)
            {
                yield return new ValidationFinding(
                    RuleId,
                    Severity.Error,
                    parameterSet.Child.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/ParameterSet",
                    "DI ParameterSet is present but does not contain any parameter Variable child.",
                    "TopologyElementType declares a MandatoryPlaceholder parameter under ParameterSet.");
            }
        }

        foreach (var methodSet in CompanionSpecRuleHelpers.FindDirectChildren(
                     node,
                     "MethodSet",
                     context,
                     CompanionSpecRuleHelpers.DiModelUri))
        {
            var hasMethod = CompanionSpecRuleHelpers.GetChildLinks(methodSet.Child).Any(link =>
                link.Child.NodeClass == NodeClass.Method &&
                CompanionSpecRuleHelpers.IsReferenceTypeCompatible(
                    context,
                    link.Reference.ReferenceTypeId,
                    CompanionSpecRuleHelpers.HasComponent));

            if (!hasMethod)
            {
                yield return new ValidationFinding(
                    RuleId,
                    Severity.Warning,
                    methodSet.Child.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/MethodSet",
                    "DI MethodSet is present but does not contain any Method child.",
                    "MethodSet is expected to group Methods exposed by the TopologyElementType instance.");
            }
        }
    }
}
