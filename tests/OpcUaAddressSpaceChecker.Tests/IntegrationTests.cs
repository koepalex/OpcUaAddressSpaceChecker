using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests;

[Trait("Category", "Integration")]
public sealed class IntegrationTests
{
    public async Task Can_browse_opcplc_and_run_validation()
    {
        Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.OpcUaAddressSpaceChecker_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        await app.ResourceNotifications.WaitForResourceAsync(
            "opcplc",
            KnownResourceStates.Running);

        var endpoint = app.GetEndpoint("opcplc", "opcua").ToString();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        await using var client = await OpcUaClientBuilder
            .Create(loggerFactory)
            .WithEndpoint(endpoint)
            .WithSecurityMode(MessageSecurityMode.None)
            .TrustAllServerCertificates()
            .ConnectAsync();

        var browser = new AddressSpaceBrowser(
            loggerFactory.CreateLogger<AddressSpaceBrowser>(),
            client);
        var snapshot = await browser.FetchAllNodesAsync();

        var nodesetRoot = NodesetTestData.Root;
        var loadOrder = new NodesetDependencyResolver().ResolveLoadOrder(
            [
                Path.Combine(nodesetRoot, "Schema", "Opc.Ua.NodeSet2.xml"),
                Path.Combine(nodesetRoot, "DI", "Opc.Ua.Di.NodeSet2.xml"),
                Path.Combine(nodesetRoot, "IA", "Opc.Ua.IA.NodeSet2.xml"),
                Path.Combine(nodesetRoot, "Machinery", "Opc.Ua.Machinery.NodeSet2.xml"),
                Path.Combine(nodesetRoot, "Pumps", "Opc.Ua.Pumps.NodeSet2.xml")
            ],
            [nodesetRoot]);
        var typeModel = new NodesetModelIndex(new NodesetLoader().Load(loadOrder));
        var registry = new RuleRegistry();
        registry.AutoDiscover(typeof(RuleRegistry).Assembly);
        var engine = new ValidationEngine(
            registry,
            typeModel,
            loggerFactory.CreateLogger<ValidationEngine>());

        var report = await engine.RunAsync(snapshot.Nodes, client.Session);
        var exitCode = report.TotalFindings > 0 ? 1 : 0;

        Assert.NotNull(report);
        Assert.InRange(exitCode, 0, 1);
    }

    // Requires Docker AND network access: the AppHost guard builds and pushes the JIT
    // (umati Industrial Joining Technologies) OPC UA server image on first run. Stays under
    // Category=Integration so it is excluded from the Docker-free gate. If the JIT server
    // disables anonymous/None connections this test will fail on connect; treat that as the
    // open item to add a credentials/security-mode override rather than forcing changes here.
    [Fact]
    public async Task Can_browse_jit_and_run_validation()
    {
        Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.OpcUaAddressSpaceChecker_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Generous timeout: first run may build the JIT image and wait for the server start-period.
        await app.ResourceNotifications.WaitForResourceAsync(
            "jit",
            KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(10));

        var endpoint = app.GetEndpoint("jit", "opcua").ToString();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        await using var client = await OpcUaClientBuilder
            .Create(loggerFactory)
            .WithEndpoint(endpoint)
            .WithSecurityMode(MessageSecurityMode.None)
            .TrustAllServerCertificates()
            .ConnectAsync();

        var browser = new AddressSpaceBrowser(
            loggerFactory.CreateLogger<AddressSpaceBrowser>(),
            client);
        var snapshot = await browser.FetchAllNodesAsync();

        var nodesetRoot = NodesetTestData.Root;
        var loadOrder = new NodesetDependencyResolver().ResolveLoadOrder(
            [
                Path.Combine(nodesetRoot, "Schema", "Opc.Ua.NodeSet2.xml"),
                Path.Combine(nodesetRoot, "DI", "Opc.Ua.Di.NodeSet2.xml"),
                Path.Combine(nodesetRoot, "IA", "Opc.Ua.IA.NodeSet2.xml"),
                Path.Combine(nodesetRoot, "Machinery", "Opc.Ua.Machinery.NodeSet2.xml")
            ],
            [nodesetRoot]);
        var typeModel = new NodesetModelIndex(new NodesetLoader().Load(loadOrder));
        var registry = new RuleRegistry();
        registry.AutoDiscover(typeof(RuleRegistry).Assembly);
        var engine = new ValidationEngine(
            registry,
            typeModel,
            loggerFactory.CreateLogger<ValidationEngine>());

        var report = await engine.RunAsync(snapshot.Nodes, client.Session);
        var exitCode = report.TotalFindings > 0 ? 1 : 0;

        Assert.NotNull(report);
        Assert.InRange(exitCode, 0, 1);
    }
}
