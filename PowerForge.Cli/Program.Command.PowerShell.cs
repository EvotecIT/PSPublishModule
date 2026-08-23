using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string PowerShellAnalyzeUsage =
        "Usage: powerforge powershell analyze <path> [--mode <Analyze|Package|Hybrid|Strict>] [--no-recurse] [--output json]";
    private const string PowerShellBuildUsage =
        "Usage: powerforge powershell build <path> --kind <exe|dll|library> [--out <directory>] [--name <artifact>] [--mode <Package|Hybrid|Strict>] [--framework <tfm>] [--rid <rid>] [--self-contained] [--optimization <None|Trimmed|NativeAot>] [--sign] [--certificate-thumbprint <thumbprint>] [--certificate-store <CurrentUser|LocalMachine>] [--timestamp-server <url>] [--signing-timeout <seconds>] [--no-single-file] [--output json]";

    private static int CommandPowerShell(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Length == 0 || argv.Any(IsHelpArgument))
        {
            WritePowerShellHelp(outputJson);
            return 0;
        }

        if (!argv[0].Equals("analyze", StringComparison.OrdinalIgnoreCase))
        {
            if (argv[0].Equals("build", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("compile", StringComparison.OrdinalIgnoreCase))
                return CommandPowerShellBuild(argv.Skip(1).ToArray(), outputJson, logger);
            return WritePowerShellError(outputJson, 2, $"Unknown PowerShell subcommand '{argv[0]}'.", logger);
        }

        return CommandPowerShellAnalyze(argv.Skip(1).ToArray(), outputJson, logger);
    }

    private static int CommandPowerShellBuild(string[] args, bool outputJson, ILogger logger)
    {
        if (args.Any(IsHelpArgument))
        {
            WritePowerShellHelp(outputJson);
            return 0;
        }

        if (!TryValidatePowerShellArguments(
                args,
                new[] { "--path", "--kind", "--target", "--out", "--output-directory", "--name", "--mode", "--framework", "--rid", "--optimization", "--certificate-thumbprint", "--certificate-store", "--timestamp-server", "--signing-timeout", "--timeout", "--output" },
                new[] { "--self-contained", "--sign", "--no-single-file", "--keep-workspace", "--json", "--output-json" },
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.build");

        var path = TryGetOptionValue(args, "--path");
        if (string.IsNullOrWhiteSpace(path) && args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal))
            path = args[0];
        if (string.IsNullOrWhiteSpace(path))
            return WritePowerShellError(outputJson, 2, "A PowerShell source file is required.", logger, "powershell.build");

        var kindValue = TryGetOptionValue(args, "--kind") ?? TryGetOptionValue(args, "--target");
        if (!TryParseArtifactKind(kindValue, out var kind))
            return WritePowerShellError(outputJson, 2, "Artifact kind must be 'exe', 'dll', or 'library'.", logger, "powershell.build");

        var defaultMode = PowerShellCompilationBuildSpec.GetDefaultMode(kind);
        var modeValue = TryGetOptionValue(args, "--mode") ?? defaultMode.ToString();
        if (!Enum.TryParse<PowerShellCompilationMode>(modeValue, ignoreCase: true, out var mode) ||
            !Enum.IsDefined(typeof(PowerShellCompilationMode), mode) ||
            mode == PowerShellCompilationMode.Analyze)
            return WritePowerShellError(outputJson, 2, $"Unknown artifact compilation mode '{modeValue}'.", logger, "powershell.build");

        try
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            var outputDirectory = TryGetOptionValue(args, "--out") ?? TryGetOptionValue(args, "--output-directory") ?? Path.Combine(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(), "artifacts");
            var artifactName = TryGetOptionValue(args, "--name") ?? Path.GetFileNameWithoutExtension(fullPath);
            var optimizationValue = TryGetOptionValue(args, "--optimization") ?? nameof(PowerShellCompilationExecutableOptimization.None);
            if (!Enum.TryParse<PowerShellCompilationExecutableOptimization>(optimizationValue, ignoreCase: true, out var optimization) ||
                !Enum.IsDefined(typeof(PowerShellCompilationExecutableOptimization), optimization))
                return WritePowerShellError(outputJson, 2, $"Unknown executable optimization '{optimizationValue}'. Use None, Trimmed, or NativeAot.", logger, "powershell.build");
            var certificateStoreValue = TryGetOptionValue(args, "--certificate-store") ?? nameof(CertificateStoreLocation.CurrentUser);
            if (!Enum.TryParse<CertificateStoreLocation>(certificateStoreValue, ignoreCase: true, out var certificateStore) ||
                !Enum.IsDefined(typeof(CertificateStoreLocation), certificateStore))
                return WritePowerShellError(outputJson, 2, $"Unknown certificate store '{certificateStoreValue}'. Use CurrentUser or LocalMachine.", logger, "powershell.build");
            var spec = new PowerShellCompilationBuildSpec(fullPath, outputDirectory, artifactName, kind, mode)
            {
                TargetFramework = TryGetOptionValue(args, "--framework") ?? "net8.0",
                RuntimeIdentifier = TryGetOptionValue(args, "--rid"),
                SelfContained = args.Any(static argument => argument.Equals("--self-contained", StringComparison.OrdinalIgnoreCase)),
                SingleFile = !args.Any(static argument => argument.Equals("--no-single-file", StringComparison.OrdinalIgnoreCase)),
                Optimization = optimization,
                SignArtifact = args.Any(static argument => argument.Equals("--sign", StringComparison.OrdinalIgnoreCase)),
                CertificateThumbprint = TryGetOptionValue(args, "--certificate-thumbprint"),
                CertificateStoreLocation = certificateStore,
                TimeStampServer = TryGetOptionValue(args, "--timestamp-server") ?? "http://timestamp.digicert.com",
                KeepBuildWorkspace = args.Any(static argument => argument.Equals("--keep-workspace", StringComparison.OrdinalIgnoreCase))
            };
            var signingTimeoutValue = TryGetOptionValue(args, "--signing-timeout");
            if (!string.IsNullOrWhiteSpace(signingTimeoutValue))
            {
                if (!int.TryParse(signingTimeoutValue, out var signingTimeoutSeconds) || signingTimeoutSeconds < 1)
                    return WritePowerShellError(outputJson, 2, "--signing-timeout must be a positive number of seconds.", logger, "powershell.build");
                spec.SigningTimeoutSeconds = signingTimeoutSeconds;
            }
            var timeoutValue = TryGetOptionValue(args, "--timeout");
            if (!string.IsNullOrWhiteSpace(timeoutValue))
            {
                if (!int.TryParse(timeoutValue, out var timeoutSeconds) || timeoutSeconds < 1)
                    return WritePowerShellError(outputJson, 2, "--timeout must be a positive number of seconds.", logger, "powershell.build");
                spec.TimeoutSeconds = timeoutSeconds;
            }

            var result = new PowerShellCompilationArtifactBuilder().Build(spec);
            var exitCode = result.Succeeded ? 0 : 1;
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.build",
                    Success = result.Succeeded,
                    ExitCode = exitCode,
                    Error = result.Error,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.PowerShellCompilationBuildResult)
                });
                return exitCode;
            }

            if (!result.Succeeded)
            {
                logger.Error(result.Error ?? "PowerShell artifact build failed.");
                if (!string.IsNullOrWhiteSpace(result.BuildOutput)) logger.Error(result.BuildOutput);
                return exitCode;
            }

            logger.Success($"Built {kind}: {result.ArtifactPath}");
            logger.Info($"Manifest: {result.ManifestPath}");
            logger.Info(result.Manifest!.UsesPowerShellRuntimeFallback
                ? $"Runtime-packaged artifact: {result.Manifest.RuntimeFallbackUnits} unit(s) execute through PowerShell."
                : result.Manifest.RequiresPowerShellRuntime
                    ? $"Typed PowerShell binary module: {result.Manifest.CompiledMethods} cmdlet(s), no dynamic script fallback."
                    : $"Typed CLR artifact: {result.Manifest.CompiledMethods} method(s), {result.Manifest.OmittedUnits} unsupported unit(s) omitted, no PowerShell runtime dependency.");
            return 0;
        }
        catch (Exception ex)
        {
            return WritePowerShellError(outputJson, 1, ex.Message, logger, "powershell.build");
        }
    }

    private static int CommandPowerShellAnalyze(string[] args, bool outputJson, ILogger logger)
    {
        if (args.Any(IsHelpArgument))
        {
            WritePowerShellHelp(outputJson);
            return 0;
        }

        if (!TryValidatePowerShellArguments(
                args,
                new[] { "--path", "--mode", "--output" },
                new[] { "--no-recurse", "--json", "--output-json" },
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger);

        var path = TryGetOptionValue(args, "--path");
        if (string.IsNullOrWhiteSpace(path) && args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal))
            path = args[0];
        if (string.IsNullOrWhiteSpace(path))
            return WritePowerShellError(outputJson, 2, "A PowerShell file or directory path is required.", logger);

        var modeValue = TryGetOptionValue(args, "--mode") ?? nameof(PowerShellCompilationMode.Analyze);
        if (!Enum.TryParse<PowerShellCompilationMode>(modeValue, ignoreCase: true, out var mode) ||
            !Enum.IsDefined(typeof(PowerShellCompilationMode), mode))
            return WritePowerShellError(outputJson, 2, $"Unknown compilation mode '{modeValue}'.", logger);

        try
        {
            var recurse = !args.Any(static argument => argument.Equals("--no-recurse", StringComparison.OrdinalIgnoreCase));
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(path, mode, recurse));
            var exitCode = plan.CanProceed ? 0 : 1;
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.analyze",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(plan, CliJson.Context.PowerShellCompilationPlan)
                });
                return exitCode;
            }

            WritePowerShellPlan(plan, logger);
            return exitCode;
        }
        catch (Exception ex)
        {
            return WritePowerShellError(outputJson, 1, ex.Message, logger);
        }
    }

    private static void WritePowerShellPlan(PowerShellCompilationPlan plan, ILogger logger)
    {
        logger.Info($"PowerShell compilation plan ({plan.Mode}): {plan.CompilableUnits}/{plan.TotalUnits} units eligible ({plan.CompilationCoveragePercentage:0.0}%).");
        foreach (var file in plan.Files)
        {
            foreach (var diagnostic in file.Diagnostics)
                logger.Error(FormatPowerShellDiagnostic(file.RelativePath, diagnostic));

            foreach (var unit in file.Units)
            {
                var location = $"{file.RelativePath}:{unit.StartLine}";
                if (unit.IsCompilable)
                {
                    logger.Success($"COMPILE  {location}  {unit.Name}");
                    continue;
                }

                logger.Warn($"FALLBACK {location}  {unit.Name}");
                foreach (var diagnostic in unit.Diagnostics)
                    logger.Warn($"  {FormatPowerShellDiagnostic(file.RelativePath, diagnostic)}");
            }
        }

        if (!plan.CanProceed)
            logger.Error(plan.ParseErrorFiles > 0
                ? $"Planning failed because {plan.ParseErrorFiles} file(s) contain parser errors."
                : $"Strict mode rejected {plan.RuntimeFallbackUnits} runtime fallback unit(s).");
    }

    private static string FormatPowerShellDiagnostic(string relativePath, PowerShellCompilationDiagnostic diagnostic)
        => $"{relativePath}:{diagnostic.Line}:{diagnostic.Column} [{diagnostic.Code}] {diagnostic.Message}";

    private static int WritePowerShellError(bool outputJson, int exitCode, string error, ILogger logger, string command = "powershell.analyze")
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = command,
                Success = false,
                ExitCode = exitCode,
                Error = error
            });
        }
        else
        {
            logger.Error(error);
            if (exitCode == 2)
            {
                Console.WriteLine(PowerShellAnalyzeUsage);
                Console.WriteLine(PowerShellBuildUsage);
            }
        }

        return exitCode;
    }

    private static void WritePowerShellHelp(bool outputJson)
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "powershell",
                Success = true,
                ExitCode = 0,
                Result = JsonSerializer.SerializeToElement(new { analyzeUsage = PowerShellAnalyzeUsage, buildUsage = PowerShellBuildUsage })
            });
        }
        else
        {
            Console.WriteLine(PowerShellAnalyzeUsage);
            Console.WriteLine(PowerShellBuildUsage);
        }
    }

    private static bool TryParseArtifactKind(string? value, out PowerShellCompilationArtifactKind kind)
    {
        if (value is not null && (value.Equals("exe", StringComparison.OrdinalIgnoreCase) || value.Equals("executable", StringComparison.OrdinalIgnoreCase)))
        {
            kind = PowerShellCompilationArtifactKind.Executable;
            return true;
        }
        if (value is not null && (value.Equals("dll", StringComparison.OrdinalIgnoreCase) || value.Equals("module", StringComparison.OrdinalIgnoreCase) || value.Equals("binarymodule", StringComparison.OrdinalIgnoreCase)))
        {
            kind = PowerShellCompilationArtifactKind.BinaryModule;
            return true;
        }
        if (value is not null && (value.Equals("library", StringComparison.OrdinalIgnoreCase) || value.Equals("clr", StringComparison.OrdinalIgnoreCase)))
        {
            kind = PowerShellCompilationArtifactKind.Library;
            return true;
        }
        kind = default;
        return false;
    }

    private static bool TryValidatePowerShellArguments(
        string[] args,
        IEnumerable<string> valueOptions,
        IEnumerable<string> switchOptions,
        out string error)
    {
        var values = valueOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var switches = switchOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var positionalCount = 0;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (switches.Contains(argument)) continue;
            if (values.Contains(argument))
            {
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    error = $"PowerShell option '{argument}' requires a value.";
                    return false;
                }
                continue;
            }
            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                error = $"Unknown PowerShell option '{argument}'.";
                return false;
            }
            if (++positionalCount > 1)
            {
                error = $"Unexpected PowerShell argument '{argument}'.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsHelpArgument(string argument)
        => argument.Equals("-h", StringComparison.OrdinalIgnoreCase) || argument.Equals("--help", StringComparison.OrdinalIgnoreCase);
}
