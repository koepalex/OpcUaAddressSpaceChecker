using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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
    private static readonly string[] OutputFormats = ["console", "json", "sarif", "markdown"];
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
            Description = "Optional NodeSet2 XML file to load as a type-model override instead of the live server types. May be specified multiple times.",
            DefaultValueFactory = (_) => []
        };

        var nodesetDirOption = new Option<string[]>(
            name: "--nodeset-dir")
        {
            Description = "Optional directory searched for companion NodeSet2 XML files when --nodeset is supplied. May be specified multiple times.",
            DefaultValueFactory = (_) => []
        };

        var typeOption = new Option<string?>(
            name: "--type")
        {
            Description = "Optional ObjectType or VariableType ExpandedNodeId. When supplied, validates only instances of that type or its subtypes.",
            DefaultValueFactory = (_) => null
        };

        var configOption = new Option<string?>(
            name: "--config")
        {
            Description = "Optional path to an appsettings.json config file (suppressed BrowsePaths and per-rule enable/severity). When omitted, appsettings.json is searched in the working directory and next to the tool; built-in defaults apply if none is found.",
            DefaultValueFactory = (_) => EnvironmentVariables.GetValue(EnvironmentVariables.ConfigPath)
        };

        var outputFormatOption = new Option<string>(
            name: "--output-format")
        {
            Description = "Output format: console, json, sarif, markdown",
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
        Options.Add(typeOption);
        Options.Add(configOption);
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
            var nodesetDirs = parseResult.GetValue(nodesetDirOption) ?? [];
            var targetTypeId = parseResult.GetValue(typeOption);
            var configPath = parseResult.GetValue(configOption);
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
                targetTypeId,
                configPath,
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
        string? targetTypeId,
        string? configPath,
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
            ExpandedNodeId? parsedTargetTypeId = null;
            if (!string.IsNullOrWhiteSpace(targetTypeId))
            {
                if (!TypeDefinitionSelector.TryParse(targetTypeId, out parsedTargetTypeId, out var typeParseError))
                {
                    logger.LogError("{Message}", typeParseError);
                    return 10;
                }
            }

            if (!IsOneOf(outputFormat, OutputFormats))
            {
                logger.LogError("Invalid output format: {OutputFormat}. Valid values: console, json, sarif, markdown", outputFormat);
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
                NodesetSearchDirs = nodesetDirs,
                TargetTypeId = targetTypeId,
                OutputFormat = outputFormat,
                OutputPath = output,
                SeverityThreshold = severityThreshold,
                IncludeRuleIds = ruleIds,
                ExcludeRuleIds = excludeRules,
                ConfigPath = configPath,
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

            var useNodesetOverride = options.NodesetPaths.Length > 0;

            logger.LogInformation("OPC UA Address Space Checker");
            logger.LogInformation("============================");
            logger.LogInformation("Endpoint: {Endpoint}", string.IsNullOrWhiteSpace(options.Endpoint) ? "(not provided)" : options.Endpoint);
            logger.LogInformation(
                "Type-model source: {Source}",
                useNodesetOverride ? "NodeSet2 files (override)" : "live server Types folder (i=86)");
            logger.LogInformation("Output format: {OutputFormat}", options.OutputFormat);
            logger.LogInformation("Severity threshold: {SeverityThreshold}", options.SeverityThreshold);
            if (parsedTargetTypeId != null)
            {
                logger.LogInformation("Target type: {TargetType} (including subtypes)", parsedTargetTypeId);
            }

            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                logger.LogError("Endpoint is required. Provide via --endpoint or OPCUA_ENDPOINT environment variable.");
                return 10;
            }

            var severityThresholdValue = ParseSeverity(severityThreshold);

            // Load user configuration (suppressed BrowsePaths + per-rule enable/severity). Missing
            // file => built-in defaults; an explicit --config that is missing or malformed fails fast.
            CheckerConfig config;
            try
            {
                var resolvedConfigPath = CheckerConfigLoader.ResolveConfigPath(options.ConfigPath);
                config = CheckerConfigLoader.Load(resolvedConfigPath);
                logger.LogInformation(
                    "Configuration: {Source} ({Suppressed} suppressed path(s), {Overrides} rule override(s)).",
                    resolvedConfigPath ?? "built-in defaults",
                    config.SuppressedBrowsePaths.Count,
                    config.Rules.Count);
            }
            catch (Exception ex) when (ex is FileNotFoundException or JsonException)
            {
                logger.LogError(ex, "Failed to load configuration file.");
                return 10;
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

            // Build the type model. By default it is read live from the server's Types folder (i=86);
            // NodeSet2 files supplied via --nodeset act as an override for servers that omit companion types.
            NodesetModelIndex typeModel;
            try
            {
                if (useNodesetOverride)
                {
                    var loadOrder = new NodesetDependencyResolver()
                        .ResolveLoadOrder(options.NodesetPaths, options.NodesetSearchDirs);
                    var loaded = new NodesetLoader().Load(loadOrder);
                    typeModel = new NodesetModelIndex(loaded);

                    logger.LogInformation(
                        "Loaded {Count} NodeSet2 file(s) as a type-model override: {Files}",
                        loaded.LoadedPaths.Count,
                        loaded.LoadedPaths.Count == 0 ? "(none)" : string.Join(", ", loaded.LoadedPaths.Select(Path.GetFileName)));

                    if (loaded.LoadedPaths.Count == 0)
                    {
                        logger.LogWarning("No NodeSet2 models were loaded; type-based checks may be limited.");
                    }
                }
                else
                {
                    var typeModelBrowser = new LiveTypeModelBrowser(
                        loggerFactory.CreateLogger<LiveTypeModelBrowser>(), client);
                    var liveTypeModel = await typeModelBrowser.FetchTypeModelAsync(cancellationToken).ConfigureAwait(false);
                    typeModel = LiveNodesetModel.Build(liveTypeModel);

                    logger.LogInformation(
                        "Built live type model with {TypeCount} type(s) from {NodeCount} browsed node(s).",
                        typeModel.TypesById.Count,
                        liveTypeModel.Nodes.Count);
                }
            }
            catch (Exception ex) when (ex is NodesetDependencyNotFoundException or FileNotFoundException or InvalidDataException)
            {
                logger.LogError(ex, "Failed to load NodeSet2 type models.");
                return 3;
            }

            // Browse the live address space into materialized LiveNodes.
            var browser = new AddressSpaceBrowser(loggerFactory.CreateLogger<AddressSpaceBrowser>(), client);
            var snapshot = await browser.FetchAllNodesAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyCollection<LiveNode> validationNodes = snapshot.Nodes;

            if (parsedTargetTypeId != null)
            {
                var selection = TypeDefinitionSelector.Select(
                    parsedTargetTypeId,
                    snapshot.Nodes,
                    client.Session.NamespaceUris,
                    typeModel);

                if (!selection.IsSuccess)
                {
                    logger.LogError("{Message}", selection.ErrorMessage);
                    return selection.Status switch
                    {
                        TypeDefinitionSelectionStatus.InvalidTypeId => 10,
                        TypeDefinitionSelectionStatus.TypeNotFound => 3,
                        _ => 1
                    };
                }

                validationNodes = selection.Nodes;
                logger.LogInformation(
                    "Selected {Count} instance(s) of {TargetType} or its subtypes for validation.",
                    validationNodes.Count,
                    parsedTargetTypeId);
            }

            // Auto-discover and run validation rules. Config-disabled rules are excluded alongside
            // any --exclude-rule values.
            var excludedRuleIds = options.ExcludeRuleIds.Concat(config.GetDisabledRuleIds()).ToArray();
            var registry = new RuleRegistry(options.IncludeRuleIds, excludedRuleIds);
            registry.AutoDiscover(typeof(CheckCommand).Assembly);
            logger.LogInformation(
                "Registered {Count} validation rule(s){Excluded}.",
                registry.Rules.Count,
                excludedRuleIds.Length > 0 ? $" ({excludedRuleIds.Length} excluded: {string.Join(", ", excludedRuleIds)})" : string.Empty);

            var engine = new ValidationEngine(registry, typeModel, loggerFactory.CreateLogger<ValidationEngine>());
            var report = await engine.RunAsync(validationNodes, client.Session, cancellationToken).ConfigureAwait(false);

            // Apply configured per-rule severity overrides and BrowsePath suppression before the
            // severity threshold is applied.
            var configFiltered = FindingFilter.Apply(report.Findings, snapshot.BrowsePathsByNodeId, config);
            if (configFiltered.SuppressedCount > 0)
            {
                logger.LogInformation(
                    "Suppressed {Count} finding(s) via configured BrowsePath filters.",
                    configFiltered.SuppressedCount);
            }

            // Apply the severity threshold to the reported findings.
            var thresholdFindings = configFiltered.Findings
                .Where(finding => finding.Severity >= severityThresholdValue)
                .ToList();

            // Informational findings (e.g. optional interface members that are not implemented) are
            // always surfaced in their own report section, independent of the severity threshold, and
            // never affect the exit code. When the threshold already includes Information they are
            // present in thresholdFindings, so only add the remainder to avoid duplicates.
            var reportFindings = severityThresholdValue <= Severity.Information
                ? thresholdFindings
                : thresholdFindings
                    .Concat(configFiltered.Findings.Where(finding => finding.Severity == Severity.Information))
                    .ToList();
            var outputReport = new ValidationReport(report.TotalNodes, reportFindings.Count, reportFindings);

            // Select the reporter and render the report to a file or stdout.
            var namespaceSnapshot = BuildNamespaceSnapshot(client.Session.NamespaceUris);
            var nodeIdFormatter = new NodeIdDisplayFormatter(
                namespaceSnapshot,
                BuildBrowseNameSnapshot(snapshot.Nodes));
            IReporter reporter = options.OutputFormat.ToLowerInvariant() switch
            {
                "json" => new JsonReporter(nodeIdFormatter),
                "sarif" => new SarifReporter(nodeIdFormatter),
                "markdown" => new MarkdownReporter(
                    namespaceSnapshot,
                    snapshot.BrowsePathsByNodeId,
                    nodeIdFormatter),
                _ => new ConsoleTableReporter(nodeIdFormatter)
            };

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                var requestedPath = Path.GetFullPath(options.OutputPath);
                var outputFullPath = Path.GetFullPath(ApplyFormatExtension(options.OutputPath, options.OutputFormat));
                if (!string.Equals(requestedPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Output extension changed to match format '{Format}': {Requested} -> {Actual}",
                        options.OutputFormat, requestedPath, outputFullPath);
                }

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
                thresholdFindings.Count,
                severityThreshold,
                outputReport.ErrorCount,
                outputReport.WarningCount,
                outputReport.InformationCount);

            // Informational findings never fail the run; the exit code reflects only findings at or
            // above the configured severity threshold.
            return thresholdFindings.Count > 0 ? 1 : 0;
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

    private static string ApplyFormatExtension(string path, string format) => format.ToLowerInvariant() switch
    {
        "json" => Path.ChangeExtension(path, ".json"),
        "sarif" => Path.ChangeExtension(path, ".sarif"),
        "markdown" => Path.ChangeExtension(path, ".md"),
        _ => path
    };

    private static IReadOnlyDictionary<ushort, string> BuildNamespaceSnapshot(NamespaceTable namespaceUris)
    {
        var snapshot = new Dictionary<ushort, string>();
        for (ushort index = 0; index < namespaceUris.Count; index++)
        {
            snapshot[index] = namespaceUris.GetString(index);
        }

        return snapshot;
    }

    /// <summary>
    /// Builds a NodeId -&gt; <c>namespaceIndex:BrowseName</c> snapshot from the browsed live nodes so
    /// reporters can render the NodeId column with a readable BrowseName. Nodes without a BrowseName
    /// are skipped (the reporter then falls back to the ExpandedNodeId/NodeId).
    /// </summary>
    private static IReadOnlyDictionary<NodeId, string> BuildBrowseNameSnapshot(IReadOnlyCollection<LiveNode> nodes)
    {
        var snapshot = new Dictionary<NodeId, string>();
        foreach (var node in nodes)
        {
            if (node.NodeId is null || string.IsNullOrEmpty(node.BrowseName.Name))
            {
                continue;
            }

            snapshot[node.NodeId] = $"{node.BrowseName.NamespaceIndex}:{node.BrowseName.Name}";
        }

        return snapshot;
    }

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
