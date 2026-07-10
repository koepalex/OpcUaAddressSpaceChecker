using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class HasTypeDefinitionPresenceRule : IValidationRule
{
    public string RuleId => "GEN-10";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Object and Variable instances must expose a HasTypeDefinition.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.IsObjectOrVariable(node) && !GenericRuleHelpers.IsCoreNamespace(node);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        if (!NodeId.IsNull(node.TypeDefinitionId))
        {
            yield break;
        }

        yield return new ValidationFinding(
            RuleId,
            Severity,
            node.NodeId,
            GenericRuleHelpers.FormatBrowseName(node.BrowseName),
            "Object or Variable has no HasTypeDefinition.",
            "The materialized live node did not contain a TypeDefinition NodeId.");
    }
}
