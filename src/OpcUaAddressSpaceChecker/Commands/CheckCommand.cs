using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using OpcUaAddressSpaceChecker.Configuration;
using OpcUaAddressSpaceChecker.NodeModel;
using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Reporting;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Commands;

/// <summary>
/// The main check command for the OPC UA address space checker tool.
/// </summary>
public class CheckCommand : RootCommand
{
    private static readonly string[] OutputFormats = ["console", "json", "sarif"];
    private static readonly string[] SeverityThresholds = ["information", "warning", "error"];

    public CheckCommand() : base("Checks an OPC UA server address space against NodeSet2 type models.")
    {
        var endpointOption = new Option<string?>(
            name: "--endpoint",
            aliases: new[] { "-e" })
        {
            Description = "OPC UA server endpoint URL (e.g., opc.tcp://localhost:4840)",
            DefaultValueFactory = (_) => EnvironmentVariables.GetValue(EnvironmentVariables.Endpoint)
        };

        var securityModeOption = new Option<string>(
            name: "--security-mode",
            aliases: new[] { "-m" })
        {
            Description = "Security mode: None, Sign, SignAndEncrypt",
            DefaultValueFactory = (_) => "None"
        };

        var securityPolicyOption = new Option<string>(
            name: "--security-policy",
            aliases: new[] { "-p" })
        {
            Description = "Security policy: None, Basic256Sha256, Aes128_Sha256_RsaOaep, Aes256_Sha256_RsaPss",
            DefaultValueFactory = (_) => "None"
        };

        var authModeOption = new Option<string>(
            name: "--auth-mode",
            aliases: new[] { "-a" })
        {
            Description = "Authentication mode: Anonymous, UserName, Certificate",
            DefaultValueFactory = (_) => "Anonymous"
        };

        var usernameOption = new Option<string?>(
            name: "--username",
            aliases: new[] { "-u" })
        {
            Description = "Username for UserName authentication",
            DefaultValueFactory = (_) => EnvironmentVariables.GetValue(EnvironmentVariables.Username)
        };

        var passwordOption = new Option<string?>(
            name: "--password")
        {
            Description = "Password for UserName authentication",
            DefaultValueFactory = (_) => EnvironmentVariables.GetValue(EnvironmentVariables.Password)
        };

        var passwordFromStdinOption = new Option<bool>(
            name: "--password-from-stdin")
        {
            Description = "Read password from stdin (for piping)",
            DefaultValueFactory = (_) => false
        };

        var certificatePathOption = new Option<string?>(
            name: "--certificate-path",
            aliases: new[] { "-c" })
        {
            Description = "Path to client X.509 certificate (PFX format)",
            DefaultValueFactory = (_) => EnvironmentVariables.GetValue(EnvironmentVariables.CertificatePath)
        };

        var certificatePasswordOption = new Option<string?>(
            name: "--certificate-password")
        {
            Description = "Password for the client certificate",
            DefaultValueFactory = (_) => EnvironmentVariables.GetValue(EnvironmentVariables.CertificatePassword)
        };

        var certificateFromStdinOption = new Option<bool>(
            name: "--certificate-from-stdin")
        {
            Description = "Read certificate (base64 PFX) from stdin",
            DefaultValueFactory = (_) => false
        };

        var nodesetOption = new Option<string[]>(
            name: "--nodeset")
        {
            Description = "Path to a NodeSet2 XML file to load. May be specified multiple times.",
            DefaultValueFactory = (_) => []
        };

        var nodesetDirOption = new Option<string[]>(
            name: "--nodeset-dir")
        {
            Description = @"Directory searched for companion NodeSet2 XML files. May be specified multiple times.",
            DefaultValueFactory = (_) => [@"C:\ode\UA-Nodeset"]
        };

        var outputFormatOption = new Option<string>(
            name: "--output-format")
        {
            Description = "Output format: console, json, sarif",
            DefaultValueFactory = (_) => "console"
        };

        var outputOption = new Option<string?>(
            name: "--output",
            aliases: new[] { "-o" })
        {
            Description = "Optional output file path. Console output writes to stdout when omitted.",
            DefaultValueFactory = (_) => null
        };

        var severityThresholdOption = new Option<string>(
            name: "--severity-threshold")
        {
            Description = "Minimum severity included in results: information, warning, error",
            DefaultValueFactory = (_) => "warning"
        };

        var ruleIdOption = new Option<string[]>(
            name: "--rule-id")
        {
            Description = "Rule ID to include. May be specified multiple times. Empty means all rules.",
            DefaultValueFactory = (_) => []
        };

        var excludeRuleOption = new Option<string[]>(
            name: "--exclude-rule")
        {
            Description = "Rule ID to exclude. May be specified multiple times.",
            DefaultValueFactory = (_) => []
        };

        var retryCountOption = new Option<int>(
            name: "--retry-count")
        {
            Description = "Number of reconnection attempts on disconnect",
            DefaultValueFactory = (_) => 3
        };

        var retryDelayOption = new Option<int>(
            name: "--retry-delay")
        {
            Description = "Delay between retries in seconds",
            DefaultValueFactory = (_) => 5
        };

        var verboseOption = new Option<bool>(
            name: "--verbose",
            aliases: new[] { "-v" })
        {
            Description = "Enable verbose logging",
            DefaultValueFactory = (_) => false
        };

        var logFileOption = new Option<string?>(
            name: "--log-file")
        {
            Description = "Optional path to a log file. Parent directories are created on demand.",
            DefaultValueFactory = (_) => EnvironmentVariables.GetValue(EnvironmentVariables.LogFile)
        };

        Options.Add(endpointOption);
        Options.Add(securityModeOption);
        Options.Add(securityPolicyOption);
        Options.Add(authModeOption);
        Options.Add(usernameOption);
        Options.Add(passwordOption);
        Options.Add(passwordFromStdinOption);
        Options.Add(certificatePathOption);
        Options.Add(certificatePasswordOption);
        Options.Add(certificateFromStdinOption);
        Options.Add(nodesetOption);
        Options.Add(nodesetDirOption);
        Options.Add(outputFormatOption);
        Options.Add(outputOption);
        Options.Add(severityThresholdOption);
        Options.Add(ruleIdOption);
        Options.Add(excludeRuleOption);
        Options.Add(retryCountOption);
        Options.Add(retryDelayOption);
        Options.Add(verboseOption);
        Options.Add(logFileOption);

        this.SetAction(async (parseResult, cancellationToken) =>
        {
            var endpoint = parseResult.GetValue(endpointOption);
            var securityMode = parseResult.GetValue(securityModeOption)!;
            var securityPolicy = parseResult.GetValue(securityPolicyOption)!;
            var authMode = parseResult.GetValue(authModeOption)!;
            var username = parseResult.GetValue(usernameOption);
            var password = parseResult.GetValue(passwordOption);
            var passwordFromStdin = parseResult.GetValue(passwordFromStdinOption);
            var certificatePath = parseResult.GetValue(certificatePathOption);
            var certificatePassword = parseResult.GetValue(certificatePasswordOption);
            var certificateFromStdin = parseResult.GetValue(certificateFromStdinOption);
            var nodesets = parseResult.GetValue(nodesetOption) ?? [];
            var nodesetDirs = parseResult.GetValue(nodesetDirOption) ?? [@"C:\ode\UA-Nodeset"];
            var outputFormat = parseResult.GetValue(outputFormatOption)!;
            var output = parseResult.GetValue(outputOption);
            var severityThreshold = parseResult.GetValue(severityThresholdOption)!;
            var ruleIds = parseResult.GetValue(ruleIdOption) ?? [];
            var excludeRules = parseResult.GetValue(excludeRuleOption) ?? [];
            var retryCount = parseResult.GetValue(retryCountOption);
            var retryDelay = parseResult.GetValue(retryDelayOption);
            var verbose = parseResult.GetValue(verboseOption);
            var logFile = parseResult.GetValue(logFileOption);

            return await ExecuteAsync(
                endpoint,
                securityMode,
                securityPolicy,
                authMode,
                username,
                password,
                passwordFromStdin,
                certificatePath,
                certificatePassword,
                certificateFromStdin,
                nodesets,
                nodesetDirs,
                outputFormat,
                output,
                severityThreshold,
                ruleIds,
                excludeRules,
                retryCount,
                retryDelay,
                verbose,
                logFile,
                cancellationToken);
        });
    }

