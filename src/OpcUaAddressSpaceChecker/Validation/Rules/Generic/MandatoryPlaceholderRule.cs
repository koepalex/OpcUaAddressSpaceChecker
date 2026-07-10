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
            if (GenericRuleHelpers.IsSuppressedByMissingOptionalAncestor(context, node, declaration, declarations))
            {
                continue;
            }

            var declaringRef = GenericRuleHelpers.ResolveDeclaringTypeReference(context, node, declaration, declarations);
            var declaringTypeId = GenericRuleHelpers.ResolveDeclaringTypeId(node, declaration, declarations);
            var declaringType = GenericRuleHelpers.FormatType(context, declaringTypeId);
            var placeholderName = GenericRuleHelpers.FormatBrowseName(declaration.BrowseName);

            IReadOnlyList<LiveNode> parentNodes = declaration.BrowsePath.Count == 1
                ? [node]
                : GenericRuleHelpers.ResolveParentLinks(context, node, declarations, declaration.BrowsePath).Select(link => link.Child).ToArray();

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
                    $"MandatoryPlaceholder '{placeholderName}' has no matching child.",
                    $"Declared by type {declaringType}. Expected at least one child with ReferenceType " +
                    $"{GenericRuleHelpers.FormatNode(context, declaration.ReferenceTypeId)} and TypeDefinition " +
                    $"{GenericRuleHelpers.FormatNode(context, declaration.TypeDefinitionId)} or subtypes.",
                    declaringRef.NamespaceUri,
                    declaringRef.ReferenceUrl);
            }
        }
    }
}
