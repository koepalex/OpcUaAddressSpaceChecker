using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class NodeClassMismatchRule : IValidationRule
{
    public string RuleId => "GEN-02";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Instance children must use the same NodeClass as their InstanceDeclaration.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        foreach (var declaration in GenericRuleHelpers.ConcreteDeclarations(context, node))
        {
            var declaringRef = GenericRuleHelpers.ResolveDeclaringTypeReference(context, node, declaration, declarations);
            foreach (var match in GenericRuleHelpers.FindChildrenByBrowsePath(context, node, declarations, declaration.BrowsePath))
            {
                if (match.Child.NodeClass == declaration.NodeClass)
                {
                    continue;
                }

                yield return new ValidationFinding(
                    RuleId,
                    Severity,
                    match.Child.NodeId,
                    GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                    "Child NodeClass does not match its InstanceDeclaration.",
                    $"Expected {declaration.NodeClass}; actual {match.Child.NodeClass}.",
                    declaringRef.NamespaceUri,
                    declaringRef.ReferenceUrl);
            }
        }
    }
}
