using System.Reflection;
using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation;

public sealed class RuleRegistry
{
    private readonly List<IValidationRule> _rules = [];

    public RuleRegistry()
    {
        IncludeRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ExcludeRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public RuleRegistry(IEnumerable<string>? includeRuleIds, IEnumerable<string>? excludeRuleIds)
    {
        IncludeRuleIds = ToRuleIdSet(includeRuleIds);
        ExcludeRuleIds = ToRuleIdSet(excludeRuleIds);
    }

    public ISet<string> IncludeRuleIds { get; }
    public ISet<string> ExcludeRuleIds { get; }
    public IReadOnlyList<IValidationRule> Rules => _rules;

    public void Register(IValidationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.RuleId))
        {
            throw new ArgumentException("RuleId must be non-empty.", nameof(rule));
        }

        if (_rules.Any(registered => string.Equals(registered.RuleId, rule.RuleId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A validation rule with ID '{rule.RuleId}' is already registered.");
        }

        _rules.Add(rule);
    }

    public void RegisterAll(IEnumerable<IValidationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules)
        {
            Register(rule);
        }
    }

    public void AutoDiscover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes()
                     .Where(type => type is { IsAbstract: false, IsInterface: false } &&
                                    type.IsAssignableTo(typeof(IValidationRule)))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            Register((IValidationRule)Activator.CreateInstance(type)!);
        }
    }

    public IReadOnlyList<IValidationRule> GetApplicableRules(
        LiveNode node,
        NodeState? typeDefinition,
        ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(context);

        var applicable = _rules
            .Where(IsSelected)
            .Where(rule => rule.Applies(node, typeDefinition, context))
            .ToArray();
        var exclusive = applicable.OfType<IExclusiveValidationRule>().Cast<IValidationRule>().ToArray();

        return exclusive.Length > 0 ? exclusive : applicable;
    }

    private bool IsSelected(IValidationRule rule)
    {
        if (IncludeRuleIds.Count > 0 && !IncludeRuleIds.Contains(rule.RuleId))
        {
            return false;
        }

        return !ExcludeRuleIds.Contains(rule.RuleId);
    }

    private static ISet<string> ToRuleIdSet(IEnumerable<string>? ruleIds) =>
        new HashSet<string>(
            ruleIds?.Where(ruleId => !string.IsNullOrWhiteSpace(ruleId)) ?? [],
            StringComparer.OrdinalIgnoreCase);
}
