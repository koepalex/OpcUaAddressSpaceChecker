using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class ReferenceTypeConformanceRule : IValidationRule
{
    public string RuleId => "GEN-08";
    public string Category => "Generic";
    public Severity Severity => Severity.Warning;
    public string Description => "Variable children must use reference types consistent with their declaration and variable kind.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        node.Children.Count > 0;

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        foreach (var child in GenericRuleHelpers.GetChildLinks(node).Where(child => child.Child.NodeClass == NodeClass.Variable))
        {
            var isProperty = GenericRuleHelpers.IsTypeCompatible(context, child.Child.TypeDefinitionId, GenericRuleHelpers.PropertyType);
            var isDataVariable = GenericRuleHelpers.IsTypeCompatible(context, child.Child.TypeDefinitionId, GenericRuleHelpers.BaseDataVariableType) && !isProperty;

            if (isProperty &&
                !GenericRuleHelpers.IsReferenceTypeCompatible(context, child.Reference.ReferenceTypeId, GenericRuleHelpers.HasProperty))
            {
                yield return CreateFinding(child, "Property variable is not referenced with HasProperty.", GenericRuleHelpers.HasProperty);
            }

            if (isDataVariable &&
                !GenericRuleHelpers.IsReferenceTypeCompatible(context, child.Reference.ReferenceTypeId, GenericRuleHelpers.HasComponent))
            {
                yield return CreateFinding(child, "DataVariable is not referenced with HasComponent.", GenericRuleHelpers.HasComponent);
            }

            if (isProperty && GenericRuleHelpers.GetChildLinks(child.Child).Any(grandchild =>
                    GenericRuleHelpers.IsReferenceTypeCompatible(context, grandchild.Reference.ReferenceTypeId, GenericRuleHelpers.HasProperty)))
            {
                yield return new ValidationFinding(
                    RuleId,
                    Severity,
                    child.Child.NodeId,
                    GenericRuleHelpers.FormatBrowseName(child.Child.BrowseName),
                    "Property variable exposes nested Properties.",
                    "Properties should not be the source of HasProperty references.");
            }
        }

        foreach (var declaration in GenericRuleHelpers.ConcreteDeclarations(context, node))
        {
            if (declaration.ReferenceTypeId != GenericRuleHelpers.HasProperty &&
                declaration.ReferenceTypeId != GenericRuleHelpers.HasComponent)
            {
                continue;
            }

            foreach (var match in GenericRuleHelpers.FindChildrenByBrowsePath(node, declaration.BrowsePath))
            {
                if (GenericRuleHelpers.IsReferenceTypeCompatible(context, match.Reference.ReferenceTypeId, declaration.ReferenceTypeId))
                {
                    continue;
                }

                yield return new ValidationFinding(
                    RuleId,
                    Severity,
                    match.Child.NodeId,
                    GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                    "Child reference type does not match its InstanceDeclaration.",
                    $"Expected {GenericRuleHelpers.FormatNodeId(declaration.ReferenceTypeId)} or a subtype; actual {GenericRuleHelpers.FormatNodeId(match.Reference.ReferenceTypeId)}.");
            }
        }
    }

    private ValidationFinding CreateFinding(GenericRuleHelpers.ChildLink child, string message, NodeId expectedReferenceTypeId) =>
        new(
            RuleId,
            Severity,
            child.Child.NodeId,
            GenericRuleHelpers.FormatBrowseName(child.Child.BrowseName),
            message,
            $"Expected {GenericRuleHelpers.FormatNodeId(expectedReferenceTypeId)} or a subtype; actual {GenericRuleHelpers.FormatNodeId(child.Reference.ReferenceTypeId)}.");
}
