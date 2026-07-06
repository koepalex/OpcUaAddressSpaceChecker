using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class SubtypeModellingRuleConsistencyRule : IValidationRule
{
    public string RuleId => "GEN-13";
    public string Category => "Generic";
    public Severity Severity => Severity.Warning;
    public string Description => "Subtype InstanceDeclarations must not loosen ModellingRules inherited from supertypes.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        typeDefinition != null && context.TypeModel.GetSupertypeChain(typeDefinition.NodeId).Count > 1;

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        if (typeDefinition == null)
        {
            yield break;
        }

        var inheritedRules = new Dictionary<string, InstanceDeclaration>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var typeNode in context.TypeModel.GetSupertypeChain(typeDefinition.NodeId))
        {
            foreach (var declaration in CollectOwnDeclarations(typeNode))
            {
                var key = GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath);
                if (inheritedRules.TryGetValue(key, out var inherited) &&
                    IsRelaxingOverride(inherited.ModellingRuleId, declaration.ModellingRuleId) &&
                    reported.Add($"{typeNode.NodeId}|{key}"))
                {
                    yield return new ValidationFinding(
                        RuleId,
                        Severity,
                        node.NodeId,
                        key,
                        "Subtype declaration loosens an inherited ModellingRule.",
                        $"Inherited {GenericRuleHelpers.FormatNodeId(inherited.ModellingRuleId)}; subtype {GenericRuleHelpers.FormatNodeId(declaration.ModellingRuleId)} on type {GenericRuleHelpers.FormatNodeId(typeNode.NodeId)}.");
                    continue;
                }

                inheritedRules[key] = declaration;
            }
        }
    }

    private static IEnumerable<InstanceDeclaration> CollectOwnDeclarations(NodeState typeNode)
    {
        var result = new List<InstanceDeclaration>();
        CollectOwnDeclarations(typeNode, [], result, []);
        return result;
    }

    private static void CollectOwnDeclarations(
        NodeState parent,
        IReadOnlyList<QualifiedName> parentBrowsePath,
        List<InstanceDeclaration> result,
        HashSet<NodeId> activeNodes)
    {
        if (NodeId.IsNull(parent.NodeId) || !activeNodes.Add(parent.NodeId))
        {
            return;
        }

        try
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(null, children);

            foreach (var child in children)
            {
                if (NodeId.IsNull(child.ModellingRuleId))
                {
                    continue;
                }

                var browsePath = parentBrowsePath.Concat([child.BrowseName]).ToArray();
                result.Add(new InstanceDeclaration(
                    child.NodeId,
                    browsePath,
                    child.BrowseName,
                    child.NodeClass,
                    NodeId.IsNull(child.TypeDefinitionId) ? null : child.TypeDefinitionId,
                    child.ModellingRuleId,
                    child.ReferenceTypeId));

                CollectOwnDeclarations(child, browsePath, result, activeNodes);
            }
        }
        finally
        {
            activeNodes.Remove(parent.NodeId);
        }
    }

    private static bool IsRelaxingOverride(NodeId inheritedModellingRuleId, NodeId subtypeModellingRuleId) =>
        inheritedModellingRuleId == GenericRuleHelpers.ModellingRuleMandatory &&
        subtypeModellingRuleId == GenericRuleHelpers.ModellingRuleOptional ||
        inheritedModellingRuleId == GenericRuleHelpers.ModellingRuleMandatoryPlaceholder &&
        subtypeModellingRuleId == GenericRuleHelpers.ModellingRuleOptionalPlaceholder;
}
