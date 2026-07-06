using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class AbstractTypeInstantiationRule : IValidationRule
{
    public string RuleId => "GEN-11";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Object and Variable instances must not directly instantiate abstract TypeDefinitions.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.IsObjectOrVariable(node) && typeDefinition is BaseTypeState;

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        if (typeDefinition is not BaseTypeState { IsAbstract: true })
        {
            yield break;
        }

        yield return new ValidationFinding(
            RuleId,
            Severity,
            node.NodeId,
            GenericRuleHelpers.FormatBrowseName(node.BrowseName),
            "Instance directly uses an abstract TypeDefinition.",
            $"TypeDefinition={GenericRuleHelpers.FormatNodeId(node.TypeDefinitionId)}.");
    }
}
