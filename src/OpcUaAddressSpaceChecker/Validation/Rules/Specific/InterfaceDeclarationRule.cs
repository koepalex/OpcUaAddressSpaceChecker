using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation.Rules.Generic;

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
    public string Description => "DI DeviceType instances satisfy mandatory declarations inherited through HasInterface; optional interface members that are not implemented are reported informationally.";

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
        var interfaceDeclarations = GetInterfaceDeclarations(context);
        foreach (var declaration in interfaceDeclarations)
        {
            if (CompanionSpecRuleHelpers.BrowsePathExists(node, declaration.BrowsePath, context, interfaceDeclarations))
            {
                continue;
            }

            // OPC UA HasInterface members keep their declared ModellingRule: an Optional member (or a
            // Mandatory member whose Optional ancestor is not materialized) is a conformant omission,
            // not an error. Only a genuinely Mandatory member with a fully materialized mandatory
            // ancestor chain is a violation.
            if (IsGenuinelyRequired(node, declaration, interfaceDeclarations, context))
            {
                yield return new ValidationFinding(
                    RuleId,
                    Severity.Error,
                    node.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/{CompanionSpecRuleHelpers.FormatBrowsePath(declaration.BrowsePath)}",
                    $"DI DeviceType instance does not satisfy mandatory interface declaration '{declaration.BrowseName.Name}'.",
                    $"Missing mandatory HasInterface-derived declaration path {CompanionSpecRuleHelpers.FormatBrowsePath(declaration.BrowsePath)}.");
            }
            else
            {
                yield return new ValidationFinding(
                    RuleId,
                    Severity.Information,
                    node.NodeId,
                    $"{CompanionSpecRuleHelpers.FormatNode(node)}/{CompanionSpecRuleHelpers.FormatBrowsePath(declaration.BrowsePath)}",
                    $"Optional interface member '{declaration.BrowseName.Name}' is not implemented.",
                    $"Optional HasInterface-derived declaration path {CompanionSpecRuleHelpers.FormatBrowsePath(declaration.BrowsePath)} is not materialized (conformant; implement only if the device exposes this metadata).");
            }
        }
    }

    /// <summary>
    /// A missing interface declaration is a genuine violation only when it is Mandatory (or a
    /// MandatoryPlaceholder) AND every Optional ancestor prefix on its BrowsePath is actually present
    /// on the instance. A Mandatory member reached only through an absent Optional ancestor (e.g. the
    /// FileType members under the Optional <c>DocumentationFiles</c> subtree) is a conformant omission.
    /// </summary>
    private static bool IsGenuinelyRequired(
        LiveNode node,
        InstanceDeclaration declaration,
        IReadOnlyList<InstanceDeclaration> interfaceDeclarations,
        ValidationContext context)
    {
        if (!GenericRuleHelpers.IsMandatory(declaration) &&
            !GenericRuleHelpers.IsMandatoryPlaceholder(declaration))
        {
            return false;
        }

        for (var depth = 1; depth < declaration.BrowsePath.Count; depth++)
        {
            var prefix = declaration.BrowsePath.Take(depth).ToArray();
            var ancestor = interfaceDeclarations.FirstOrDefault(candidate =>
                GenericRuleHelpers.BrowsePathEquals(candidate.BrowsePath, prefix));

            if (ancestor != null &&
                (GenericRuleHelpers.IsOptional(ancestor) || GenericRuleHelpers.IsOptionalPlaceholder(ancestor)) &&
                !CompanionSpecRuleHelpers.BrowsePathExists(node, prefix, context, interfaceDeclarations))
            {
                return false;
            }
        }

        return true;
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
