using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation.Rules.Specific;

namespace OpcUaAddressSpaceChecker.Tests.Validation;

public sealed class SpecificRuleTests(NodesetTestFixture fixture) : IClassFixture<NodesetTestFixture>
{
    private static readonly string[] MandatoryDeviceProperties =
    [
        "Manufacturer",
        "Model",
        "HardwareRevision",
        "SoftwareRevision",
        "DeviceRevision",
        "DeviceManual",
        "SerialNumber",
        "RevisionCounter"
    ];

    [Fact]
    public void Di01_reports_missing_mandatory_DeviceType_property_and_accepts_all_properties()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var di = fixture.NamespaceIndex(NodesetTestFixture.DiModelUri);
        var rule = new DeviceTypeMandatoryPropertiesRule();

        var invalid = DeviceWithProperties(deviceType.NodeId, di, MandatoryDeviceProperties.Except(["SerialNumber"]));
        var valid = DeviceWithProperties(deviceType.NodeId, di, MandatoryDeviceProperties);

        Assert.Contains(rule.Validate(invalid, deviceType, fixture.Context), finding =>
            finding.RuleId == "DI-01" &&
            finding.BrowsePath.Contains("SerialNumber", StringComparison.Ordinal));
        Assert.Empty(rule.Validate(valid, deviceType, fixture.Context));
    }

    [Fact]
    public void Pumps02_reports_ConfigurationGroupType_descendant_exposed_directly()
    {
        var pumpType = fixture.FindType(NodesetTestFixture.PumpsModelUri, "PumpType");
        var pumps = fixture.NamespaceIndex(NodesetTestFixture.PumpsModelUri);
        var di = fixture.NamespaceIndex(NodesetTestFixture.DiModelUri);
        var rule = new PumpConfigurationDescendantNestingRule();

        var invalid = LiveNodeFactory.Object(new QualifiedName("Pump", pumps), pumpType.NodeId);
        LiveNodeFactory.AddChild(
            invalid,
            LiveNodeFactory.Object(new QualifiedName("Implementation", pumps)),
            ReferenceTypeIds.HasComponent);

        var valid = LiveNodeFactory.Object(new QualifiedName("Pump", pumps), pumpType.NodeId);
        var configuration = LiveNodeFactory.Object(new QualifiedName("Configuration", di));
        LiveNodeFactory.AddChild(valid, configuration, ReferenceTypeIds.HasComponent);
        LiveNodeFactory.AddChild(
            configuration,
            LiveNodeFactory.Object(new QualifiedName("Implementation", pumps)),
            ReferenceTypeIds.HasComponent);

        Assert.Contains(rule.Validate(invalid, pumpType, fixture.Context), finding => finding.RuleId == "PUMPS-02");
        Assert.Empty(rule.Validate(valid, pumpType, fixture.Context));
    }

    [Fact]
    public void Pumps03_reports_Design_or_SystemRequirements_exposed_directly()
    {
        var pumpType = fixture.FindType(NodesetTestFixture.PumpsModelUri, "PumpType");
        var pumps = fixture.NamespaceIndex(NodesetTestFixture.PumpsModelUri);
        var di = fixture.NamespaceIndex(NodesetTestFixture.DiModelUri);
        var rule = new PumpDesignSystemRequirementsNestingRule();

        var invalid = LiveNodeFactory.Object(new QualifiedName("Pump", pumps), pumpType.NodeId);
        LiveNodeFactory.AddChild(
            invalid,
            LiveNodeFactory.Object(new QualifiedName("Design", pumps)),
            ReferenceTypeIds.HasComponent);
        LiveNodeFactory.AddChild(
            invalid,
            LiveNodeFactory.Object(new QualifiedName("SystemRequirements", pumps)),
            ReferenceTypeIds.HasComponent);

        var valid = LiveNodeFactory.Object(new QualifiedName("Pump", pumps), pumpType.NodeId);
        var configuration = LiveNodeFactory.Object(new QualifiedName("Configuration", di));
        LiveNodeFactory.AddChild(valid, configuration, ReferenceTypeIds.HasComponent);
        LiveNodeFactory.AddChild(configuration, LiveNodeFactory.Object(new QualifiedName("Design", pumps)), ReferenceTypeIds.HasComponent);
        LiveNodeFactory.AddChild(configuration, LiveNodeFactory.Object(new QualifiedName("SystemRequirements", pumps)), ReferenceTypeIds.HasComponent);

        var findings = rule.Validate(invalid, pumpType, fixture.Context).ToArray();
        Assert.Contains(findings, finding => finding.RuleId == "PUMPS-03" && finding.BrowsePath.Contains("Design", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.RuleId == "PUMPS-03" && finding.BrowsePath.Contains("SystemRequirements", StringComparison.Ordinal));
        Assert.Empty(rule.Validate(valid, pumpType, fixture.Context));
    }

    private static LiveNode DeviceWithProperties(
        NodeId deviceTypeId,
        ushort diNamespaceIndex,
        IEnumerable<string> propertyNames)
    {
        var device = LiveNodeFactory.Object(new QualifiedName("Device", diNamespaceIndex), deviceTypeId);
        foreach (var propertyName in propertyNames)
        {
            LiveNodeFactory.AddChild(
                device,
                LiveNodeFactory.Variable(
                    new QualifiedName(propertyName, diNamespaceIndex),
                    ReferenceTypeIds.HasProperty,
                    DataTypeIds.String,
                    -1),
                ReferenceTypeIds.HasProperty);
        }

        return device;
    }
}
