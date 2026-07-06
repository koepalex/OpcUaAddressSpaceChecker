using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Specific;

public sealed class PumpConfigurationNestingRule : IValidationRule
{
    private const string ConfigurationName = "Configuration";

    public string RuleId => "PUMPS-01";
    public string Category => "Pumps";
    public Severity Severity => Severity.Warning;
    public string Description => "Pumps Configuration object is typed as ConfigurationGroupType.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        PumpConfigurationRuleSupport.AppliesToPump(node, context);

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        var configurationLinks = CompanionSpecRuleHelpers.FindDirectChildren(
            node,
            ConfigurationName,
            context,
            CompanionSpecRuleHelpers.DiModelUri);
        foreach (var configurationLink in configurationLinks)
        {
            if (!CompanionSpecRuleHelpers.IsTypeCompatible(
                    context,
                    configurationLink.Child.TypeDefinitionId,
                    CompanionSpecRuleHelpers.PumpsModelUri,
                    1024))
            {
                yield return new ValidationFinding(
                    "PUMPS-01",
                    Severity.Warning,
                    configurationLink.Child.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/{ConfigurationName}",
                    "Pump Configuration object does not use ConfigurationGroupType as its TypeDefinition.",
                    $"Actual TypeDefinition: {CompanionSpecRuleHelpers.FormatNodeId(configurationLink.Child.TypeDefinitionId)}; expected http://opcfoundation.org/UA/Pumps/#ConfigurationGroupType.");
            }
        }
    }
}

public sealed class PumpConfigurationDescendantNestingRule : IValidationRule
{
    private const string ConfigurationName = "Configuration";

    public string RuleId => "PUMPS-02";
    public string Category => "Pumps";
    public Severity Severity => Severity.Error;
    public string Description => "Pumps ConfigurationGroupType descendants are nested below Configuration.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        PumpConfigurationRuleSupport.AppliesToPump(node, context);

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        var explicitlyHandledByPumps03 = PumpConfigurationRuleSupport.MustBeNestedUnderConfiguration;

        var forbiddenNestedBrowseNames = PumpConfigurationRuleSupport.GetConfigurationDescendantDeclarations(context)
            .Where(declaration => declaration.BrowsePath.Count > 0 &&
                                  !explicitlyHandledByPumps03.Contains(
                                      declaration.BrowseName.Name,
                                      StringComparer.Ordinal))
            .Select(declaration => (
                Name: declaration.BrowseName.Name,
                ModelUri: context.TypeModel.NamespaceMap.TryGetValue(declaration.BrowseName.NamespaceIndex, out var uri)
                    ? uri
                    : string.Empty))
            .Where(item => !string.IsNullOrEmpty(item.Name) && !string.IsNullOrEmpty(item.ModelUri))
            .ToHashSet();

        foreach (var directChild in CompanionSpecRuleHelpers.GetChildLinks(node))
        {
            if (CompanionSpecRuleHelpers.BrowseNameMatches(
                    directChild.Child.BrowseName,
                    ConfigurationName,
                    context,
                    CompanionSpecRuleHelpers.DiModelUri))
            {
                continue;
            }

            var directChildNamespaceUri = context.ResolveNamespaceUri(directChild.Child.BrowseName.NamespaceIndex);
            var isForbiddenNestedName = forbiddenNestedBrowseNames.Any(item =>
                string.Equals(item.Name, directChild.Child.BrowseName.Name, StringComparison.Ordinal) &&
                string.Equals(item.ModelUri, directChildNamespaceUri, StringComparison.Ordinal));

            if (isForbiddenNestedName)
            {
                yield return new ValidationFinding(
                    "PUMPS-02",
                    Severity.Error,
                    directChild.Child.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/{CompanionSpecRuleHelpers.FormatNode(directChild.Child)}",
                    "Pump exposes ConfigurationGroupType nested content as a direct child.",
                    "ConfigurationGroupType descendants must be nested below the pump Configuration object.");
            }
        }
    }
}

public sealed class PumpDesignSystemRequirementsNestingRule : IValidationRule
{
    public string RuleId => "PUMPS-03";
    public string Category => "Pumps";
    public Severity Severity => Severity.Error;
    public string Description => "Pumps Design and SystemRequirements objects are nested below Configuration.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        PumpConfigurationRuleSupport.AppliesToPump(node, context);

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        foreach (var requiredNestedName in PumpConfigurationRuleSupport.MustBeNestedUnderConfiguration)
        {
            foreach (var directChild in CompanionSpecRuleHelpers.FindDirectChildren(
                         node,
                         requiredNestedName,
                         context,
                         CompanionSpecRuleHelpers.PumpsModelUri))
            {
                yield return new ValidationFinding(
                    "PUMPS-03",
                    Severity.Error,
                    directChild.Child.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/{requiredNestedName}",
                    $"Pump '{requiredNestedName}' object is exposed directly instead of below Configuration.",
                    $"Expected browse path: {PumpConfigurationRuleSupport.ConfigurationName}/{requiredNestedName}.");
            }
        }
    }
}

internal static class PumpConfigurationRuleSupport
{
    internal const string ConfigurationName = "Configuration";
    internal static readonly string[] MustBeNestedUnderConfiguration = ["Design", "SystemRequirements"];

    internal static bool AppliesToPump(LiveNode node, ValidationContext context) =>
        CompanionSpecRuleHelpers.TypeDerivesFrom(
            context,
            node.TypeDefinitionId,
            CompanionSpecRuleHelpers.PumpsModelUri,
            "PumpType");

    internal static IReadOnlyList<InstanceDeclaration> GetConfigurationDescendantDeclarations(ValidationContext context)
    {
        if (!CompanionSpecRuleHelpers.TryFindType(
                context,
                CompanionSpecRuleHelpers.PumpsModelUri,
                "ConfigurationGroupType",
                out var configurationGroupType))
        {
            return [];
        }

        return context.GetInstanceDeclarations(configurationGroupType.NodeId)
            .Where(declaration => declaration.NodeClass is NodeClass.Object or NodeClass.Variable)
            .ToArray();
    }
}
