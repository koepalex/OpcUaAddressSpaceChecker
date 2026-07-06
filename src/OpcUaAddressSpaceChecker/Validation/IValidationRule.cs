using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation;

public interface IValidationRule
{
    string RuleId { get; }
    string Category { get; }
    Severity Severity { get; }
    string Description { get; }
    bool Applies(LiveNode node, NodeState? typeDefinition, ValidationContext context);
    IEnumerable<ValidationFinding> Validate(LiveNode node, NodeState? typeDefinition, ValidationContext context);
}
