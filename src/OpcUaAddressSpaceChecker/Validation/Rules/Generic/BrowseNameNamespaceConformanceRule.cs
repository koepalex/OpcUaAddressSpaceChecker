using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class BrowseNameNamespaceConformanceRule : IValidationRule
{
    public string RuleId => "GEN-14";
    public string Category => "Generic";
    public Severity Severity => Severity.Warning;
    public string Description => "Instance child BrowseName namespace indexes should match matching InstanceDeclarations.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        foreach (var declaration in GenericRuleHelpers.ConcreteDeclarations(context, node))
        {
            IReadOnlyList<LiveNode> parentNodes = declaration.BrowsePath.Count == 1
                ? [node]
                : GenericRuleHelpers.ResolveParentLinks(context, node, declarations, declaration.BrowsePath).Select(link => link.Child).ToArray();

            foreach (var parent in parentNodes)
            {
                foreach (var child in GenericRuleHelpers.GetChildLinks(parent))
                {
                    if (!GenericRuleHelpers.BrowseNameMatchesNameOnly(child.Child.BrowseName, declaration.BrowseName) ||
                        child.Child.BrowseName.NamespaceIndex == declaration.BrowseName.NamespaceIndex)
                    {
                        continue;
                    }

                    yield return new ValidationFinding(
                        RuleId,
                        Severity,
                        child.Child.NodeId,
                        GenericRuleHelpers.FormatBrowseName(child.Child.BrowseName),
                        "Child BrowseName name matches the declaration but its namespace index differs.",
                        $"Expected namespace index {declaration.BrowseName.NamespaceIndex}; actual {child.Child.BrowseName.NamespaceIndex} for declaration path {GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath)}.");
                }
            }
        }
    }
}
