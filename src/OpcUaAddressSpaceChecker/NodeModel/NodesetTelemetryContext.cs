using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;

namespace OpcUaAddressSpaceChecker.NodeModel;

internal sealed class NodesetTelemetryContext : ITelemetryContext
{
    private readonly ActivitySource _activitySource = new("OpcUaAddressSpaceChecker.NodeModel");

    public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

    public ActivitySource ActivitySource => _activitySource;

    public Meter CreateMeter() => new("OpcUaAddressSpaceChecker.NodeModel");
}
