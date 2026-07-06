using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class MandatoryPlaceholderRule : IValidationRule
{
    public string RuleId => "GEN-06";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "MandatoryPlaceholder declarations require at least one matching child.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        foreach (var declaration in declarations.Where(GenericRuleHelpers.IsMandatoryPlaceholder))
        {
            if (GenericRuleHelpers.IsSuppressedByMissingOptionalAncestor(node, declaration, declarations))
            {
                continue;
            }

            IReadOnlyList<LiveNode> parentNodes = declaration.BrowsePath.Count == 1
                ? [node]
                : GenericRuleHelpers.ResolveParentLinks(node, declaration.BrowsePath).Select(link => link.Child).ToArray();

            foreach (var parent in parentNodes)
            {
                var matchingChildren = GenericRuleHelpers.GetChildLinks(parent).Count(child =>
                    GenericRuleHelpers.IsReferenceTypeCompatible(context, child.Reference.ReferenceTypeId, declaration.ReferenceTypeId) &&
                    GenericRuleHelpers.IsTypeCompatible(context, child.Child.TypeDefinitionId, declaration.TypeDefinitionId));

                if (matchingChildren > 0)
                {
                    continue;
                }

                yield return new ValidationFinding(
                    RuleId,
                    Severity,
                    parent.NodeId,
                    GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                    "MandatoryPlaceholder declaration has no matching child.",
                    $"Expected at least one child with ReferenceType {GenericRuleHelpers.FormatNodeId(declaration.ReferenceTypeId)} and TypeDefinition {GenericRuleHelpers.FormatNodeId(declaration.TypeDefinitionId)} or subtypes.");
            }
        }
    }
}
