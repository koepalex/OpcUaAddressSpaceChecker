using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation;

public sealed class ValidationEngine
{
    private readonly RuleRegistry _registry;
    private readonly NodesetModelIndex _typeModel;
    private readonly ILogger<ValidationEngine> _logger;

    public ValidationEngine(
        RuleRegistry registry,
        NodesetModelIndex typeModel,
        ILogger<ValidationEngine> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _typeModel = typeModel ?? throw new ArgumentNullException(nameof(typeModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ValidationReport> RunAsync(
        IEnumerable<LiveNode> liveNodes,
        ISession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(liveNodes);
        ArgumentNullException.ThrowIfNull(session);

        var nodes = liveNodes as IReadOnlyCollection<LiveNode> ?? liveNodes.ToArray();
        var findings = new List<ValidationFinding>();
        var seen = new HashSet<(string RuleId, string NodeId, Severity Severity, string Message, string? Details)>();

        foreach (var liveNode in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var namespaceUri = ResolveNamespaceUri(session, liveNode.NodeId);
            var context = new ValidationContext(_typeModel, session, namespaceUri, _logger);
            var typeDefinition = ResolveTypeDefinition(liveNode);

            foreach (var rule in _registry.GetApplicableRules(liveNode, typeDefinition, context))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var finding in rule.Validate(liveNode, typeDefinition, context))
                {
                    // The same subject node can be reached through more than one traversal path
                    // (e.g. a shared node referenced via multiple hierarchical references), which
                    // would otherwise surface identical findings multiple times. Collapse findings
                    // that are indistinguishable to the reader; genuinely distinct issues differ in
                    // Message/Details and are preserved.
                    if (seen.Add((finding.RuleId, finding.NodeId.ToString(), finding.Severity, finding.Message, finding.Details)))
                    {
                        findings.Add(finding);
                    }
                }
            }
        }

        return Task.FromResult(new ValidationReport(
            nodes.Count,
            findings.Count,
            findings.AsReadOnly()));
    }

    private NodeState? ResolveTypeDefinition(LiveNode liveNode)
    {
        if (NodeId.IsNull(liveNode.TypeDefinitionId))
        {
            return null;
        }

        return _typeModel.TryGetType(liveNode.TypeDefinitionId, out var typeDefinition)
            ? typeDefinition
            : null;
    }

    private static string ResolveNamespaceUri(ISession session, NodeId nodeId) =>
        session.NamespaceUris.GetString(nodeId.NamespaceIndex) ?? string.Empty;
}
