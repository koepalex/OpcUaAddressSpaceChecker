using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class MethodDeclarationRule : IValidationRule
{
    public string RuleId => "GEN-12";
    public string Category => "Generic";
    public Severity Severity => Severity.Warning;
    public string Description => "Method InstanceDeclarations should be present and expose declared argument properties when determinable.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        var methodDeclarations = declarations
            .Where(declaration => declaration.NodeClass == NodeClass.Method)
            .ToArray();

        foreach (var declaration in methodDeclarations)
        {
            var matches = GenericRuleHelpers.FindChildrenByBrowsePath(node, declaration.BrowsePath);
            if (matches.Count == 0)
            {
                if (GenericRuleHelpers.IsMandatory(declaration) &&
                    !GenericRuleHelpers.IsSuppressedByMissingOptionalAncestor(node, declaration, declarations))
                {
                    yield return new ValidationFinding(
                        RuleId,
                        Severity,
                        node.NodeId,
                        GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                        "Mandatory Method declaration is missing.",
                        $"Expected Method {GenericRuleHelpers.FormatBrowseName(declaration.BrowseName)}.");
                }

                continue;
            }

            foreach (var argumentDeclaration in GetArgumentDeclarations(declarations, declaration))
            {
                if (GenericRuleHelpers.BrowsePathExists(node, argumentDeclaration.BrowsePath))
                {
                    continue;
                }

                foreach (var match in matches)
                {
                    yield return new ValidationFinding(
                        RuleId,
                        Severity,
                        match.Child.NodeId,
                        GenericRuleHelpers.FormatBrowsePath(argumentDeclaration.BrowsePath),
                        "Method argument property declared by the type is missing on the instance method.",
                        $"Expected {GenericRuleHelpers.FormatBrowseName(argumentDeclaration.BrowseName)}.");
                }
            }
        }
    }

    private static IEnumerable<InstanceDeclaration> GetArgumentDeclarations(
        IReadOnlyList<InstanceDeclaration> declarations,
        InstanceDeclaration methodDeclaration) =>
        declarations.Where(declaration =>
            declaration.BrowsePath.Count == methodDeclaration.BrowsePath.Count + 1 &&
            GenericRuleHelpers.BrowsePathEquals(
                declaration.BrowsePath.Take(methodDeclaration.BrowsePath.Count).ToArray(),
                methodDeclaration.BrowsePath) &&
            declaration.BrowseName.Name is "InputArguments" or "OutputArguments");
}
