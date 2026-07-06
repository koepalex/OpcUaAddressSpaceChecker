using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Specific;

public sealed class InterfaceDeclarationRule : IValidationRule
{
    private static readonly string[] DeviceInterfaceTypeNames =
    [
        "IVendorNameplateType",
        "ITagNameplateType",
        "ISupportInfoType",
        "IDeviceHealthType"
    ];

    public string RuleId => "DI-05";
    public string Category => "DI";
    public Severity Severity => Severity.Error;
    public string Description => "DI DeviceType instances satisfy mandatory declarations inherited through HasInterface.";

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
        foreach (var declaration in GetInterfaceDeclarations(context))
        {
            if (!CompanionSpecRuleHelpers.BrowsePathExists(node, declaration.BrowsePath, context))
            {
                yield return new ValidationFinding(
                    RuleId,
                    Severity.Error,
                    node.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/{CompanionSpecRuleHelpers.FormatBrowsePath(declaration.BrowsePath)}",
                    $"DI DeviceType instance does not satisfy interface declaration '{declaration.BrowseName.Name}'.",
                    $"Missing HasInterface-derived declaration path {CompanionSpecRuleHelpers.FormatBrowsePath(declaration.BrowsePath)}.");
            }
        }
    }

    private static IReadOnlyList<InstanceDeclaration> GetInterfaceDeclarations(ValidationContext context)
    {
        var declarations = new Dictionary<string, InstanceDeclaration>(StringComparer.Ordinal);

        foreach (var interfaceTypeName in DeviceInterfaceTypeNames)
        {
            if (!CompanionSpecRuleHelpers.TryFindType(
                    context,
                    CompanionSpecRuleHelpers.DiModelUri,
                    interfaceTypeName,
                    out var interfaceType))
            {
                continue;
            }

            foreach (var declaration in context.GetInstanceDeclarations(interfaceType.NodeId))
            {
                if (declaration.BrowsePath.Count == 0)
                {
                    continue;
                }

                declarations.TryAdd(CompanionSpecRuleHelpers.FormatBrowsePath(declaration.BrowsePath), declaration);
            }
        }

        return declarations.Values.ToArray();
    }
}
