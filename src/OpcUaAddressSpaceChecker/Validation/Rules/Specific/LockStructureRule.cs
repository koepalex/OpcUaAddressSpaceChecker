using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Specific;

public sealed class LockStructureRule : IValidationRule
{
    private static readonly string[] MandatoryProperties =
    [
        "Locked",
        "LockingClient",
        "LockingUser",
        "RemainingLockTime"
    ];

    private static readonly string[] MandatoryMethods =
    [
        "InitLock",
        "RenewLock",
        "ExitLock",
        "BreakLock"
    ];

    public string RuleId => "DI-04";
    public string Category => "DI";
    public Severity Severity => Severity.Error;
    public string Description => "Present DI Lock objects expose all mandatory lock state properties and methods.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        CompanionSpecRuleHelpers.TypeDerivesFrom(
            context,
            node.TypeDefinitionId,
            CompanionSpecRuleHelpers.DiModelUri,
            "TopologyElementType");

    public IEnumerable<ValidationFinding> Validate(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        foreach (var lockLink in CompanionSpecRuleHelpers.FindDirectChildren(
                     node,
                     "Lock",
                     context,
                     CompanionSpecRuleHelpers.DiModelUri))
        {
            var lockChildLinks = CompanionSpecRuleHelpers.GetChildLinks(lockLink.Child);

            foreach (var propertyName in MandatoryProperties)
            {
                var found = lockChildLinks.Any(link =>
                    CompanionSpecRuleHelpers.BrowseNameMatches(
                        link.Child.BrowseName,
                        propertyName,
                        context,
                        CompanionSpecRuleHelpers.DiModelUri) &&
                    CompanionSpecRuleHelpers.IsReferenceTypeCompatible(
                        context,
                        link.Reference.ReferenceTypeId,
                        CompanionSpecRuleHelpers.HasProperty));

                if (!found)
                {
                    yield return MissingLockMember(node, lockLink.Child, propertyName, "property");
                }
            }

            foreach (var methodName in MandatoryMethods)
            {
                var found = lockChildLinks.Any(link =>
                    link.Child.NodeClass == NodeClass.Method &&
                    CompanionSpecRuleHelpers.BrowseNameMatches(
                        link.Child.BrowseName,
                        methodName,
                        context,
                        CompanionSpecRuleHelpers.DiModelUri) &&
                    CompanionSpecRuleHelpers.IsReferenceTypeCompatible(
                        context,
                        link.Reference.ReferenceTypeId,
                        CompanionSpecRuleHelpers.HasComponent));

                if (!found)
                {
                    yield return MissingLockMember(node, lockLink.Child, methodName, "method");
                }
            }
        }
    }

    private ValidationFinding MissingLockMember(LiveNode parent, LiveNode lockNode, string memberName, string memberKind) =>
        new(
            RuleId,
            Severity.Error,
            lockNode.NodeId,
            $"{CompanionSpecRuleHelpers.FormatNode(parent)}/Lock/{memberName}",
            $"DI Lock object is missing mandatory {memberKind} '{memberName}'.",
            "The optional Lock object follows the DI LockingServicesType structure when present.");
}
