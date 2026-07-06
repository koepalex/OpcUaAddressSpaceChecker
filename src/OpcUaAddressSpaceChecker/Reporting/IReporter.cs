using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Reporting;

public interface IReporter
{
    void Report(ValidationReport report, TextWriter writer);
}
