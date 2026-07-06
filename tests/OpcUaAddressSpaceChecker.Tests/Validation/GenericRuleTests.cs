using Opc.Ua;
using OpcUaAddressSpaceChecker.Validation.Rules.Generic;

namespace OpcUaAddressSpaceChecker.Tests.Validation;

public sealed class GenericRuleTests(NodesetTestFixture fixture) : IClassFixture<NodesetTestFixture>
{
    [Fact]
    public void Gen01_reports_missing_mandatory_child_and_accepts_complete_instance()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var di = fixture.NamespaceIndex(NodesetTestFixture.DiModelUri);
        var incomplete = LiveNodeFactory.Object(new QualifiedName("Device", di), deviceType.NodeId);
        var rule = new MissingMandatoryChildRule();

        var incompleteFindings = rule.Validate(incomplete, deviceType, fixture.Context).ToArray();

        Assert.Contains(incompleteFindings, finding =>
            finding.RuleId == "GEN-01" &&
            finding.BrowsePath.Contains("Manufacturer", StringComparison.Ordinal));

        var complete = LiveNodeFactory.Object(new QualifiedName("Device", di), deviceType.NodeId);
        var declarations = fixture.Context.GetInstanceDeclarations(deviceType.NodeId);
        foreach (var declaration in declarations.Where(declaration => declaration.ModellingRuleId == new NodeId(78)))
        {
            LiveNodeFactory.AddDeclarationPath(complete, declaration, declarations, fixture.Model);
        }

        Assert.Empty(rule.Validate(complete, deviceType, fixture.Context));
    }

    [Fact]
    public void Gen02_reports_node_class_mismatch_and_accepts_matching_class()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var manufacturer = fixture.Declaration(deviceType.NodeId, "Manufacturer");
        var di = fixture.NamespaceIndex(NodesetTestFixture.DiModelUri);
        var rule = new NodeClassMismatchRule();

        var invalid = LiveNodeFactory.Object(new QualifiedName("Device", di), deviceType.NodeId);
        LiveNodeFactory.AddChild(
            invalid,
            LiveNodeFactory.Object(manufacturer.BrowseName, manufacturer.TypeDefinitionId),
            manufacturer.ReferenceTypeId);

        var valid = LiveNodeFactory.Object(new QualifiedName("Device", di), deviceType.NodeId);
        LiveNodeFactory.AddChild(
            valid,
            LiveNodeFactory.Variable(manufacturer.BrowseName, manufacturer.TypeDefinitionId),
            manufacturer.ReferenceTypeId);

        Assert.Contains(rule.Validate(invalid, deviceType, fixture.Context), finding => finding.RuleId == "GEN-02");
        Assert.Empty(rule.Validate(valid, deviceType, fixture.Context));
    }

    [Fact]
    public void Gen04_reports_variable_datatype_mismatch_and_accepts_declared_datatype()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var manufacturer = fixture.Declaration(deviceType.NodeId, "Manufacturer");
        var di = fixture.NamespaceIndex(NodesetTestFixture.DiModelUri);
        var rule = new DataTypeValueRankMismatchRule();

        var invalid = LiveNodeFactory.Object(new QualifiedName("Device", di), deviceType.NodeId);
        LiveNodeFactory.AddChild(
            invalid,
            LiveNodeFactory.Variable(
                manufacturer.BrowseName,
                manufacturer.TypeDefinitionId,
                DataTypeIds.String,
                -1),
            manufacturer.ReferenceTypeId);

        var valid = LiveNodeFactory.Object(new QualifiedName("Device", di), deviceType.NodeId);
        LiveNodeFactory.AddChild(
            valid,
            LiveNodeFactory.Variable(
                manufacturer.BrowseName,
                manufacturer.TypeDefinitionId,
                DataTypeIds.LocalizedText,
                -1),
            manufacturer.ReferenceTypeId);

        Assert.Contains(rule.Validate(invalid, deviceType, fixture.Context), finding => finding.RuleId == "GEN-04");
        Assert.Empty(rule.Validate(valid, deviceType, fixture.Context));
    }

    [Fact]
    public void Gen09_reports_direct_compression_ratio_on_pump_and_accepts_nested_path()
    {
        var pumpType = fixture.FindType(NodesetTestFixture.PumpsModelUri, "PumpType");
        var compressionRatio = fixture.Declaration(
            pumpType.NodeId,
            "Configuration",
            "SystemRequirements",
            "CompressionRatio");
        var pumps = fixture.NamespaceIndex(NodesetTestFixture.PumpsModelUri);
        var di = fixture.NamespaceIndex(NodesetTestFixture.DiModelUri);
        var rule = new NestedBrowsePathRule();

        var invalid = LiveNodeFactory.Object(new QualifiedName("Pump", pumps), pumpType.NodeId);
        LiveNodeFactory.AddChild(
            invalid,
            LiveNodeFactory.Variable(compressionRatio.BrowseName, compressionRatio.TypeDefinitionId, DataTypeIds.Double, -1),
            compressionRatio.ReferenceTypeId);

        var valid = LiveNodeFactory.Object(new QualifiedName("Pump", pumps), pumpType.NodeId);
        var configuration = LiveNodeFactory.Object(new QualifiedName("Configuration", di), fixture.NodeId(NodesetTestFixture.PumpsModelUri, 1024));
        var systemRequirements = LiveNodeFactory.Object(new QualifiedName("SystemRequirements", pumps));
        var nestedCompressionRatio = LiveNodeFactory.Variable(
            compressionRatio.BrowseName,
            compressionRatio.TypeDefinitionId,
            DataTypeIds.Double,
            -1);

        LiveNodeFactory.AddChild(valid, configuration, ReferenceTypeIds.HasComponent);
        LiveNodeFactory.AddChild(configuration, systemRequirements, ReferenceTypeIds.HasComponent);
        LiveNodeFactory.AddChild(systemRequirements, nestedCompressionRatio, compressionRatio.ReferenceTypeId);

        Assert.Contains(rule.Validate(invalid, pumpType, fixture.Context), finding => finding.RuleId == "GEN-09");
        Assert.Empty(rule.Validate(valid, pumpType, fixture.Context));
    }
}
