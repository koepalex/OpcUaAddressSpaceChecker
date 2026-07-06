using Opc.Ua;

namespace OpcUaAddressSpaceChecker.Tests.NodeModel;

public sealed class InstanceDeclarationTests(NodesetTestFixture fixture) : IClassFixture<NodesetTestFixture>
{
    [Fact]
    public void PumpType_contains_nested_configuration_system_requirements_compression_ratio()
    {
        var pumpType = fixture.FindType(NodesetTestFixture.PumpsModelUri, "PumpType");

        var declaration = fixture.Declaration(
            pumpType.NodeId,
            "Configuration",
            "SystemRequirements",
            "CompressionRatio");

        Assert.Equal(
            ["Configuration", "SystemRequirements", "CompressionRatio"],
            declaration.BrowsePath.Select(segment => segment.Name).ToArray());
        Assert.Equal(
            [
                NodesetTestFixture.DiModelUri,
                NodesetTestFixture.PumpsModelUri,
                NodesetTestFixture.PumpsModelUri
            ],
            declaration.BrowsePath
                .Select(segment => fixture.Model.NamespaceMap[segment.NamespaceIndex])
                .ToArray());
    }

    [Fact]
    public void DeviceType_has_expected_mandatory_properties()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var declarations = fixture.Model.GetInstanceDeclarations(deviceType.NodeId);
        var mandatoryProperties = new[]
        {
            "Manufacturer",
            "Model",
            "HardwareRevision",
            "SoftwareRevision",
            "DeviceRevision",
            "DeviceManual",
            "SerialNumber",
            "RevisionCounter"
        };

        foreach (var propertyName in mandatoryProperties)
        {
            var declaration = declarations.Single(candidate =>
                candidate.BrowsePath.Count == 1 &&
                candidate.BrowseName.Name == propertyName);

            Assert.Equal(NodeClass.Variable, declaration.NodeClass);
            Assert.Equal(ReferenceTypeIds.HasProperty, declaration.ReferenceTypeId);
            Assert.Equal(new NodeId(78), declaration.ModellingRuleId);
        }
    }

    [Fact]
    public void DeviceType_includes_HasInterface_derived_declarations_as_mandatory()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var declarations = fixture.Model.GetInstanceDeclarations(deviceType.NodeId);

        var deviceTypeImage = declarations.Single(declaration =>
            declaration.BrowsePath.Count == 1 &&
            declaration.BrowseName.Name == "DeviceTypeImage");

        Assert.Equal(NodeClass.Object, deviceTypeImage.NodeClass);
        Assert.Equal(new NodeId(78), deviceTypeImage.ModellingRuleId);
        Assert.Equal(fixture.NodeId(NodesetTestFixture.DiModelUri, 15055), deviceTypeImage.NodeId);
    }

    [Fact]
    public void DeviceType_tightens_ComponentType_optional_nameplate_properties_to_mandatory()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");

        var manufacturer = fixture.Model.GetInstanceDeclarations(deviceType.NodeId)
            .Single(declaration =>
                declaration.BrowsePath.Count == 1 &&
                declaration.BrowseName.Name == "Manufacturer");

        Assert.Equal(new NodeId(78), manufacturer.ModellingRuleId);
        Assert.Equal(fixture.NodeId(NodesetTestFixture.DiModelUri, 6003), manufacturer.NodeId);
    }
}
