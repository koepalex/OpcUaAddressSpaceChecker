using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Specific;

public sealed class DeviceSetPlacementRule : IValidationRule
{
    private static readonly PlacementRegistry PlacementRegistry = new();

    public string RuleId => "DI-02";
    public string Category => "DI";
    public Severity Severity => Severity.Warning;
    public string Description => "DI ComponentType instances are reachable from the DI DeviceSet entry point.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        PlacementRegistry.TryGetEntryForType(
            context,
            node.TypeDefinitionId,
            CompanionSpecRuleHelpers.DiModelUri,
            out _);

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        if (!PlacementRegistry.TryGetEntryForType(
                context,
                node.TypeDefinitionId,
                CompanionSpecRuleHelpers.DiModelUri,
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
                "DI ComponentType instance is not reachable from the DeviceSet entry point by hierarchical references.",
                $"Expected reachability from {entry.EntryPointModelUri}#{entry.EntryPointBrowseName}.");
        }
    }
}