    private static async Task<int> ExecuteAsync(
        string? endpoint,
        string securityMode,
        string securityPolicy,
        string authMode,
        string? username,
        string? password,
        bool passwordFromStdin,
        string? certificatePath,
        string? certificatePassword,
        bool certificateFromStdin,
        string[] nodesets,
        string[] nodesetDirs,
        string outputFormat,
        string? output,
        string severityThreshold,
        string[] ruleIds,
        string[] excludeRules,
        int retryCount,
        int retryDelay,
        bool verbose,
        string? logFile,
        CancellationToken cancellationToken)
    {
        var minLevel = verbose ? LogLevel.Debug : LogLevel.Information;
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(minLevel);
            if (!string.IsNullOrWhiteSpace(logFile))
            {
                builder.AddProvider(new Logging.FileLoggerProvider(logFile, minLevel));
            }
        });

        var logger = loggerFactory.CreateLogger<CheckCommand>();

        try
        {
            if (!IsOneOf(outputFormat, OutputFormats))
            {
                logger.LogError("Invalid output format: {OutputFormat}. Valid values: console, json, sarif", outputFormat);
                return 10;
            }

            if (!IsOneOf(severityThreshold, SeverityThresholds))
            {
                logger.LogError("Invalid severity threshold: {SeverityThreshold}. Valid values: information, warning, error", severityThreshold);
                return 10;
            }

            if (passwordFromStdin)
            {
                logger.LogDebug("Reading password from stdin...");
                password = await StdinReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(password))
                {
                    logger.LogError("No password provided via stdin.");
                    return 1;
                }
            }

            X509Certificate2? certificate = null;
            if (certificateFromStdin)
            {
                logger.LogDebug("Reading certificate from stdin (base64 PFX)...");
                var certBytes = await StdinReader.ReadBase64CertificateAsync(cancellationToken).ConfigureAwait(false);
                if (certBytes == null)
                {
                    logger.LogError("No certificate provided via stdin.");
                    return 1;
                }

                certificate = X509CertificateLoader.LoadPkcs12(certBytes, certificatePassword,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);
                logger.LogInformation("Loaded certificate from stdin: {Subject}", certificate.Subject);
            }
            else if (!string.IsNullOrEmpty(certificatePath))
            {
                var certManager = new CertificateManager(loggerFactory.CreateLogger<CertificateManager>());
                certificate = certManager.LoadCertificateFromFile(certificatePath, certificatePassword);
            }

            var options = new CheckerOptions
            {
                Endpoint = endpoint ?? string.Empty,
                SecurityMode = ParseSecurityMode(securityMode),
                SecurityPolicy = ParseSecurityPolicy(securityPolicy),
                AuthMode = ParseAuthMode(authMode),
                Username = username,
                Password = password,
                CertificatePath = certificatePath,
                CertificatePassword = certificatePassword,
                Certificate = certificate,
                NodesetPaths = nodesets,
                NodesetSearchDirs = nodesetDirs.Length == 0 ? [@"C:\ode\UA-Nodeset"] : nodesetDirs,
                OutputFormat = outputFormat,
                OutputPath = output,
                SeverityThreshold = severityThreshold,
                IncludeRuleIds = ruleIds,
                ExcludeRuleIds = excludeRules,
                RetryCount = retryCount,
                RetryDelaySeconds = retryDelay,
                Verbose = verbose,
                LogFile = logFile
            };

            if (options.AuthMode == AuthenticationMode.UserName &&
                (string.IsNullOrEmpty(options.Username) || string.IsNullOrEmpty(options.Password)))
            {
                logger.LogError("Username and password are required for UserName authentication.");
                return 10;
            }

            if (options.AuthMode == AuthenticationMode.Certificate && options.Certificate == null)
            {
                logger.LogError("Certificate is required for Certificate authentication.");
                return 10;
            }

            logger.LogInformation("OPC UA Address Space Checker");
            logger.LogInformation("============================");
            logger.LogInformation("Endpoint: {Endpoint}", string.IsNullOrWhiteSpace(options.Endpoint) ? "(not provided)" : options.Endpoint);
            logger.LogInformation("Nodeset search dirs: {NodesetDirs}", string.Join(", ", options.NodesetSearchDirs));
            logger.LogInformation("Output format: {OutputFormat}", options.OutputFormat);
            logger.LogInformation("Severity threshold: {SeverityThreshold}", options.SeverityThreshold);

            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                logger.LogError("Endpoint is required. Provide via --endpoint or OPCUA_ENDPOINT environment variable.");
                return 10;
            }

            var severityThresholdValue = ParseSeverity(severityThreshold);

            // Load NodeSet2 type models (base + companion + custom) in dependency order.
            NodesetModelIndex typeModel;
            try
            {
                var loadOrder = new NodesetDependencyResolver()
                    .ResolveLoadOrder(options.NodesetPaths, options.NodesetSearchDirs);
                var loaded = new NodesetLoader().Load(loadOrder);
                typeModel = new NodesetModelIndex(loaded);

                logger.LogInformation(
                    "Loaded {Count} NodeSet2 file(s): {Files}",
                    loaded.LoadedPaths.Count,
                    loaded.LoadedPaths.Count == 0 ? "(none)" : string.Join(", ", loaded.LoadedPaths.Select(Path.GetFileName)));

                if (loaded.LoadedPaths.Count == 0)
                {
                    logger.LogWarning("No NodeSet2 models were loaded; supply --nodeset to enable type-based checks.");
                }
            }
            catch (Exception ex) when (ex is NodesetDependencyNotFoundException or FileNotFoundException or InvalidDataException)
            {
                logger.LogError(ex, "Failed to load NodeSet2 type models.");
                return 3;
            }

            // Connect to the live server.
            logger.LogInformation("Connecting to OPC UA server...");
            await using var client = await OpcUaClientBuilder
                .Create(loggerFactory)
                .FromOptions(options)
                .TrustAllServerCertificates()
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("Connected successfully!");

            // Browse the live address space into materialized LiveNodes.
            var browser = new AddressSpaceBrowser(loggerFactory.CreateLogger<AddressSpaceBrowser>(), client);
            var liveNodes = await browser.FetchAllNodesAsync(cancellationToken).ConfigureAwait(false);

            // Auto-discover and run validation rules.
            var registry = new RuleRegistry(options.IncludeRuleIds, options.ExcludeRuleIds);
            registry.AutoDiscover(typeof(CheckCommand).Assembly);
            logger.LogInformation("Registered {Count} validation rule(s).", registry.Rules.Count);

            var engine = new ValidationEngine(registry, typeModel, loggerFactory.CreateLogger<ValidationEngine>());
            var report = await engine.RunAsync(liveNodes, client.Session, cancellationToken).ConfigureAwait(false);

            // Apply the severity threshold to the reported findings.
            var filteredFindings = report.Findings
                .Where(finding => finding.Severity >= severityThresholdValue)
                .ToList();
            var outputReport = new ValidationReport(report.TotalNodes, filteredFindings.Count, filteredFindings);

            // Select the reporter and render the report to a file or stdout.
            IReporter reporter = options.OutputFormat.ToLowerInvariant() switch
            {
                "json" => new JsonReporter(),
                "sarif" => new SarifReporter(),
                _ => new ConsoleTableReporter()
            };

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                var outputFullPath = Path.GetFullPath(options.OutputPath);
                var outputDirectory = Path.GetDirectoryName(outputFullPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                await using var fileWriter = new StreamWriter(outputFullPath, append: false);
                reporter.Report(outputReport, fileWriter);
                logger.LogInformation("Wrote {Format} report to {Path}", options.OutputFormat, outputFullPath);
            }
            else
            {
                reporter.Report(outputReport, Console.Out);
            }

            logger.LogInformation(
                "Validation complete: {Nodes} nodes checked, {Findings} finding(s) at or above '{Threshold}' (errors={Errors}, warnings={Warnings}, info={Info}).",
                report.TotalNodes,
                outputReport.TotalFindings,
                severityThreshold,
                report.ErrorCount,
                report.WarningCount,
                report.InformationCount);

            return outputReport.TotalFindings > 0 ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Operation cancelled.");
            return 130;
        }
        catch (OpcUaConnectionException ex)
        {
            logger.LogError(ex, "Failed to connect to OPC UA server.");
            return 2;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred.");
            return 1;
        }
    }

    private static bool IsOneOf(string value, IReadOnlyCollection<string> allowedValues) =>
        allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static Severity ParseSeverity(string threshold)
    {
        return threshold.ToLowerInvariant() switch
        {
            "information" => Severity.Information,
            "warning" => Severity.Warning,
            "error" => Severity.Error,
            _ => Severity.Warning
        };
    }

    private static MessageSecurityMode ParseSecurityMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "none" => MessageSecurityMode.None,
            "sign" => MessageSecurityMode.Sign,
            "signandencrypt" => MessageSecurityMode.SignAndEncrypt,
            _ => throw new ArgumentException($"Invalid security mode: {mode}. Valid values: None, Sign, SignAndEncrypt")
        };
    }

    private static string ParseSecurityPolicy(string policy)
    {
        return policy.ToLowerInvariant() switch
        {
            "none" => SecurityPolicies.None,
            "basic256sha256" => SecurityPolicies.Basic256Sha256,
            "aes128_sha256_rsaoaep" => SecurityPolicies.Aes128_Sha256_RsaOaep,
            "aes256_sha256_rsapss" => SecurityPolicies.Aes256_Sha256_RsaPss,
            _ => throw new ArgumentException($"Invalid security policy: {policy}. Valid values: None, Basic256Sha256, Aes128_Sha256_RsaOaep, Aes256_Sha256_RsaPss")
        };
    }

    private static AuthenticationMode ParseAuthMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "anonymous" => AuthenticationMode.Anonymous,
            "username" => AuthenticationMode.UserName,
            "certificate" => AuthenticationMode.Certificate,
            _ => throw new ArgumentException($"Invalid authentication mode: {mode}. Valid values: Anonymous, UserName, Certificate")
        };
    }
}
