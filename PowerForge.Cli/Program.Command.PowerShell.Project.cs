using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string PowerShellProjectUsage =
        "Usage: powerforge powershell project <init|analyze|explain|recommend|lock|restore|build|test|pack|install|diagnose> <project-or-source> [--project <powerforge.psproject.json>] [--name <name>] [--kind <exe|dll|library>] [--mode <Package|Hybrid|Strict>] [--semantic-profile <id>] [--framework <tfm>] [--rid <rid>] [--self-contained] [--optimization <None|Trimmed|NativeAot>] [--target <name> ...] [--boundary-profile <profile.json>] [--offline] [--output json]";

    private static int CommandPowerShellProject(string[] args, bool outputJson, ILogger logger)
    {
        if (args.Length == 0 || args.Any(IsHelpArgument))
        {
            WritePowerShellProjectHelp(outputJson);
            return 0;
        }
        var operation = args[0].ToLowerInvariant();
        var operationArgs = args.Skip(1).ToArray();
        if (operation == "init")
            return CommandPowerShellProjectInit(operationArgs, outputJson, logger);
        if (operation is not ("analyze" or "explain" or "recommend" or "lock" or "restore" or "build" or "test" or "pack" or "install" or "diagnose"))
            return WritePowerShellError(outputJson, 2, $"Unknown PowerShell project operation '{operation}'.", logger, "powershell.project");
        if (!TryValidatePowerShellArguments(
                operationArgs,
                new[] { "--project", "--target", "--boundary-profile", "--output" },
                new[] { "--offline", "--json", "--output-json" },
                out var positionalProject,
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.project." + operation);
        var projectPath = TryGetOptionValue(operationArgs, "--project") ?? positionalProject;
        if (string.IsNullOrWhiteSpace(projectPath))
            return WritePowerShellError(outputJson, 2, "A PowerShell compilation project path is required.", logger, "powershell.project." + operation);
        try
        {
            var service = new PowerShellCompilationProjectWorkflowService();
            var targets = GetOptionValues(operationArgs, "--target").ToArray();
            var result = operation switch
            {
                "analyze" => service.Analyze(projectPath, targets),
                "explain" => service.Explain(projectPath, targets),
                "recommend" => service.Recommend(projectPath, TryGetOptionValue(operationArgs, "--boundary-profile"), targets),
                "lock" => service.Lock(projectPath, targets),
                "restore" => service.Restore(projectPath, operationArgs.Any(static value => value.Equals("--offline", StringComparison.OrdinalIgnoreCase)), targets),
                "build" => service.Build(projectPath, targets),
                "test" => service.Test(projectPath, targets),
                "pack" => service.Pack(projectPath, targets),
                "install" => service.Install(projectPath, targets),
                "diagnose" => service.Diagnose(projectPath, targets),
                _ => throw new InvalidOperationException()
            };
            return WritePowerShellProjectResult(result, outputJson, logger);
        }
        catch (Exception exception)
        {
            return WritePowerShellError(outputJson, 1, exception.Message, logger, "powershell.project." + operation);
        }
    }

    private static int CommandPowerShellProjectInit(string[] args, bool outputJson, ILogger logger)
    {
        if (!TryValidatePowerShellArguments(
                args,
                new[] { "--project", "--name", "--kind", "--mode", "--semantic-profile", "--framework", "--rid", "--optimization", "--output" },
                new[] { "--self-contained", "--no-single-file", "--json", "--output-json" },
                out var sourcePath,
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.project.init");
        if (string.IsNullOrWhiteSpace(sourcePath))
            return WritePowerShellError(outputJson, 2, "A contained PowerShell source file or module directory is required.", logger, "powershell.project.init");
        try
        {
            var fullSource = Path.GetFullPath(sourcePath.Trim().Trim('"'));
            var projectPath = TryGetOptionValue(args, "--project") ?? Path.Combine(
                Directory.Exists(fullSource) ? fullSource : Path.GetDirectoryName(fullSource)!,
                "powerforge.psproject.json");
            projectPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
            if (File.Exists(projectPath)) throw new IOException($"Project manifest already exists: {projectPath}");
            var kindValue = TryGetOptionValue(args, "--kind");
            var defaultKind = Directory.Exists(fullSource) || Path.GetExtension(fullSource).Equals(".psm1", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(fullSource).Equals(".psd1", StringComparison.OrdinalIgnoreCase)
                ? PowerShellCompilationArtifactKind.BinaryModule
                : PowerShellCompilationArtifactKind.Executable;
            var kind = defaultKind;
            if (!string.IsNullOrWhiteSpace(kindValue) && !TryParseArtifactKind(kindValue, out kind))
                return WritePowerShellError(outputJson, 2, "Artifact kind must be 'exe', 'dll', or 'library'.", logger, "powershell.project.init");
            var modeValue = TryGetOptionValue(args, "--mode") ?? (kind == PowerShellCompilationArtifactKind.BinaryModule ? "Hybrid" : "Strict");
            if (!Enum.TryParse<PowerShellCompilationMode>(modeValue, true, out var mode) || !Enum.IsDefined(typeof(PowerShellCompilationMode), mode) || mode == PowerShellCompilationMode.Analyze)
                return WritePowerShellError(outputJson, 2, "Project mode must be Package, Hybrid, or Strict.", logger, "powershell.project.init");
            PowerShellCompilationBuildSpec.EnsureModeSupported(kind, mode);
            var optimizationValue = TryGetOptionValue(args, "--optimization") ?? "None";
            if (!Enum.TryParse<PowerShellCompilationExecutableOptimization>(optimizationValue, true, out var optimization) || !Enum.IsDefined(typeof(PowerShellCompilationExecutableOptimization), optimization))
                return WritePowerShellError(outputJson, 2, "Optimization must be None, Trimmed, or NativeAot.", logger, "powershell.project.init");
            var framework = TryGetOptionValue(args, "--framework") ?? (kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Strict ? "net10.0" : "net8.0");
            var selfContained = args.Any(static value => value.Equals("--self-contained", StringComparison.OrdinalIgnoreCase)) || optimization != PowerShellCompilationExecutableOptimization.None;
            var semanticProfileId = PowerShellCompilationSemanticOracleCatalog.Get(
                TryGetOptionValue(args, "--semantic-profile") ?? PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId).ProfileId;
            var target = PowerShellCompilationTargetContractService.Create(
                kind,
                mode,
                framework,
                TryGetOptionValue(args, "--rid"),
                selfContained,
                !args.Any(static value => value.Equals("--no-single-file", StringComparison.OrdinalIgnoreCase)),
                optimization,
                explicitContract: true,
                semanticProfileId: semanticProfileId);
            var projectName = TryGetOptionValue(args, "--name") ?? Path.GetFileNameWithoutExtension(Directory.Exists(fullSource) ? fullSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : fullSource);
            var manifestService = new PowerShellCompilationProjectManifestService();
            var manifest = manifestService.Create(projectPath, fullSource, projectName, target);
            manifestService.Save(projectPath, manifest);
            var result = new PowerShellCompilationProjectResult
            {
                Operation = "init",
                ProjectPath = projectPath,
                Succeeded = true,
                Targets = manifest.Artifacts.Select(artifact => new PowerShellCompilationProjectTargetResult
                {
                    Name = artifact.Name,
                    TargetContractSha256 = artifact.Target.ContractSha256,
                    Succeeded = true,
                    Message = "Portable project manifest created.",
                    Path = projectPath
                }).ToArray()
            };
            return WritePowerShellProjectResult(result, outputJson, logger);
        }
        catch (Exception exception)
        {
            return WritePowerShellError(outputJson, 1, exception.Message, logger, "powershell.project.init");
        }
    }

    private static int WritePowerShellProjectResult(PowerShellCompilationProjectResult result, bool outputJson, ILogger logger)
    {
        var exitCode = result.Succeeded ? 0 : 1;
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "powershell.project." + result.Operation,
                Success = result.Succeeded,
                ExitCode = exitCode,
                Result = CliJson.SerializeToElement(result, CliJson.Context.PowerShellCompilationProjectResult)
            });
            return exitCode;
        }
        foreach (var target in result.Targets)
        {
            var message = $"{target.Name}: {target.Message}";
            if (target.Succeeded) logger.Success(message); else logger.Error(message);
            if (!string.IsNullOrWhiteSpace(target.Path)) logger.Info(target.Path);
        }
        return exitCode;
    }

    private static void WritePowerShellProjectHelp(bool outputJson)
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "powershell.project",
                Success = true,
                ExitCode = 0,
                Result = JsonSerializer.SerializeToElement(new { usage = PowerShellProjectUsage })
            });
        }
        else Console.WriteLine(PowerShellProjectUsage);
    }
}
