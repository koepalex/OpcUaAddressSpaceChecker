using Aspire.Hosting;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var opcplc = builder
    .AddContainer("opcplc", "mcr.microsoft.com/iotedge/opc-plc", "2.14.19")
    .WithEndpoint(port: 50000, targetPort: 50000, scheme: "opc.tcp", name: "opcua")
    .WithArgs("--ph=localhost")
    .WithArgs("--cdn=localhost,opcplc")
    .WithArgs("--autoaccept")
    .WithArgs("--sn=25")
    .WithArgs("--sr=10")
    .WithArgs("--fn=2000")
    .WithArgs("--veryfastrate=1000")
    .WithArgs("--gn=5")
    .WithArgs("--pn=50000")
    .WithArgs("--maxsessioncount=100")
    .WithArgs("--maxsubscriptioncount=100")
    .WithArgs("--maxqueuedrequestcount=2000")
    .WithArgs("--ses")
    .WithArgs("--alm")
    .WithArgs("--pumps")
    .WithArgs("--at=FlatDirectory")
    .WithArgs("--drurs");

var opcPlcEndpoint = opcplc.GetEndpoint("opcua");

builder.AddProject<OpcUaAddressSpaceChecker>("opcua-address-space-checker")
    .WithEnvironment("OPCUA_ENDPOINT", opcPlcEndpoint)
    .WithArgs("--nodeset-dir", @"C:\ode\UA-Nodeset");

builder.Build().Run();
