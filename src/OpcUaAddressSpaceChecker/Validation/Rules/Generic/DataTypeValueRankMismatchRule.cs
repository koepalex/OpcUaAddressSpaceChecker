using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation.Rules.Generic;

public sealed class DataTypeValueRankMismatchRule : IValidationRule
{
    public string RuleId => "GEN-04";
    public string Category => "Generic";
    public Severity Severity => Severity.Error;
    public string Description => "Variable instances must match declared DataType, ValueRank, and ArrayDimensions constraints.";

    public bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context) =>
        GenericRuleHelpers.HasUsableType(node, context);

    public IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context)
    {
        var declarations = context.GetInstanceDeclarations(node);
        foreach (var declaration in GenericRuleHelpers.ConcreteDeclarations(context, node)
                     .Where(declaration => declaration.NodeClass == NodeClass.Variable))
        {
            if (!GenericRuleHelpers.TryGetDeclarationVariable(context, declaration, out var declarationVariable))
            {
                continue;
            }

            var declaringRef = GenericRuleHelpers.ResolveDeclaringTypeReference(context, node, declaration, declarations);
            foreach (var match in GenericRuleHelpers.FindChildrenByBrowsePath(context, node, declarations, declaration.BrowsePath)
                         .Where(match => match.Child.NodeClass == NodeClass.Variable))
            {
                foreach (var finding in ValidateVariable(match.Child, declaration, declarationVariable, context, declaringRef))
                {
                    yield return finding;
                }
            }
        }
    }

    private IEnumerable<ValidationFinding> ValidateVariable(
        LiveNode child,
        InstanceDeclaration declaration,
        BaseVariableState declarationVariable,
        ValidationContext context,
        GenericRuleHelpers.DeclaringTypeReference declaringRef)
    {
        if (!NodeId.IsNull(declarationVariable.DataType) &&
            !GenericRuleHelpers.IsTypeCompatible(context, child.DataType, declarationVariable.DataType))
        {
            yield return new ValidationFinding(
                RuleId,
                Severity,
                child.NodeId,
                GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                "Variable DataType is not compatible with its InstanceDeclaration.",
                $"Expected {GenericRuleHelpers.FormatNode(context, declarationVariable.DataType)} or a subtype; actual {GenericRuleHelpers.FormatNode(context, child.DataType)}.",
                declaringRef.NamespaceUri,
                declaringRef.ReferenceUrl);
        }

        if (child.ValueRank is not int actualValueRank || !IsValueRankCompatible(actualValueRank, declarationVariable.ValueRank))
        {
            yield return new ValidationFinding(
                RuleId,
                Severity,
                child.NodeId,
                GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                "Variable ValueRank is not compatible with its InstanceDeclaration.",
                $"Expected ValueRank {declarationVariable.ValueRank}; actual {(child.ValueRank.HasValue ? child.ValueRank.Value.ToString() : "<missing>")}.",
                declaringRef.NamespaceUri,
                declaringRef.ReferenceUrl);
        }

        uint[] declaredDimensions = declarationVariable.ArrayDimensions?.ToArray() ?? [];
        if (declaredDimensions.Length > 0 && !AreArrayDimensionsCompatible(child.ArrayDimensions, declaredDimensions))
        {
            yield return new ValidationFinding(
                RuleId,
                Severity,
                child.NodeId,
                GenericRuleHelpers.FormatBrowsePath(declaration.BrowsePath),
                "Variable ArrayDimensions are not compatible with its InstanceDeclaration.",
                $"Expected [{string.Join(", ", declaredDimensions)}]; actual [{string.Join(", ", child.ArrayDimensions)}].",
                declaringRef.NamespaceUri,
                declaringRef.ReferenceUrl);
        }
    }

    private static bool IsValueRankCompatible(int actual, int declared) =>
        declared switch
        {
            -2 => true,
            -3 => actual is -1 or 1,
            -1 => actual == -1,
            0 => actual >= 1,
            1 => actual == 1,
            _ when declared > 1 => actual == declared,
            _ => actual == declared
        };

    private static bool AreArrayDimensionsCompatible(IReadOnlyList<uint> actual, IReadOnlyList<uint> declared)
    {
        if (actual.Count != declared.Count)
        {
            return false;
        }

        for (var i = 0; i < declared.Count; i++)
        {
            if (declared[i] != 0 && actual[i] > declared[i])
            {
                return false;
            }
        }

        return true;
    }
}
