using PowerForge;
using PowerForge.Cli;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private const string WorkspaceUsage =
        "Usage: powerforge workspace validate [--config <workspace.validation.json>] [--list] [--profile <name>] [--configuration <Release|Debug>] [--enable-feature <name[,name...]>] [--disable-feature <name[,name...]>] [--testimox-root <path>] [--variable <name=value>] [--plan] [--validate] [--output json]";

    private static int CommandWorkspace(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        if (argv.Length == 0 || argv[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(WorkspaceUsage);
            return 2;
        }

        var sub = argv[0].ToLowerInvariant();
        if (!string.Equals(sub, "validate", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(WorkspaceUsage);
            return 2;
        }

        var subArgs = argv.Skip(1).ToArray();
        var outputJson = IsJsonOutput(subArgs);
        var listOnly = subArgs.Any(a => a.Equals("--list", StringComparison.OrdinalIgnoreCase) || a.Equals("--ls", StringComparison.OrdinalIgnoreCase));
        var planOnly = subArgs.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase) || a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        var validateOnly = subArgs.Any(a => a.Equals("--validate", StringComparison.OrdinalIgnoreCase));
        var profileName = TryGetOptionValue(subArgs, "--profile");
        var configuration = TryGetOptionValue(subArgs, "--configuration") ?? "Release";
        var testimoXRoot = TryGetOptionValue(subArgs, "--testimox-root") ?? TryGetOptionValue(subArgs, "--testimoX-root");
        var enabledFeatures = ParseCsvOptionValues(subArgs, "--enable-feature");
        var disabledFeatures = ParseCsvOptionValues(subArgs, "--disable-feature");
        var variables = ParseKeyValueOptions(subArgs, "--variable");

        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
        {
            var baseDir = TryGetProjectRoot(subArgs);
            if (!string.IsNullOrWhiteSpace(baseDir))
                baseDir = Path.GetFullPath(baseDir.Trim().Trim('"'));
            else
                baseDir = Directory.GetCurrentDirectory();

            configPath = FindDefaultWorkspaceValidationConfig(baseDir);
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "workspace.validate",
                    Success = false,
                    ExitCode = 2,
                    Error = "Missing --config and no default workspace validation config found."
                });
                return 2;
            }

            Console.WriteLine(WorkspaceUsage);
            return 2;
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var loaded = LoadWorkspaceValidationSpecWithPath(configPath);
            var service = new WorkspaceValidationService();
            var profileList = service.ListProfiles(loaded.Value);

            if (listOnly)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "workspace.validate.list",
                        Success = true,
                        ExitCode = 0,
                        Config = "workspacevalidation",
                        ConfigPath = loaded.FullPath,
                        Spec = CliJson.SerializeToElement(loaded.Value, CliJson.Context.WorkspaceValidationSpec),
                        Results = CliJson.SerializeToElement(profileList, CliJson.Context.WorkspaceValidationProfileSummaryArray),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                cmdLogger.Info($"Workspace validation: {loaded.FullPath}");
                foreach (var profile in profileList)
                {
                    cmdLogger.Success(profile.Name);
                    if (!string.IsNullOrWhiteSpace(profile.Description))
                        cmdLogger.Info($"  {profile.Description}");
                    if (profile.Features.Length > 0)
                        cmdLogger.Info($"  Features: {string.Join(", ", profile.Features)}");
                }
                return 0;
            }

            var request = new WorkspaceValidationRequest
            {
                ProfileName = string.IsNullOrWhiteSpace(profileName) ? "default" : profileName!,
                Configuration = configuration,
                TestimoXRoot = testimoXRoot,
                EnabledFeatures = enabledFeatures,
                DisabledFeatures = disabledFeatures,
                Variables = variables,
                CaptureOutput = outputJson,
                CaptureError = outputJson
            };

            var plan = service.Plan(loaded.Value, loaded.FullPath, request);
            if (validateOnly)
            {
                var errors = service.Validate(loaded.Value, loaded.FullPath, request);
                var validateExitCode = errors.Length == 0 ? 0 : 2;

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "workspace.validate",
                        Success = validateExitCode == 0,
                        ExitCode = validateExitCode,
                        Error = validateExitCode == 0 ? null : string.Join("\n", errors),
                        Config = "workspacevalidation",
                        ConfigPath = loaded.FullPath,
                        Spec = CliJson.SerializeToElement(loaded.Value, CliJson.Context.WorkspaceValidationSpec),
                        Plan = CliJson.SerializeToElement(plan, CliJson.Context.WorkspaceValidationPlan),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return validateExitCode;
                }

                if (validateExitCode == 0)
                {
                    cmdLogger.Success($"Workspace validation config is valid ({plan.Steps.Length} step(s)).");
                    return 0;
                }

                cmdLogger.Error("Workspace validation config failed validation.");
                foreach (var error in errors)
                    cmdLogger.Error(error);
                return validateExitCode;
            }

            if (planOnly)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "workspace.validate.plan",
                        Success = true,
                        ExitCode = 0,
                        Config = "workspacevalidation",
                        ConfigPath = loaded.FullPath,
                        Spec = CliJson.SerializeToElement(loaded.Value, CliJson.Context.WorkspaceValidationSpec),
                        Plan = CliJson.SerializeToElement(plan, CliJson.Context.WorkspaceValidationPlan),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                cmdLogger.Success($"Planned workspace validation ({plan.Steps.Length} step(s)).");
                cmdLogger.Info($"Profile: {plan.ProfileName}");
                cmdLogger.Info($"Features: {string.Join(", ", plan.ActiveFeatures)}");
                foreach (var step in plan.Steps)
                    cmdLogger.Info($" -> {step.Name}: {step.DisplayCommand}");
                return 0;
            }

            var result = RunWithStatus(outputJson, cli, "Running workspace validation", () =>
                service.RunAsync(loaded.Value, loaded.FullPath, request).GetAwaiter().GetResult());
            var exitCode = result.Succeeded ? 0 : 1;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "workspace.validate",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Error = exitCode == 0 ? null : result.ErrorMessage,
                    Config = "workspacevalidation",
                    ConfigPath = loaded.FullPath,
                    Spec = CliJson.SerializeToElement(loaded.Value, CliJson.Context.WorkspaceValidationSpec),
                    Plan = CliJson.SerializeToElement(result.Plan, CliJson.Context.WorkspaceValidationPlan),
                    Result = CliJson.SerializeToElement(result, CliJson.Context.WorkspaceValidationResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            if (exitCode != 0)
            {
                cmdLogger.Error(result.ErrorMessage ?? "Workspace validation failed.");
                return exitCode;
            }

            cmdLogger.Success($"Workspace validation completed ({result.Steps.Length} step(s)).");
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "workspace.validate",
                    Success = false,
                    ExitCode = 1,
                    Error = ex.Message
                });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
        }
    }
}
