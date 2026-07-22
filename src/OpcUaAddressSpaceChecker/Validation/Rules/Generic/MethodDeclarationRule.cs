using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class MethodDeclarationRule : IValidationRule
{
    public string RuleId => "GEN-12";
    public string Category => "Generic";
    public Severity Severity => Severity.Warning;
    public string Description => "Present Method instances expose their declared InputArguments and OutputArguments properties.";

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
            var matches = GenericRuleHelpers.FindChildrenByBrowsePath(context, node, declarations, declaration.BrowsePath);
            if (matches.Count == 0)
            {
                continue;
            }

            foreach (var argumentDeclaration in GetArgumentDeclarations(declarations, declaration))
            {
                if (GenericRuleHelpers.BrowsePathExists(context, node, declarations, argumentDeclaration.BrowsePath))
                {
                    continue;
                }

                var argumentRef = GenericRuleHelpers.ResolveDeclaringTypeReference(context, node, argumentDeclaration, declarations);
                foreach (var match in matches)
                {
                    yield return new ValidationFinding(
                        RuleId,
                        Severity,
                        match.Child.NodeId,
                        GenericRuleHelpers.FormatBrowsePath(argumentDeclaration.BrowsePath),
                        "Method argument property declared by the type is missing on the instance method.",
                        $"Expected {GenericRuleHelpers.FormatBrowseName(argumentDeclaration.BrowseName)}.",
                        argumentRef.NamespaceUri,
                        argumentRef.ReferenceUrl,
                        context.AbsenceConfidence);
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
