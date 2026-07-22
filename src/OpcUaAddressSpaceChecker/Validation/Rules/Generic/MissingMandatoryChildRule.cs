using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class MissingMandatoryChildRule : IValidationRule
{
    public string RuleId => "GEN-01";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Mandatory InstanceDeclarations must be present on instances at the declared BrowsePath.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        foreach (var declaration in declarations.Where(GenericRuleHelpers.IsMandatory))
        {
            if (GenericRuleHelpers.IsSuppressedByMissingOptionalAncestor(context, node, declaration, declarations) ||
                GenericRuleHelpers.IsSuppressedByMissingRequiredAncestor(context, node, declaration, declarations))
            {
                continue;
            }

            var declaringRef = GenericRuleHelpers.ResolveDeclaringTypeReference(context, node, declaration, declarations);

            if (GenericRuleHelpers.CrossesPlaceholderAncestor(declarations, declaration))
            {
                foreach (var (instance, suffix) in GenericRuleHelpers.FindPlaceholderInstancesMissingChild(context, node, declarations, declaration))
                {
                    var instancePath = new List<QualifiedName> { instance.BrowseName };
                    instancePath.AddRange(suffix);

                    yield return new ValidationFinding(
                        RuleId,
                        Severity,
                        instance.NodeId,
                        GenericRuleHelpers.FormatBrowsePath(instancePath),
                        "Mandatory child is missing.",
                        $"Placeholder instance {GenericRuleHelpers.FormatBrowseName(instance.BrowseName)} is missing {GenericRuleHelpers.FormatBrowseName(declaration.BrowseName)} with NodeClass {declaration.NodeClass}.",
                        declaringRef.NamespaceUri,
                        declaringRef.ReferenceUrl,
                        context.AbsenceConfidence);
                }

                continue;
            }

            if (GenericRuleHelpers.BrowsePathExists(context, node, declarations, declaration.BrowsePath))
            {
                continue;
            }

            yield return new ValidationFinding(
                RuleId,
                Severity,
                node.NodeId,
                GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                "Mandatory child is missing.",
                $"Expected declaration {GenericRuleHelpers.FormatBrowseName(declaration.BrowseName)} with NodeClass {declaration.NodeClass}.",
                declaringRef.NamespaceUri,
                declaringRef.ReferenceUrl,
                context.AbsenceConfidence);
        }
    }
}
