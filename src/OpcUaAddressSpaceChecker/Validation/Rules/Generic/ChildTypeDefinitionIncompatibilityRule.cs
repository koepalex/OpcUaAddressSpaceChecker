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
        var declarations = context.GetInstanceDeclarations(node);
        foreach (var declaration in GenericRuleHelpers.ConcreteDeclarations(context, node)
                     .Where(declaration => !NodeId.IsNull(declaration.TypeDefinitionId)))
        {
            var declaringRef = GenericRuleHelpers.ResolveDeclaringTypeReference(context, node, declaration, declarations);
            var declaringTypeId = GenericRuleHelpers.ResolveDeclaringTypeId(node, declaration, declarations);
            var declaringType = GenericRuleHelpers.FormatType(context, declaringTypeId);
            foreach (var match in GenericRuleHelpers.FindChildrenByBrowsePath(context, node, declarations, declaration.BrowsePath))
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
                    $"Child {GenericRuleHelpers.FormatBrowseName(match.Child.BrowseName)} declared by type {declaringType}. " +
                    $"Expected TypeDefinition {GenericRuleHelpers.FormatNode(context, declaration.TypeDefinitionId)} or a subtype; " +
                    $"actual {GenericRuleHelpers.FormatNode(context, match.Child.TypeDefinitionId)}.",
                    declaringRef.NamespaceUri,
                    declaringRef.ReferenceUrl);
            }
        }
    }
}
