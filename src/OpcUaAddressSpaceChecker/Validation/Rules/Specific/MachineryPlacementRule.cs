using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Specific;

public sealed class MachineryPlacementRule : IValidationRule
{
    private static readonly PlacementRegistry PlacementRegistry = new();

    public string RuleId => "MACHINERY-01";
    public string Category => "Machinery";
    public Severity Severity => Severity.Warning;
    public string Description => "Machinery machine instances are reachable from the Machinery Machines entry point.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        PlacementRegistry.TryGetEntryForType(
            context,
            node.TypeDefinitionId,
            CompanionSpecRuleHelpers.MachineryModelUri,
            out _);

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        if (!PlacementRegistry.TryGetEntryForType(
                context,
                node.TypeDefinitionId,
                CompanionSpecRuleHelpers.MachineryModelUri,
                out var entry) ||
            entry == null)
        {
            yield break;
        }

        if (!PlacementRegistry.IsReachableFromEntryPoint(context, entry, node.NodeId))
        {
            yield return new ValidationFinding(
                RuleId,
                Severity.Warning,
                node.NodeId,
                CompanionSpecRuleHelpers.FormatNode(node),
                "Machinery machine instance is not reachable from the Machines entry point by hierarchical references.",
                $"Expected reachability from {entry.EntryPointModelUri}#{entry.EntryPointBrowseName}.",
                Confidence: context.AbsenceConfidence);
        }
    }
}
