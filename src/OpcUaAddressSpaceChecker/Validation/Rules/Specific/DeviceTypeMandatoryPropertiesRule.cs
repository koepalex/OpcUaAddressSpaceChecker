using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Specific;

public sealed class DeviceTypeMandatoryPropertiesRule : IValidationRule
{
    private static readonly string[] MandatoryProperties =
    [
        "Manufacturer",
        "Model",
        "HardwareRevision",
        "SoftwareRevision",
        "DeviceRevision",
        "DeviceManual",
        "SerialNumber",
        "RevisionCounter"
    ];

    public string RuleId => "DI-01";
    public string Category => "DI";
    public Severity Severity => Severity.Error;
    public string Description => "DeviceType instances expose all mandatory DI nameplate properties.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        CompanionSpecRuleHelpers.TypeDerivesFrom(
            context,
            node.TypeDefinitionId,
            CompanionSpecRuleHelpers.DiModelUri,
            "DeviceType");

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        var propertyLinks = CompanionSpecRuleHelpers.GetChildLinks(node)
            .Where(link => CompanionSpecRuleHelpers.IsReferenceTypeCompatible(
                context,
                link.Reference.ReferenceTypeId,
                CompanionSpecRuleHelpers.HasProperty))
            .ToArray();

        foreach (var propertyName in MandatoryProperties)
        {
            var found = propertyLinks.Any(link => CompanionSpecRuleHelpers.BrowseNameMatches(
                link.Child.BrowseName,
                propertyName,
                context,
                CompanionSpecRuleHelpers.DiModelUri));

            if (!found)
            {
                yield return new ValidationFinding(
                    RuleId,
                    Severity.Error,
                    node.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/{propertyName}",
                    $"DeviceType instance is missing mandatory DI property '{propertyName}'.",
                    "OPC UA DI DeviceType requires Manufacturer, Model, HardwareRevision, SoftwareRevision, DeviceRevision, DeviceManual, SerialNumber, and RevisionCounter.",
                    Confidence: context.AbsenceConfidence);
            }
        }
    }
}
