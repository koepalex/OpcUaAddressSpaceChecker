using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class NodeAddressabilityRule : IExclusiveValidationRule
{
    private static readonly uint[] IdentityAttributeIds =
    [
        Attributes.NodeClass,
        Attributes.BrowseName,
        Attributes.DisplayName
    ];

    public string RuleId => "GEN-15";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Browsed targets must remain addressable when directly browsed or read.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        if (node.HasStatusCode(StatusCodes.BadUserAccessDenied))
        {
            return false;
        }

        return node.BrowseStatusCode?.Code == StatusCodes.BadNodeIdUnknown ||
               IdentityAttributeIds.All(attributeId =>
                   node.AttributeStatusCodes.TryGetValue(attributeId, out var statusCode) &&
                   statusCode.Code == StatusCodes.BadNodeIdUnknown);
    }

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        var failedAttributes = IdentityAttributeIds
            .Where(attributeId =>
                node.AttributeStatusCodes.TryGetValue(attributeId, out var statusCode) &&
                statusCode.Code == StatusCodes.BadNodeIdUnknown)
            .Select(Attributes.GetBrowseName)
            .ToArray();

        yield return new ValidationFinding(
            RuleId,
            Severity,
            node.NodeId,
            GenericRuleHelpers.FormatBrowseName(node.BrowseName),
            "Browsed target is no longer addressable by its NodeId.",
            $"BrowseStatus={FormatStatus(node.BrowseStatusCode)}; " +
            $"BadNodeIdUnknown attributes={(failedAttributes.Length == 0 ? "(none)" : string.Join(", ", failedAttributes))}.");
    }

    private static string FormatStatus(StatusCode? statusCode) =>
        statusCode?.ToString() ?? "(not recorded)";
}
