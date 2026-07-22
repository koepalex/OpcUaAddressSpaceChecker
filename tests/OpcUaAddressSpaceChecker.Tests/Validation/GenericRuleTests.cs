using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation;
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

    [Fact]
    public void Gen05_reports_legal_instance_extensions_as_information()
    {
        var serverCapabilitiesType = fixture.FindType(NodesetTestFixture.UaModelUri, "ServerCapabilitiesType");
        var root = LiveNodeFactory.Object(
            new QualifiedName("ServerCapabilities", 0),
            serverCapabilitiesType.NodeId);
        var diExtension = LiveNodeFactory.Variable(
            new QualifiedName("MaxInactiveLockTime", fixture.NamespaceIndex(NodesetTestFixture.DiModelUri)),
            VariableTypeIds.BaseDataVariableType,
            DataTypeIds.Duration,
            -1);
        var applicationExtension = LiveNodeFactory.Variable(
            new QualifiedName("VendorProperty", 42),
            VariableTypeIds.PropertyType,
            DataTypeIds.String,
            -1,
            nodeNamespaceIndex: 42);
        LiveNodeFactory.AddChild(root, diExtension, ReferenceTypeIds.HasProperty);
        LiveNodeFactory.AddChild(root, applicationExtension, ReferenceTypeIds.HasProperty);

        var findings = new UnexpectedChildRule().Validate(root, serverCapabilitiesType, fixture.Context).ToArray();

        Assert.Equal(2, findings.Length);
        Assert.All(findings, finding => Assert.Equal(Severity.Information, finding.Severity));
        Assert.Contains(findings, finding => finding.NodeId == diExtension.NodeId);
        Assert.Contains(findings, finding => finding.NodeId == applicationExtension.NodeId);
        Assert.All(findings, finding =>
            Assert.Contains("Additional instance References are permitted", finding.Details, StringComparison.Ordinal));
    }

    [Fact]
    public void Gen05_keeps_misplaced_nested_child_advisory_while_gen09_confirms_structure_error()
    {
        var pumpType = fixture.FindType(NodesetTestFixture.PumpsModelUri, "PumpType");
        var declaration = fixture.Declaration(
            pumpType.NodeId,
            "Configuration",
            "SystemRequirements",
            "CompressionRatio");
        var root = LiveNodeFactory.Object(
            new QualifiedName("Pump", fixture.NamespaceIndex(NodesetTestFixture.PumpsModelUri)),
            pumpType.NodeId);
        var misplaced = LiveNodeFactory.Variable(
            declaration.BrowseName,
            declaration.TypeDefinitionId,
            DataTypeIds.Double,
            -1);
        LiveNodeFactory.AddChild(root, misplaced, declaration.ReferenceTypeId);

        var gen05 = Assert.Single(new UnexpectedChildRule().Validate(root, pumpType, fixture.Context));
        var gen09 = Assert.Single(new NestedBrowsePathRule().Validate(root, pumpType, fixture.Context));

        Assert.Equal(Severity.Information, gen05.Severity);
        Assert.Equal(FindingConfidence.Confirmed, gen09.Confidence);
    }

    [Fact]
    public void Gen07_assigns_alias_children_across_sibling_placeholders()
    {
        var type = fixture.FindType(NodesetTestFixture.UaModelUri, "AliasNameCategoryType");
        var aliasPlaceholder = fixture.Declaration(type.NodeId, "<Alias>");
        var subcategoryPlaceholder = fixture.Declaration(type.NodeId, "<SubAliasNameCategories>");
        var root = LiveNodeFactory.Object(new QualifiedName("Aliases", 0), type.NodeId);
        var alias = LiveNodeFactory.Object(new QualifiedName("MotorAlias", 2), aliasPlaceholder.TypeDefinitionId);
        var subcategory = LiveNodeFactory.Object(new QualifiedName("Motors", 2), subcategoryPlaceholder.TypeDefinitionId);
        LiveNodeFactory.AddChild(root, alias, aliasPlaceholder.ReferenceTypeId);
        LiveNodeFactory.AddChild(root, subcategory, subcategoryPlaceholder.ReferenceTypeId);

        var findings = new OptionalPlaceholderConformanceRule().Validate(root, type, fixture.Context);

        Assert.Empty(findings);
    }

    [Fact]
    public void Gen07_emits_one_finding_for_one_plausible_incompatible_placeholder()
    {
        var type = fixture.FindType(NodesetTestFixture.UaModelUri, "AliasNameCategoryType");
        var aliasPlaceholder = fixture.Declaration(type.NodeId, "<Alias>");
        var root = LiveNodeFactory.Object(new QualifiedName("Aliases", 0), type.NodeId);
        var alias = LiveNodeFactory.Object(new QualifiedName("MotorAlias", 2), aliasPlaceholder.TypeDefinitionId);
        LiveNodeFactory.AddChild(root, alias, ReferenceTypeIds.HasComponent);

        var finding = Assert.Single(new OptionalPlaceholderConformanceRule().Validate(root, type, fixture.Context));

        Assert.Equal("GEN-07", finding.RuleId);
        Assert.Contains("reference type", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gen07_does_not_apply_variable_placeholder_to_object_extension()
    {
        var type = fixture.FindType(NodesetTestFixture.UaModelUri, "ServerCapabilitiesType");
        var vendorCapability = fixture.Declaration(type.NodeId, "<VendorCapability>");
        Assert.Equal(NodeClass.Variable, vendorCapability.NodeClass);
        var root = LiveNodeFactory.Object(new QualifiedName("ServerCapabilities", 0), type.NodeId);
        LiveNodeFactory.AddChild(
            root,
            LiveNodeFactory.Object(new QualifiedName("VendorObject", 2), ObjectTypeIds.BaseObjectType),
            vendorCapability.ReferenceTypeId);

        var findings = new OptionalPlaceholderConformanceRule().Validate(root, type, fixture.Context);

        Assert.Empty(findings);
    }

    [Fact]
    public void Gen07_does_not_assign_unrelated_object_extension_by_reference_type_alone()
    {
        var deviceType = fixture.FindType(NodesetTestFixture.DiModelUri, "DeviceType");
        var softwareUpdateType = fixture.FindType(NodesetTestFixture.DiModelUri, "SoftwareUpdateType");
        var root = LiveNodeFactory.Object(
            new QualifiedName("Device", fixture.NamespaceIndex(NodesetTestFixture.DiModelUri)),
            deviceType.NodeId);
        LiveNodeFactory.AddChild(
            root,
            LiveNodeFactory.Object(
                new QualifiedName("SoftwareUpdate", fixture.NamespaceIndex(NodesetTestFixture.DiModelUri)),
                softwareUpdateType.NodeId),
            ReferenceTypeIds.HasComponent);

        var findings = new OptionalPlaceholderConformanceRule().Validate(root, deviceType, fixture.Context);

        Assert.Empty(findings);
    }

    [Fact]
    public void Gen07_leaves_unrelated_extension_to_gen05()
    {
        var type = fixture.FindType(NodesetTestFixture.UaModelUri, "AliasNameCategoryType");
        var root = LiveNodeFactory.Object(new QualifiedName("Aliases", 0), type.NodeId);
        var extension = LiveNodeFactory.Object(
            new QualifiedName("VendorObject", 2),
            ObjectTypeIds.BaseObjectType);
        LiveNodeFactory.AddChild(root, extension, ReferenceTypeIds.HasProperty);

        Assert.Empty(new OptionalPlaceholderConformanceRule().Validate(root, type, fixture.Context));
        Assert.Single(new UnexpectedChildRule().Validate(root, type, fixture.Context));
    }

    [Fact]
    public void Gen08_accepts_HasProperty_properties_on_a_FunctionalGroupType_subtype_instance()
    {
        // Regression for the removed DI-06 rule, which wrongly flagged HasProperty children of
        // FunctionalGroupType subtypes (e.g. MachineIdentificationType). GEN-08 must accept them:
        // DI spec OPC 10000-100 §4.4.1 permits subtype-declared Properties referenced via HasProperty.
        var identificationType = fixture.FindType(NodesetTestFixture.MachineryModelUri, "MachineIdentificationType");
        var machinery = fixture.NamespaceIndex(NodesetTestFixture.MachineryModelUri);
        var rule = new ReferenceTypeConformanceRule();

        var instance = LiveNodeFactory.Object(new QualifiedName("Identification", machinery), identificationType.NodeId);
        foreach (var propertyName in new[] { "Manufacturer", "SerialNumber", "ProductInstanceUri" })
        {
            var declaration = fixture.Declaration(identificationType.NodeId, propertyName);
            Assert.Equal(GenericRuleHelpers.HasProperty, declaration.ReferenceTypeId);
            LiveNodeFactory.AddChild(
                instance,
                LiveNodeFactory.Variable(declaration.BrowseName, declaration.TypeDefinitionId, DataTypeIds.String, -1),
                declaration.ReferenceTypeId);
        }

        Assert.Empty(rule.Validate(instance, identificationType, fixture.Context));
    }

    [Fact]
    public void Gen10_skips_core_namespace_object_without_type_definition()
    {
        var rule = new HasTypeDefinitionPresenceRule();
        var objectsFolder = new LiveNode
        {
            NodeId = ObjectIds.ObjectsFolder,
            BrowseName = new QualifiedName("Objects", 0),
            DisplayName = "Objects",
            NodeClass = NodeClass.Object
        };

        Assert.False(rule.Applies(objectsFolder, null, fixture.Context));
    }

    [Fact]
    public void Gen10_reports_custom_namespace_object_without_type_definition()
    {
        var di = fixture.NamespaceIndex(NodesetTestFixture.DiModelUri);
        var rule = new HasTypeDefinitionPresenceRule();
        var customObject = LiveNodeFactory.Object(new QualifiedName("Device", di));

        Assert.True(rule.Applies(customObject, null, fixture.Context));
        Assert.Contains(rule.Validate(customObject, null, fixture.Context), finding => finding.RuleId == "GEN-10");
    }

    [Fact]
    public void Gen01_optional_placeholder_fulfilled_instance_produces_no_finding()
    {
        var type = fixture.FindType(NodesetTestFixture.IaModelUri, "BasicStacklightType");
        var declarations = fixture.Context.GetInstanceDeclarations(type.NodeId);
        var placeholder = fixture.Declaration(type.NodeId, "<OrderedObject>");
        var mandatoryDescendants = MandatoryDescendants(declarations, placeholder);
        var ia = fixture.NamespaceIndex(NodesetTestFixture.IaModelUri);
        var rule = new MissingMandatoryChildRule();

        var root = LiveNodeFactory.Object(new QualifiedName("Stacklight", ia), type.NodeId);
        var instance = AddFulfillingPlaceholderInstance(root, "Element0", placeholder, mandatoryDescendants);

        var findings = rule.Validate(root, type, fixture.Context).ToArray();

        Assert.NotEmpty(mandatoryDescendants);
        Assert.DoesNotContain(findings, finding => finding.BrowsePath.Contains('<'));
        Assert.DoesNotContain(findings, finding => finding.NodeId == instance.NodeId);
    }

    [Fact]
    public void Gen01_mandatory_placeholder_fulfilled_instance_produces_no_finding()
    {
        var type = fixture.FindType(NodesetTestFixture.MachineryModelUri, "MachineryLifetimeCounterType");
        var declarations = fixture.Context.GetInstanceDeclarations(type.NodeId);
        var placeholder = fixture.Declaration(type.NodeId, "<LifetimeVariable>");
        var mandatoryDescendants = MandatoryDescendants(declarations, placeholder);
        var machinery = fixture.NamespaceIndex(NodesetTestFixture.MachineryModelUri);
        var rule = new MissingMandatoryChildRule();

        var root = LiveNodeFactory.Object(new QualifiedName("Counter", machinery), type.NodeId);
        var instance = AddFulfillingPlaceholderInstance(root, "OperatingHours", placeholder, mandatoryDescendants);

        var findings = rule.Validate(root, type, fixture.Context).ToArray();

        Assert.NotEmpty(mandatoryDescendants);
        Assert.DoesNotContain(findings, finding => finding.BrowsePath.Contains('<'));
        Assert.DoesNotContain(findings, finding => finding.NodeId == instance.NodeId);
    }

    [Fact]
    public void Gen01_suppresses_descendants_when_required_placeholder_ancestor_is_missing()
    {
        var type = fixture.FindType(NodesetTestFixture.MachineryModelUri, "MachineryLifetimeCounterType");
        var declarations = fixture.Context.GetInstanceDeclarations(type.NodeId);
        var placeholder = fixture.Declaration(type.NodeId, "<LifetimeVariable>");
        var mandatoryDescendants = MandatoryDescendants(declarations, placeholder);
        var root = LiveNodeFactory.Object(
            new QualifiedName("Counter", fixture.NamespaceIndex(NodesetTestFixture.MachineryModelUri)),
            type.NodeId);

        var gen01 = new MissingMandatoryChildRule().Validate(root, type, fixture.Context).ToArray();
        var gen06 = new MandatoryPlaceholderRule().Validate(root, type, fixture.Context).ToArray();

        Assert.NotEmpty(mandatoryDescendants);
        Assert.DoesNotContain(gen01, finding =>
            mandatoryDescendants.Any(descendant =>
                finding.BrowsePath.Contains(descendant.BrowseName.Name, StringComparison.Ordinal)));
        Assert.Single(gen06, finding => finding.BrowsePath.Contains("<LifetimeVariable>", StringComparison.Ordinal));
    }

    [Fact]
    public void Gen12_ignores_missing_method_but_reports_missing_arguments_on_present_method()
    {
        var type = fixture.FindType(NodesetTestFixture.DiModelUri, "LockingServicesType");
        var methodDeclaration = fixture.Declaration(type.NodeId, "InitLock");
        var root = LiveNodeFactory.Object(
            new QualifiedName("Lock", fixture.NamespaceIndex(NodesetTestFixture.DiModelUri)),
            type.NodeId);
        var gen01 = new MissingMandatoryChildRule();
        var gen12 = new MethodDeclarationRule();

        Assert.Contains(gen01.Validate(root, type, fixture.Context), finding =>
            finding.BrowsePath.Contains("InitLock", StringComparison.Ordinal));
        Assert.Empty(gen12.Validate(root, type, fixture.Context));

        var method = new LiveNode
        {
            NodeId = new NodeId(60_001u, methodDeclaration.BrowseName.NamespaceIndex),
            BrowseName = methodDeclaration.BrowseName,
            DisplayName = methodDeclaration.BrowseName.Name,
            NodeClass = NodeClass.Method
        };
        LiveNodeFactory.AddChild(root, method, methodDeclaration.ReferenceTypeId);

        var findings = gen12.Validate(root, type, fixture.Context).ToArray();

        Assert.NotEmpty(findings);
        Assert.All(findings, finding => Assert.Contains("Arguments", finding.BrowsePath, StringComparison.Ordinal));
    }

    [Fact]
    public void Gen01_mandatory_placeholder_incomplete_instance_reports_single_finding_on_instance()
    {
        var type = fixture.FindType(NodesetTestFixture.MachineryModelUri, "MachineryLifetimeCounterType");
        var declarations = fixture.Context.GetInstanceDeclarations(type.NodeId);
        var placeholder = fixture.Declaration(type.NodeId, "<LifetimeVariable>");
        var mandatoryDescendants = MandatoryDescendants(declarations, placeholder);
        Assert.True(mandatoryDescendants.Count >= 2);

        var omitted = mandatoryDescendants[0];
        var provided = mandatoryDescendants.Skip(1).ToArray();
        var machinery = fixture.NamespaceIndex(NodesetTestFixture.MachineryModelUri);
        var rule = new MissingMandatoryChildRule();

        var root = LiveNodeFactory.Object(new QualifiedName("Counter", machinery), type.NodeId);
        var instance = AddFulfillingPlaceholderInstance(root, "OperatingHours", placeholder, provided);

        var findings = rule.Validate(root, type, fixture.Context).ToArray();
        var instanceFindings = findings.Where(finding => finding.NodeId == instance.NodeId).ToArray();

        Assert.Single(instanceFindings);
        Assert.Equal("GEN-01", instanceFindings[0].RuleId);
        Assert.Contains(omitted.BrowseName.Name, instanceFindings[0].BrowsePath, StringComparison.Ordinal);
        Assert.DoesNotContain(instanceFindings, finding => finding.BrowsePath.Contains('<'));
    }

    [Fact]
    public void Gen01_finding_carries_declaring_type_documentation_deep_link()
    {
        var type = fixture.FindType(NodesetTestFixture.IaModelUri, "BasicStacklightType");
        var placeholder = fixture.Declaration(type.NodeId, "<OrderedObject>");
        var stackElementType = fixture.FindType(NodesetTestFixture.IaModelUri, "StackElementType");
        Assert.True(fixture.Model.TryGetDocumentation(stackElementType.NodeId, out var expectedUrl));

        var ia = fixture.NamespaceIndex(NodesetTestFixture.IaModelUri);
        var rule = new MissingMandatoryChildRule();

        var root = LiveNodeFactory.Object(new QualifiedName("Stacklight", ia), type.NodeId);
        var instance = AddFulfillingPlaceholderInstance(root, "Element0", placeholder, []);

        var findings = rule.Validate(root, type, fixture.Context).ToArray();
        var instanceFindings = findings.Where(finding => finding.NodeId == instance.NodeId).ToArray();

        Assert.NotEmpty(instanceFindings);
        Assert.All(instanceFindings, finding => Assert.Equal(expectedUrl, finding.DeclaringTypeReferenceUrl));
    }

    [Fact]
    public void Placeholder_traversal_handles_multi_level_placeholders()
    {
        // No loaded companion spec declares a placeholder beneath another placeholder, so this
        // exercises the declaration-driven traversal against a synthetic two-level placeholder chain
        // (<Outer> OptionalPlaceholder / <Inner> OptionalPlaceholder / Leaf Mandatory).
        var ns = fixture.NamespaceIndex(NodesetTestFixture.IaModelUri);
        var typeDefinitionId = fixture.NodeId(NodesetTestFixture.IaModelUri, 1005);
        var referenceTypeId = new NodeId(47);
        var optionalPlaceholder = new NodeId(11508);
        var mandatory = new NodeId(78);

        var outerSegment = new QualifiedName("<Outer>", ns);
        var innerSegment = new QualifiedName("<Inner>", ns);
        var leafSegment = new QualifiedName("Leaf", ns);

        var outerDeclaration = new InstanceDeclaration(
            new NodeId(9001u, ns), [outerSegment], outerSegment, NodeClass.Object, typeDefinitionId, optionalPlaceholder, referenceTypeId);
        var innerDeclaration = new InstanceDeclaration(
            new NodeId(9002u, ns), [outerSegment, innerSegment], innerSegment, NodeClass.Object, typeDefinitionId, optionalPlaceholder, referenceTypeId);
        var leafDeclaration = new InstanceDeclaration(
            new NodeId(9003u, ns), [outerSegment, innerSegment, leafSegment], leafSegment, NodeClass.Variable, null, mandatory, referenceTypeId);
        var declarations = new[] { outerDeclaration, innerDeclaration, leafDeclaration };

        var root = LiveNodeFactory.Object(new QualifiedName("Root", ns), typeDefinitionId);
        var outer = LiveNodeFactory.Object(new QualifiedName("outer0", ns), typeDefinitionId);
        LiveNodeFactory.AddChild(root, outer, referenceTypeId);
        var inner = LiveNodeFactory.Object(new QualifiedName("inner0", ns), typeDefinitionId);
        LiveNodeFactory.AddChild(outer, inner, referenceTypeId);

        Assert.True(GenericRuleHelpers.CrossesPlaceholderAncestor(declarations, leafDeclaration));
        Assert.False(GenericRuleHelpers.BrowsePathExists(fixture.Context, root, declarations, [outerSegment, innerSegment, leafSegment]));

        var missing = GenericRuleHelpers.FindPlaceholderInstancesMissingChild(fixture.Context, root, declarations, leafDeclaration);
        Assert.Single(missing);
        Assert.Same(inner, missing[0].Instance);

        LiveNodeFactory.AddChild(inner, LiveNodeFactory.Variable(leafSegment), referenceTypeId);

        Assert.True(GenericRuleHelpers.BrowsePathExists(fixture.Context, root, declarations, [outerSegment, innerSegment, leafSegment]));
        Assert.Empty(GenericRuleHelpers.FindPlaceholderInstancesMissingChild(fixture.Context, root, declarations, leafDeclaration));
    }

    private static readonly NodeId MandatoryModellingRule = new(78);

    private static IReadOnlyList<InstanceDeclaration> MandatoryDescendants(
        IReadOnlyList<InstanceDeclaration> declarations,
        InstanceDeclaration placeholder) =>
        declarations.Where(declaration =>
            declaration.BrowsePath.Count > 1 &&
            declaration.ModellingRuleId == MandatoryModellingRule &&
            declaration.BrowsePath[0].NamespaceIndex == placeholder.BrowseName.NamespaceIndex &&
            string.Equals(declaration.BrowsePath[0].Name, placeholder.BrowseName.Name, StringComparison.Ordinal))
            .ToArray();

    private static LiveNode AddFulfillingPlaceholderInstance(
        LiveNode root,
        string instanceName,
        InstanceDeclaration placeholder,
        IReadOnlyList<InstanceDeclaration> mandatoryDescendants)
    {
        var ns = placeholder.BrowseName.NamespaceIndex;
        var instance = placeholder.NodeClass == NodeClass.Variable
            ? LiveNodeFactory.Variable(new QualifiedName(instanceName, ns), placeholder.TypeDefinitionId)
            : LiveNodeFactory.Object(new QualifiedName(instanceName, ns), placeholder.TypeDefinitionId);
        LiveNodeFactory.AddChild(root, instance, placeholder.ReferenceTypeId);

        foreach (var descendant in mandatoryDescendants.OrderBy(declaration => declaration.BrowsePath.Count))
        {
            var suffix = descendant.BrowsePath.Skip(1).ToArray();
            var current = instance;
            for (var depth = 0; depth < suffix.Length; depth++)
            {
                var segment = suffix[depth];
                var existing = current.Children.FirstOrDefault(child =>
                    child.BrowseName.NamespaceIndex == segment.NamespaceIndex &&
                    string.Equals(child.BrowseName.Name, segment.Name, StringComparison.Ordinal));
                if (existing != null)
                {
                    current = existing;
                    continue;
                }

                var isLeaf = depth == suffix.Length - 1;
                var nodeClass = isLeaf ? descendant.NodeClass : NodeClass.Object;
                var typeDefinitionId = isLeaf ? descendant.TypeDefinitionId : null;
                var referenceTypeId = isLeaf ? descendant.ReferenceTypeId : ReferenceTypeIds.HasComponent;
                var child = nodeClass == NodeClass.Variable
                    ? LiveNodeFactory.Variable(segment, typeDefinitionId)
                    : LiveNodeFactory.Object(segment, typeDefinitionId);
                LiveNodeFactory.AddChild(current, child, referenceTypeId);
                current = child;
            }
        }

        return instance;
    }
}
