using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerDeploy(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.deploy");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var manifest = loaded.Manifest;
        var manifestPath = loaded.ManifestPath!;
        var sshCommand = TryGetOptionValue(subArgs, "--ssh") ?? "ssh";
        var dryRun = HasOption(subArgs, "--dry-run");
        var failOnFailure = HasOption(subArgs, "--fail-on-failure");
        var target = BuildServerSshTarget(manifest.Target);
        var commandResults = new List<PowerForgeServerDeployCommandResult>();
        var warnings = new List<string>();

        var deployCommands = manifest.Deploy?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>();
        if (deployCommands.Length == 0)
            warnings.Add("No deploy commands are defined in the manifest.");

        foreach (var command in deployCommands)
        {
            var commandText = BuildDeployCommand(command);
            if (string.IsNullOrWhiteSpace(commandText))
            {
                warnings.Add($"Skipping deploy command '{command.Id}' because command text is empty.");
                continue;
            }

            if (command.Sensitive)
            {
                warnings.Add($"Skipping sensitive deploy command '{command.Id}'.");
                commandResults.Add(new PowerForgeServerDeployCommandResult
                {
                    Id = command.Id,
                    Command = command.Command,
                    WorkingDirectory = command.WorkingDirectory,
                    Required = command.Required,
                    Sensitive = command.Sensitive,
                    Skipped = true,
                    Success = !command.Required,
                    ErrorPreview = "Sensitive command skipped."
                });
                continue;
            }

            if (dryRun)
            {
                commandResults.Add(new PowerForgeServerDeployCommandResult
                {
                    Id = command.Id,
                    Command = command.Command,
                    WorkingDirectory = command.WorkingDirectory,
                    Required = command.Required,
                    Sensitive = command.Sensitive,
                    Skipped = true,
                    Success = true,
                    OutputPreview = commandText
                });
                continue;
            }

            var result = ExecuteRemote(sshCommand, target, commandText);
            commandResults.Add(new PowerForgeServerDeployCommandResult
            {
                Id = command.Id,
                Command = command.Command,
                WorkingDirectory = command.WorkingDirectory,
                Required = command.Required,
                Sensitive = command.Sensitive,
                ExitCode = result.ExitCode,
                Success = result.Success,
                OutputPreview = Preview(result.Stdout),
                ErrorPreview = Preview(result.Stderr)
            });
        }

        var failedCommands = commandResults
            .Where(static result => result.Required && !result.Success)
            .ToArray();
        if (failedCommands.Length > 0)
            warnings.Add($"{failedCommands.Length} required deploy command(s) failed.");

        var success = failedCommands.Length == 0;
        var resultSummary = new PowerForgeServerDeployResult
        {
            ManifestPath = manifestPath,
            Target = target,
            DryRun = dryRun,
            Success = success,
            Commands = commandResults.ToArray(),
            Warnings = warnings.ToArray()
        };

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.deploy",
                Success = success || !failOnFailure,
                ExitCode = success || !failOnFailure ? 0 : 1,
                Config = "web.serverrecovery",
                ConfigPath = manifestPath,
                Result = WebCliJson.SerializeToElement(resultSummary, WebCliJson.Context.PowerForgeServerDeployResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return success || !failOnFailure ? 0 : 1;
        }

        logger.Success(success ? (dryRun ? "Server deploy dry run completed." : "Server deploy completed.") : "Server deploy completed with failures.");
        logger.Info($"Target: {target}");
        logger.Info($"Commands: {commandResults.Count}");
        foreach (var result in commandResults.Where(static result => result.Skipped && !string.IsNullOrWhiteSpace(result.OutputPreview)))
            logger.Info($"dry-run {result.Id}: {result.OutputPreview}");
        foreach (var failure in failedCommands)
            logger.Warn($"command {failure.Id}: exit={failure.ExitCode}; error={failure.ErrorPreview}");
        foreach (var warning in warnings)
            logger.Warn(warning);

        return success || !failOnFailure ? 0 : 1;
    }

    private static string? BuildDeployCommand(PowerForgeServerNamedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Command))
            return null;
        return string.IsNullOrWhiteSpace(command.WorkingDirectory)
            ? command.Command
            : $"cd {ShellQuote(command.WorkingDirectory)} && {command.Command}";
    }
}
