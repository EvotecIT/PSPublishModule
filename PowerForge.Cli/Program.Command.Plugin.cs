using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string PluginExportUsage =
        "Usage: powerforge plugin export [--config <powerforge.plugins.json>] [--project-root <path>] [--group <Name[,Name...]>] [--framework <tfm>] [--configuration <Release|Debug>] [--output-root <path>] [--keep-symbols] [--plan] [--output json]";
    private const string PluginPackUsage =
        "Usage: powerforge plugin pack [--config <powerforge.plugins.json>] [--project-root <path>] [--group <Name[,Name...]>] [--configuration <Release|Debug>] [--output-root <path>] [--no-build] [--include-symbols] [--package-version <version>] [--version-suffix <suffix>] [--push] [--source <url|name>] [--api-key <token>] [--skip-duplicate] [--plan] [--output json]";

    private static int CommandPlugin(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Length == 0 || argv.Any(a => a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "plugin",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new { exportUsage = PluginExportUsage, packUsage = PluginPackUsage })
                });
            }
            else
            {
                Console.WriteLine(PluginExportUsage);
                Console.WriteLine(PluginPackUsage);
            }

            return 0;
        }

        var sub = argv[0].ToLowerInvariant();
        if (sub.Equals("export", StringComparison.OrdinalIgnoreCase))
            return CommandPluginExport(argv.Skip(1).ToArray(), outputJson, cli, logger);
        if (sub.Equals("pack", StringComparison.OrdinalIgnoreCase))
            return CommandPluginPack(argv.Skip(1).ToArray(), outputJson, cli, logger);

        return WritePluginError(outputJson, "plugin", 2, $"Unknown plugin subcommand '{argv[0]}'.", logger);
    }

    private static int CommandPluginExport(string[] subArgs, bool outputJson, CliOptions cli, ILogger logger)
    {
        if (subArgs.Any(a => a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "plugin.export",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new { usage = PluginExportUsage })
                });
            }
            else
            {
                Console.WriteLine(PluginExportUsage);
            }

            return 0;
        }

        var planOnly = subArgs.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase) || a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            configPath = FindDefaultPluginCatalogConfig(Directory.GetCurrentDirectory());

        if (string.IsNullOrWhiteSpace(configPath))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "plugin.export",
                    Success = false,
                    ExitCode = 2,
                    Error = "Missing --config and no default plugin catalog config found."
                });
                return 2;
            }

            Console.WriteLine(PluginExportUsage);
            return 2;
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var loaded = LoadPowerForgePluginCatalogSpecWithPath(configPath);
            var spec = loaded.Value;
            var fullConfigPath = loaded.FullPath;

            var projectRoot = TryGetOptionValue(subArgs, "--project-root");
            if (!string.IsNullOrWhiteSpace(projectRoot))
                spec.ProjectRoot = Path.GetFullPath(projectRoot.Trim().Trim('"'));

            var request = new PowerForgePluginCatalogRequest
            {
                Groups = ParseCsvOptionValues(subArgs, "--group", "--groups"),
                PreferredFramework = TryGetOptionValue(subArgs, "--framework"),
                Configuration = TryGetOptionValue(subArgs, "--configuration"),
                OutputRoot = TryGetOptionValue(subArgs, "--output-root") ?? TryGetOptionValue(subArgs, "--out"),
                IncludeSymbols = subArgs.Any(a => a.Equals("--keep-symbols", StringComparison.OrdinalIgnoreCase))
            };

            var service = new PowerForgePluginCatalogService(cmdLogger);
            var plan = service.PlanFolderExport(spec, fullConfigPath, request);
            var result = planOnly
                ? null
                : RunWithStatus(outputJson, cli, "Exporting plugin folders", () => service.ExportFolders(plan));
            var exitCode = result is not null && !result.Success ? 1 : 0;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "plugin.export",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Error = exitCode == 0 ? null : result?.ErrorMessage,
                    Config = "plugins",
                    ConfigPath = fullConfigPath,
                    Spec = CliJson.SerializeToElement(spec, CliJson.Context.PowerForgePluginCatalogSpec),
                    Plan = CliJson.SerializeToElement(plan, CliJson.Context.PowerForgePluginFolderExportPlan),
                    Result = result is null ? null : CliJson.SerializeToElement(result, CliJson.Context.PowerForgePluginFolderExportResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            if (planOnly)
            {
                cmdLogger.Success($"Planned plugin export ({plan.Entries.Length} plugin(s)).");
                cmdLogger.Info($"Project root: {plan.ProjectRoot}");
                cmdLogger.Info($"Output root: {plan.OutputRoot}");
                return 0;
            }

            if (result is null)
            {
                cmdLogger.Error("Plugin export failed (no result).");
                return 1;
            }

            if (!result.Success)
            {
                cmdLogger.Error(result.ErrorMessage ?? "Plugin export failed.");
                return 1;
            }

            cmdLogger.Success($"Plugin export completed ({result.Entries.Length} plugin(s)).");
            foreach (var entry in result.Entries ?? Array.Empty<PowerForgePluginFolderExportEntryResult>())
            {
                cmdLogger.Info($" -> {entry.PackageId} ({entry.Framework}): {entry.OutputPath}");
                if (!string.IsNullOrWhiteSpace(entry.ManifestPath))
                    cmdLogger.Info($"    manifest: {entry.ManifestPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            return WritePluginError(outputJson, "plugin.export", 1, ex.Message, logger);
        }
    }

    private static int CommandPluginPack(string[] subArgs, bool outputJson, CliOptions cli, ILogger logger)
    {
        if (subArgs.Any(a => a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "plugin.pack",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new { usage = PluginPackUsage })
                });
            }
            else
            {
                Console.WriteLine(PluginPackUsage);
            }

            return 0;
        }

        var planOnly = subArgs.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase) || a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            configPath = FindDefaultPluginCatalogConfig(Directory.GetCurrentDirectory());

        if (string.IsNullOrWhiteSpace(configPath))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "plugin.export",
                    Success = false,
                    ExitCode = 2,
                    Error = "Missing --config and no default plugin catalog config found."
                });
                return 2;
            }

            Console.WriteLine(PluginPackUsage);
            return 2;
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var loaded = LoadPowerForgePluginCatalogSpecWithPath(configPath);
            var spec = loaded.Value;
            var fullConfigPath = loaded.FullPath;

            var projectRoot = TryGetOptionValue(subArgs, "--project-root");
            if (!string.IsNullOrWhiteSpace(projectRoot))
                spec.ProjectRoot = Path.GetFullPath(projectRoot.Trim().Trim('"'));

            var request = new PowerForgePluginPackageRequest
            {
                Groups = ParseCsvOptionValues(subArgs, "--group", "--groups"),
                Configuration = TryGetOptionValue(subArgs, "--configuration"),
                OutputRoot = TryGetOptionValue(subArgs, "--output-root") ?? TryGetOptionValue(subArgs, "--out"),
                NoBuild = subArgs.Any(a => a.Equals("--no-build", StringComparison.OrdinalIgnoreCase)),
                IncludeSymbols = subArgs.Any(a => a.Equals("--include-symbols", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("--keep-symbols", StringComparison.OrdinalIgnoreCase)),
                PackageVersion = TryGetOptionValue(subArgs, "--package-version"),
                VersionSuffix = TryGetOptionValue(subArgs, "--version-suffix"),
                PushPackages = subArgs.Any(a => a.Equals("--push", StringComparison.OrdinalIgnoreCase)),
                PushSource = TryGetOptionValue(subArgs, "--source"),
                ApiKey = TryGetOptionValue(subArgs, "--api-key"),
                SkipDuplicate = true
            };

            var service = new PowerForgePluginCatalogService(cmdLogger);
            var plan = service.PlanPackages(spec, fullConfigPath, request);
            var result = planOnly
                ? null
                : RunWithStatus(outputJson, cli, "Packing plugin packages", () => service.PackPackages(plan, request.ApiKey));
            var exitCode = result is not null && !result.Success ? 1 : 0;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "plugin.pack",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Error = exitCode == 0 ? null : result?.ErrorMessage,
                    Config = "plugins",
                    ConfigPath = fullConfigPath,
                    Spec = CliJson.SerializeToElement(spec, CliJson.Context.PowerForgePluginCatalogSpec),
                    Plan = CliJson.SerializeToElement(plan, CliJson.Context.PowerForgePluginPackagePlan),
                    Result = result is null ? null : CliJson.SerializeToElement(result, CliJson.Context.PowerForgePluginPackageResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            if (planOnly)
            {
                cmdLogger.Success($"Planned plugin pack ({plan.Entries.Length} package(s)).");
                cmdLogger.Info($"Project root: {plan.ProjectRoot}");
                cmdLogger.Info($"Output root: {plan.OutputRoot}");
                return 0;
            }

            if (result is null)
            {
                cmdLogger.Error("Plugin pack failed (no result).");
                return 1;
            }

            if (!result.Success)
            {
                cmdLogger.Error(result.ErrorMessage ?? "Plugin pack failed.");
                return 1;
            }

            cmdLogger.Success($"Plugin pack completed ({result.Entries.Length} package(s)).");
            foreach (var entry in result.Entries ?? Array.Empty<PowerForgePluginPackageEntryResult>())
            {
                foreach (var packagePath in entry.PackagePaths ?? Array.Empty<string>())
                    cmdLogger.Info($" -> {entry.PackageId}: {packagePath}");
                foreach (var symbolPath in entry.SymbolPackagePaths ?? Array.Empty<string>())
                    cmdLogger.Info($"    symbols: {symbolPath}");
                foreach (var push in entry.PushResults ?? Array.Empty<DotNetNuGetPushResult>())
                    cmdLogger.Info($"    push: {(push.ExitCode == 0 && !push.TimedOut ? "ok" : "failed")} {push.Executable}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            return WritePluginError(outputJson, "plugin.pack", 1, ex.Message, logger);
        }
    }

    private static int WritePluginError(bool outputJson, string command, int exitCode, string error, ILogger logger)
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
            return exitCode;
        }

        logger.Error(error);
        Console.WriteLine(PluginExportUsage);
        Console.WriteLine(PluginPackUsage);
        return exitCode;
    }
}
