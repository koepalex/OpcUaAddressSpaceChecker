using System.ComponentModel;
using System.Diagnostics;

using Aspire.Hosting;

const int JitRegistryPort = 5000;
const string JitImageName = "opcua-ijt-server";
const string JitImageTag = "latest";
const string JitRegistryImageRef = "localhost:5000/opcua-ijt-server";

var builder = DistributedApplication.CreateBuilder(args);

// The JIT (umati Industrial Joining Technologies) OPC UA server is built locally and served from a
// local Docker registry (localhost:5000). Ensure the image is present before wiring the container,
// building it via build-jit-server.ps1 when the registry does not yet have it.
EnsureJitImage(builder.AppHostDirectory);

var reportsDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "reports"));
var opcPlcReportPath = Path.Combine(reportsDirectory, "opcplc.md");
var umatiReportPath = Path.Combine(reportsDirectory, "umati.md");
var jitReportPath = Path.Combine(reportsDirectory, "jit.md");
var pumpReportPath = Path.Combine(reportsDirectory, "pump.md");

var opcplc = builder
    .AddContainer("opcplc", "mcr.microsoft.com/iotedge/opc-plc", "2.14.20")
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

var umati = builder
    .AddContainer("umati", "ghcr.io/umati/sample-server", "develop")
    .WithEndpoint(port: 4840, targetPort: 4840, scheme: "opc.tcp", name: "opcua");

var jit = builder
    .AddContainer("jit", JitRegistryImageRef, JitImageTag)
    .WithEndpoint(port: 40451, targetPort: 40451, scheme: "opc.tcp", name: "opcua")
    .WithEnvironment("OPCUA_HOSTNAME", "localhost");

// The OPC Foundation Pump Device Integration reference server implements the Pumps companion
// specification. It listens on opc.tcp port 62542 and accepts anonymous/None connections.
var pump = builder
    .AddContainer("pump", "ghcr.io/opcfoundation/pumpdeviceintegrationserver", "2.0.78.11812-preview")
    .WithEndpoint(port: 62542, targetPort: 62542, scheme: "opc.tcp", name: "opcua");

var opcPlcEndpoint = opcplc.GetEndpoint("opcua");
var umatiEndpoint = umati.GetEndpoint("opcua");
var jitEndpoint = jit.GetEndpoint("opcua");
var pumpEndpoint = pump.GetEndpoint("opcua");

// The address-space checker is a run-once console tool. It is wired as an executable (not a project
// resource) so DCP always launches it as a process: project resources can be delegated to an "IDE"
// runner that is unavailable when the AppHost is not started from a supported IDE, which fails with
// "no runner found for execution type 'IDE'". Running the built assembly via `dotnet <dll>` sidesteps
// that entirely. The assembly is produced by the AppHost's ProjectReference to the checker.
var checkerAssemblyPath = ResolveCheckerAssemblyPath(builder.AppHostDirectory);

builder.AddExecutable(
        "opcua-address-space-checker",
        "dotnet",
        builder.AppHostDirectory,
        checkerAssemblyPath, "--retry-count", "5", "--retry-delay", "3", "--output-format", "markdown", "--output", opcPlcReportPath)
    .WithEnvironment("OPCUA_ENDPOINT", opcPlcEndpoint)
    .WaitFor(opcplc);

builder.AddExecutable(
        "opcua-address-space-checker-umati",
        "dotnet",
        builder.AppHostDirectory,
        checkerAssemblyPath, "--retry-count", "5", "--retry-delay", "3", "--output-format", "markdown", "--output", umatiReportPath)
    .WithEnvironment("OPCUA_ENDPOINT", umatiEndpoint)
    .WaitFor(umati);

builder.AddExecutable(
        "opcua-address-space-checker-jit",
        "dotnet",
        builder.AppHostDirectory,
        checkerAssemblyPath, "--retry-count", "5", "--retry-delay", "3", "--output-format", "markdown", "--output", jitReportPath)
    .WithEnvironment("OPCUA_ENDPOINT", jitEndpoint)
    .WaitFor(jit);

builder.AddExecutable(
        "opcua-address-space-checker-pump",
        "dotnet",
        builder.AppHostDirectory,
        checkerAssemblyPath, "--retry-count", "5", "--retry-delay", "3", "--output-format", "markdown", "--output", pumpReportPath)
    .WithEnvironment("OPCUA_ENDPOINT", pumpEndpoint)
    .WaitFor(pump);

builder.Build().Run();

// Resolves the built OpcUaAddressSpaceChecker assembly, which the AppHost ProjectReference builds
// alongside this host (matching build configuration). Running it via `dotnet <dll>` lets the checker
// be launched as a plain executable resource, independent of any IDE runner.
static string ResolveCheckerAssemblyPath(string appHostDirectory)
{
    const string configuration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    var repositoryRoot = Path.GetFullPath(Path.Combine(appHostDirectory, "..", ".."));
    var assemblyPath = Path.Combine(
        repositoryRoot,
        "src",
        "OpcUaAddressSpaceChecker",
        "bin",
        configuration,
        "net10.0",
        "OpcUaAddressSpaceChecker.dll");

    if (!File.Exists(assemblyPath))
    {
        throw new InvalidOperationException(
            $"Checker assembly not found at '{assemblyPath}'. Build the solution so the AppHost's " +
            "ProjectReference produces the checker before running.");
    }

    return assemblyPath;
}

// Ensures the locally built JIT OPC UA server image is available in the local Docker registry
// (localhost:5000). Probes the registry catalog and, when the image is missing, runs
// build-jit-server.ps1 to build and push it, then re-probes to confirm availability.
static void EnsureJitImage(string appHostDirectory)
{
    if (IsJitImagePresent())
    {
        return;
    }

    Console.WriteLine(
        "JIT image not found in local registry; building via build-jit-server.ps1 " +
        "(this can take several minutes on first run)…");

    var scriptPath = Path.Combine(appHostDirectory, "build-jit-server.ps1");
    if (!File.Exists(scriptPath))
    {
        throw new InvalidOperationException(
            $"JIT build script not found at '{scriptPath}'.");
    }

    RunBuildScript(scriptPath);

    if (!IsJitImagePresent())
    {
        throw new InvalidOperationException(
            "JIT server image is still not present in the local registry after running " +
            "build-jit-server.ps1; see the script output above for details.");
    }
}

static bool IsJitImagePresent()
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var tagsUri = $"http://localhost:{JitRegistryPort}/v2/{JitImageName}/tags/list";
        var body = http.GetStringAsync(tagsUri).GetAwaiter().GetResult();
        return body.Contains($"\"{JitImageTag}\"", StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

static void RunBuildScript(string scriptPath)
{
    // Prefer PowerShell 7 (pwsh); fall back to Windows PowerShell when pwsh is not on PATH.
    foreach (var shell in new[] { "pwsh", "powershell.exe" })
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start '{shell}'.");
        }
        catch (Win32Exception)
        {
            // Shell not found on PATH; try the next candidate.
            continue;
        }

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "JIT server image build failed; see build-jit-server.ps1 output.");
        }

        return;
    }

    throw new InvalidOperationException(
        "Neither 'pwsh' nor 'powershell.exe' could be started to run build-jit-server.ps1.");
}
