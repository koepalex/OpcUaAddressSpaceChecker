using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class UnexpectedChildRule : IValidationRule
{
    public string RuleId => "GEN-05";
    public string Category => "Generic";
    public Severity Severity => Severity.Warning;
    public string Description => "Warns when a direct instance child is not covered by concrete or placeholder declarations.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context) && node.Children.Count > 0;

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = GenericRuleHelpers.DirectDeclarations(context, node);
        var concreteDeclarations = declarations.Where(declaration => !GenericRuleHelpers.IsPlaceholder(declaration)).ToArray();
        var placeholderDeclarations = declarations.Where(GenericRuleHelpers.IsPlaceholder).ToArray();

        foreach (var child in GenericRuleHelpers.GetChildLinks(node))
        {
            var hasConcreteDeclaration = concreteDeclarations.Any(declaration =>
                GenericRuleHelpers.BrowseNameEquals(child.Child.BrowseName, declaration.BrowseName));
            if (hasConcreteDeclaration || GenericRuleHelpers.IsCoveredByPlaceholder(context, child, placeholderDeclarations))
            {
                continue;
            }

            yield return new ValidationFinding(
                RuleId,
                Severity,
                child.Child.NodeId,
                GenericRuleHelpers.FormatBrowseName(child.Child.BrowseName),
                "Child is not covered by the instance's TypeDefinition declarations.",
                $"ReferenceType={GenericRuleHelpers.FormatNode(context, child.Reference.ReferenceTypeId)}, TypeDefinition={GenericRuleHelpers.FormatNode(context, child.Child.TypeDefinitionId)}.");
        }
    }
}
