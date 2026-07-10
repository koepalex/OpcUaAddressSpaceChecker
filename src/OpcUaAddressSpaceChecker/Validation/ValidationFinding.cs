using Opc.Ua;

namespace OpcUaAddressSpaceChecker.Validation;

public sealed record ValidationFinding(
    string RuleId,
    Severity Severity,
    NodeId NodeId,
    string BrowsePath,
    string Message,
    string? Details = null,
    string? DeclaringTypeNamespaceUri = null,
    string? DeclaringTypeReferenceUrl = null);
