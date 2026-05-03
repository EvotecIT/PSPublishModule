using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private const string SupportedServerActions = "inspect, plan, capture, deploy, verify, bootstrap-plan, restore-secrets-plan";

    internal static int HandleServer(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        if (subArgs.Length == 0)
            return Fail($"Missing server action. Supported actions: {SupportedServerActions}.", outputJson, logger, "web.server");

        var action = subArgs[0].ToLowerInvariant();
        var actionArgs = subArgs.Skip(1).ToArray();

        return action switch
        {
            "inspect" => HandleServerInspect(actionArgs, outputJson, logger, outputSchemaVersion),
            "plan" => HandleServerPlan(actionArgs, outputJson, logger, outputSchemaVersion),
            "validate" => HandleServerPlan(actionArgs, outputJson, logger, outputSchemaVersion),
            "capture" => HandleServerCapture(actionArgs, outputJson, logger, outputSchemaVersion),
            "verify" => HandleServerVerify(actionArgs, outputJson, logger, outputSchemaVersion),
            "deploy" => HandleServerDeploy(actionArgs, outputJson, logger, outputSchemaVersion),
            "bootstrap-plan" => HandleServerBootstrapPlan(actionArgs, outputJson, logger, outputSchemaVersion),
            "restore-secrets-plan" => HandleServerRestoreSecretsPlan(actionArgs, outputJson, logger, outputSchemaVersion),
            _ => Fail($"Unknown server action '{subArgs[0]}'. Supported actions: {SupportedServerActions}.", outputJson, logger, "web.server")
        };
    }

    private static int HandleServerPlan(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.plan");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var fullManifestPath = loaded.ManifestPath!;
        var manifest = loaded.Manifest;

        var warnings = new List<string>();
        if (manifest.SchemaVersion <= 0)
            warnings.Add("schemaVersion should be greater than zero.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            warnings.Add("name is missing.");
        if (manifest.Target is null)
            warnings.Add("target is missing.");
        if (manifest.Repositories is null || manifest.Repositories.Length == 0)
            warnings.Add("No repositories are defined.");
        if (manifest.Secrets?.Any(secret => secret.Capture.Equals("encrypted", StringComparison.OrdinalIgnoreCase)) == true &&
            string.IsNullOrWhiteSpace(manifest.BackupTarget?.RecipientEnv) &&
            string.IsNullOrWhiteSpace(manifest.BackupTarget?.Recipient))
        {
            warnings.Add("Encrypted secret capture is configured but backupTarget.recipient and backupTarget.recipientEnv are missing.");
        }
        else if (string.IsNullOrWhiteSpace(manifest.BackupTarget?.Recipient) &&
                 !string.IsNullOrWhiteSpace(manifest.BackupTarget?.RecipientEnv) &&
                 string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(manifest.BackupTarget.RecipientEnv)))
        {
            warnings.Add($"Encryption recipient environment variable '{manifest.BackupTarget.RecipientEnv}' is not set.");
        }

        var stages = BuildServerRecoveryStages(manifest);
        var result = new PowerForgeServerRecoveryPlanResult
        {
            ManifestPath = fullManifestPath,
            Name = manifest.Name,
            TargetHost = manifest.Target?.Host,
            SshAlias = manifest.Target?.SshAlias,
            SshPort = manifest.Target?.SshPort,
            RepositoryCount = manifest.Repositories?.Length ?? 0,
            PackageCount = manifest.Packages?.Apt?.Length ?? 0,
            ApacheModuleCount = manifest.Packages?.ApacheModules?.Length ?? manifest.Apache?.Modules?.Length ?? 0,
            SystemdServiceCount = manifest.Systemd?.Services?.Length ?? 0,
            SystemdTimerCount = manifest.Systemd?.Timers?.Length ?? 0,
            CertificateCount = manifest.Certificates?.Length ?? 0,
            PlainCaptureCount = manifest.Capture?.PlainFiles?.Length ?? 0,
            EncryptedCaptureCount = manifest.Capture?.EncryptedFiles?.Length ?? 0,
            SecretCount = manifest.Secrets?.Length ?? 0,
            BackupTarget = manifest.BackupTarget?.Repository,
            BackupEncryption = manifest.BackupTarget?.Encryption,
            Stages = stages,
            Warnings = warnings.ToArray()
        };

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.plan",
                Success = true,
                ExitCode = 0,
                Config = "web.serverrecovery",
                ConfigPath = fullManifestPath,
                Spec = WebCliJson.SerializeToElement(manifest, WebCliJson.Context.PowerForgeServerRecoveryManifest),
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.PowerForgeServerRecoveryPlanResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return 0;
        }

        logger.Success("Server recovery manifest loaded.");
        logger.Info($"Manifest: {fullManifestPath}");
        logger.Info($"Target: {manifest.Target?.SshAlias ?? manifest.Target?.Host ?? "(unknown)"} port {manifest.Target?.SshPort?.ToString() ?? "(default)"}");
        logger.Info($"Repositories: {result.RepositoryCount}; packages: {result.PackageCount}; services: {result.SystemdServiceCount}; timers: {result.SystemdTimerCount}");
        logger.Info($"Certificates: {result.CertificateCount}; plain captures: {result.PlainCaptureCount}; encrypted captures: {result.EncryptedCaptureCount}; secrets: {result.SecretCount}");
        if (!string.IsNullOrWhiteSpace(result.BackupTarget))
            logger.Info($"Backup target: {result.BackupTarget} ({result.BackupEncryption ?? "no encryption configured"})");

        logger.Info("Recovery stages:");
        foreach (var stage in stages)
            logger.Info($"- {stage}");

        foreach (var warning in warnings)
            logger.Warn(warning);

        return 0;
    }

    private static int HandleServerCapture(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.capture");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var manifest = loaded.Manifest;
        var manifestPath = loaded.ManifestPath!;
        var outPathArg = TryGetOptionValue(subArgs, "--out") ??
                         TryGetOptionValue(subArgs, "--output") ??
                         TryGetOptionValue(subArgs, "--output-dir");
        var dryRun = HasOption(subArgs, "--dry-run");
        var skipFiles = HasOption(subArgs, "--skip-files");
        var skipEncrypted = HasOption(subArgs, "--skip-encrypted");
        var encryptRemote = HasOption(subArgs, "--encrypt-remote") || HasOption(subArgs, "--remote-encryption");
        var sshCommand = TryGetOptionValue(subArgs, "--ssh") ?? "ssh";
        var ageCommand = TryGetOptionValue(subArgs, "--age") ?? "age";

        var outputRoot = ResolveCaptureOutputPath(outPathArg, manifest);
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(Path.Combine(outputRoot, "commands"));

        var warnings = new List<string>();
        var commandResults = new List<PowerForgeServerCaptureCommandResult>();
        var copiedManifestPath = Path.Combine(outputRoot, "manifest.json");
        File.Copy(manifestPath, copiedManifestPath, overwrite: true);

        var commandList = manifest.Capture?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>();
        var plainFiles = manifest.Capture?.PlainFiles ?? Array.Empty<PowerForgeServerManagedFile>();
        var encryptedFiles = manifest.Capture?.EncryptedFiles ?? Array.Empty<PowerForgeServerManagedFile>();
        var target = BuildServerSshTarget(manifest.Target);
        var plainArchivePath = dryRun || skipFiles || plainFiles.Length == 0
            ? null
            : Path.Combine(outputRoot, "plain-files.tar.gz");
        string? encryptedArchivePath = null;

        if (dryRun)
        {
            warnings.Add("Dry run requested; no SSH commands were executed.");
        }
        else
        {
            foreach (var command in commandList.Where(static command => !command.Sensitive))
            {
                var result = CaptureRemoteCommand(
                    sshCommand,
                    target,
                    command,
                    Path.Combine(outputRoot, "commands"));
                commandResults.Add(result);
                if (!result.Success && command.Required)
                    warnings.Add($"Required capture command '{result.Id}' failed with exit code {result.ExitCode}.");
            }

            if (!skipFiles && plainFiles.Length > 0 && plainArchivePath is not null)
            {
                var archiveResult = CaptureRemoteTarArchive(sshCommand, target, plainFiles, plainArchivePath);
                if (!archiveResult.Success)
                {
                    warnings.Add($"Plain file archive failed with exit code {archiveResult.ExitCode}.");
                    if (!string.IsNullOrWhiteSpace(archiveResult.Stderr))
                        File.WriteAllText(Path.Combine(outputRoot, "plain-files.stderr.txt"), archiveResult.Stderr);
                }
            }

            if (!skipEncrypted && encryptedFiles.Length > 0)
            {
                var recipientEnv = manifest.BackupTarget?.RecipientEnv;
                var recipient = ResolveBackupRecipient(manifest);

                if (string.IsNullOrWhiteSpace(recipientEnv) && string.IsNullOrWhiteSpace(manifest.BackupTarget?.Recipient))
                {
                    warnings.Add("Encrypted capture skipped: backupTarget.recipient and backupTarget.recipientEnv are not configured.");
                }
                else if (string.IsNullOrWhiteSpace(recipient))
                {
                    warnings.Add($"Encrypted capture skipped: environment variable '{recipientEnv}' is not set.");
                }
                else
                {
                    encryptedArchivePath = Path.Combine(outputRoot, "encrypted-secrets.tar.gz.age");
                    var encryptedResult = encryptRemote
                        ? CaptureRemoteEncryptedTarArchive(
                            sshCommand,
                            target,
                            encryptedFiles,
                            encryptedArchivePath,
                            recipient)
                        : CaptureEncryptedRemoteTarArchive(
                            sshCommand,
                            ageCommand,
                            target,
                            encryptedFiles,
                            encryptedArchivePath,
                            recipient);
                    if (!encryptedResult.Success)
                    {
                        warnings.Add($"Encrypted capture failed with exit code {encryptedResult.ExitCode}.");
                        if (!string.IsNullOrWhiteSpace(encryptedResult.Stderr))
                            File.WriteAllText(Path.Combine(outputRoot, "encrypted-secrets.stderr.txt"), encryptedResult.Stderr);
                    }
                }
            }
        }

        var checklistPath = Path.Combine(outputRoot, "restore-checklist.md");
        WriteRestoreChecklist(checklistPath, manifest, plainArchivePath, encryptedArchivePath, warnings);

        var resultSummary = new PowerForgeServerCaptureResult
        {
            ManifestPath = manifestPath,
            OutputPath = outputRoot,
            PlainArchivePath = plainArchivePath,
            EncryptedArchivePath = encryptedArchivePath,
            RestoreChecklistPath = checklistPath,
            CommandResults = commandResults.ToArray(),
            Warnings = warnings.ToArray()
        };

        File.WriteAllText(
            Path.Combine(outputRoot, "capture-summary.json"),
            JsonSerializer.Serialize(resultSummary, WebCliJson.Context.PowerForgeServerCaptureResult));

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.capture",
                Success = true,
                ExitCode = 0,
                Config = "web.serverrecovery",
                ConfigPath = manifestPath,
                Result = WebCliJson.SerializeToElement(resultSummary, WebCliJson.Context.PowerForgeServerCaptureResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return 0;
        }

        logger.Success(dryRun ? "Server capture dry run completed." : "Server capture completed.");
        logger.Info($"Output: {outputRoot}");
        logger.Info($"Command captures: {commandResults.Count}");
        if (!string.IsNullOrWhiteSpace(plainArchivePath))
            logger.Info($"Plain archive: {plainArchivePath}");
        if (!string.IsNullOrWhiteSpace(encryptedArchivePath))
            logger.Info($"Encrypted archive: {encryptedArchivePath}");
        logger.Info($"Restore checklist: {checklistPath}");
        foreach (var warning in warnings)
            logger.Warn(warning);

        return 0;
    }

    private static (PowerForgeServerRecoveryManifest? Manifest, string? ManifestPath, int ExitCode) LoadServerRecoveryManifest(
        string[] subArgs,
        bool outputJson,
        WebConsoleLogger logger,
        string commandName)
    {
        var manifestPath = TryGetOptionValue(subArgs, "--manifest") ??
                           TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(manifestPath))
            return (null, null, Fail("Missing required --manifest.", outputJson, logger, commandName));

        try
        {
            var fullManifestPath = ResolveExistingFilePath(manifestPath);
            var manifest = JsonSerializer.Deserialize<PowerForgeServerRecoveryManifest>(
                File.ReadAllText(fullManifestPath), WebCliJson.Options);
            if (manifest is null)
                return (null, null, Fail("Invalid server recovery manifest.", outputJson, logger, commandName));

            return (manifest, fullManifestPath, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return (null, null, Fail($"Failed to load server recovery manifest: {ex.Message}", outputJson, logger, commandName));
        }
    }

    private static string? ResolveBackupRecipient(PowerForgeServerRecoveryManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.BackupTarget?.Recipient))
            return manifest.BackupTarget.Recipient;
        return string.IsNullOrWhiteSpace(manifest.BackupTarget?.RecipientEnv)
            ? null
            : Environment.GetEnvironmentVariable(manifest.BackupTarget.RecipientEnv);
    }

    private static string ResolveCaptureOutputPath(string? outPathArg, PowerForgeServerRecoveryManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(outPathArg))
            return Path.GetFullPath(outPathArg);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var name = SanitizeFileName(string.IsNullOrWhiteSpace(manifest.Name) ? "server" : manifest.Name);
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "_server-state", name, timestamp));
    }

    private static string BuildServerSshTarget(PowerForgeServerTarget? target)
    {
        if (!string.IsNullOrWhiteSpace(target?.SshAlias))
            return target.SshAlias!;
        if (string.IsNullOrWhiteSpace(target?.Host))
            throw new InvalidOperationException("Server recovery target requires target.sshAlias or target.host.");
        return string.IsNullOrWhiteSpace(target.User) ? target.Host! : $"{target.User}@{target.Host}";
    }

    private static PowerForgeServerCaptureCommandResult CaptureRemoteCommand(
        string sshCommand,
        string target,
        PowerForgeServerNamedCommand command,
        string commandOutputDirectory)
    {
        var id = SanitizeFileName(string.IsNullOrWhiteSpace(command.Id) ? "command" : command.Id);
        var stdoutPath = Path.Combine(commandOutputDirectory, $"{id}.out.txt");
        var stderrPath = Path.Combine(commandOutputDirectory, $"{id}.err.txt");
        var execution = RunProcessCaptureText(sshCommand, BuildSshArguments(target, command.Command ?? string.Empty));

        File.WriteAllText(stdoutPath, execution.Stdout);
        File.WriteAllText(stderrPath, execution.Stderr);

        return new PowerForgeServerCaptureCommandResult
        {
            Id = command.Id,
            Command = command.Command,
            ExitCode = execution.ExitCode,
            Success = execution.ExitCode == 0,
            StdoutPath = stdoutPath,
            StderrPath = stderrPath
        };
    }

    private static ProcessResult CaptureRemoteTarArchive(
        string sshCommand,
        string target,
        PowerForgeServerManagedFile[] files,
        string outputPath)
    {
        var script = BuildRemoteTarScript(files);
        return RunProcessCaptureBinary(sshCommand, BuildSshArguments(target, script), outputPath);
    }

    private static ProcessResult CaptureEncryptedRemoteTarArchive(
        string sshCommand,
        string ageCommand,
        string target,
        PowerForgeServerManagedFile[] files,
        string outputPath,
        string recipient)
    {
        var script = BuildRemoteTarScript(files);
        using var ssh = CreateProcess(sshCommand, BuildSshArguments(target, script));
        using var age = CreateProcess(ageCommand, new[] { "-r", recipient, "-o", outputPath });
        ssh.StartInfo.RedirectStandardOutput = true;
        ssh.StartInfo.RedirectStandardError = true;
        age.StartInfo.RedirectStandardInput = true;
        age.StartInfo.RedirectStandardError = true;

        if (!ssh.Start())
            throw new InvalidOperationException("Failed to start ssh.");
        if (!age.Start())
            throw new InvalidOperationException("Failed to start age.");

        var copyTask = ssh.StandardOutput.BaseStream.CopyToAsync(age.StandardInput.BaseStream)
            .ContinueWith(task =>
            {
                age.StandardInput.Close();
                if (task.IsFaulted && task.Exception is not null)
                    throw task.Exception;
            });
        var sshErrTask = ssh.StandardError.ReadToEndAsync();
        var ageErrTask = age.StandardError.ReadToEndAsync();

        ssh.WaitForExit();
        copyTask.GetAwaiter().GetResult();
        age.WaitForExit();

        var stderr = string.Concat(sshErrTask.GetAwaiter().GetResult(), ageErrTask.GetAwaiter().GetResult());
        return new ProcessResult
        {
            ExitCode = ssh.ExitCode == 0 ? age.ExitCode : ssh.ExitCode,
            Stdout = string.Empty,
            Stderr = stderr
        };
    }

    private static ProcessResult CaptureRemoteEncryptedTarArchive(
        string sshCommand,
        string target,
        PowerForgeServerManagedFile[] files,
        string outputPath,
        string recipient)
    {
        var script = BuildRemoteEncryptedTarScript(files, recipient);
        return RunProcessCaptureBinary(sshCommand, BuildSshArguments(target, script), outputPath);
    }

    private static string BuildRemoteTarScript(PowerForgeServerManagedFile[] files)
    {
        var paths = files
            .Select(static file => file.Target)
            .Where(static target => !string.IsNullOrWhiteSpace(target))
            .Select(static target => target!)
            .ToArray();
        if (paths.Length == 0)
            throw new InvalidOperationException("No target paths are defined for capture.");

        foreach (var path in paths)
        {
            if (path.Any(static c => !(char.IsLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or '*' or '?' or '[' or ']')))
                throw new InvalidOperationException($"Capture path contains unsupported characters: {path}");
        }

        return "set -e; sudo -n tar -czf - --ignore-failed-read " + string.Join(' ', paths);
    }

    private static string BuildRemoteEncryptedTarScript(PowerForgeServerManagedFile[] files, string recipient)
    {
        var tarScript = BuildRemoteTarScript(files);
        return $"{tarScript} | age -r {ShellQuote(recipient)} -o -";
    }

    private static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string[] BuildSshArguments(string target, string command)
        => new[] { "-o", "ConnectTimeout=30", target, $"sh -lc {ShellQuote(command)}" };

    private static ProcessResult RunProcessCaptureText(string fileName, IReadOnlyList<string> args)
    {
        using var process = CreateProcess(fileName, args);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdoutTask.GetAwaiter().GetResult(),
            Stderr = stderrTask.GetAwaiter().GetResult()
        };
    }

    private static ProcessResult RunProcessCaptureBinary(string fileName, IReadOnlyList<string> args, string stdoutPath)
    {
        using var process = CreateProcess(fileName, args);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        using var output = File.Create(stdoutPath);
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        copyTask.GetAwaiter().GetResult();
        return new ProcessResult { ExitCode = process.ExitCode, Stdout = string.Empty, Stderr = stderrTask.GetAwaiter().GetResult() };
    }

    private static Process CreateProcess(string fileName, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        return new Process { StartInfo = startInfo };
    }

    private static void WriteRestoreChecklist(
        string path,
        PowerForgeServerRecoveryManifest manifest,
        string? plainArchivePath,
        string? encryptedArchivePath,
        IReadOnlyList<string> warnings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# PowerForge Server Restore Checklist");
        builder.AppendLine();
        builder.AppendLine($"Manifest: `{manifest.Name}`");
        builder.AppendLine($"Target: `{manifest.Target?.SshAlias ?? manifest.Target?.Host ?? "unknown"}`");
        builder.AppendLine();
        builder.AppendLine("## Artifacts");
        builder.AppendLine();
        builder.AppendLine($"- Plain config archive: `{plainArchivePath ?? "not captured"}`");
        builder.AppendLine($"- Encrypted secret archive: `{encryptedArchivePath ?? "not captured"}`");
        builder.AppendLine();
        builder.AppendLine("## Restore Order");
        builder.AppendLine();
        builder.AppendLine("1. Bootstrap Ubuntu and install prerequisite packages.");
        builder.AppendLine("2. Restore plain configuration files from the plain archive.");
        builder.AppendLine("3. Restore encrypted secrets only after decrypting them on a trusted machine.");
        builder.AppendLine("4. Clone or update the manifest repositories.");
        builder.AppendLine("5. Run the deployment command from the manifest.");
        builder.AppendLine("6. Run the verification commands and public URL checks from the manifest.");
        builder.AppendLine();
        builder.AppendLine("## Required Repositories");
        builder.AppendLine();
        foreach (var repository in manifest.Repositories ?? Array.Empty<PowerForgeServerRepository>())
            builder.AppendLine($"- `{repository.Role ?? "repository"}`: `{repository.Path ?? repository.Url ?? "manual"}`");
        builder.AppendLine();
        builder.AppendLine("## Required Secrets");
        builder.AppendLine();
        foreach (var secret in manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
            builder.AppendLine($"- `{secret.Id}`: `{secret.Path ?? secret.Env ?? "manual"}` ({secret.Capture})");

        if (warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Warnings");
            builder.AppendLine();
            foreach (var warning in warnings)
                builder.AppendLine($"- {warning}");
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
            builder.Append(invalid.Contains(c) || c is '/' or '\\' ? '-' : c);
        return builder.ToString();
    }

    private static string[] BuildServerRecoveryStages(PowerForgeServerRecoveryManifest manifest)
    {
        var stages = new List<string> { "inspect" };
        if (manifest.Capture is not null)
            stages.Add("capture");
        if (manifest.Bootstrap?.Commands?.Length > 0)
            stages.Add("bootstrap");
        if (manifest.Secrets?.Length > 0)
            stages.Add("restore-secrets");
        if (manifest.Deploy?.Commands?.Length > 0)
            stages.Add("deploy");
        if (manifest.Verify?.Commands?.Length > 0 || manifest.Verify?.Urls?.Length > 0)
            stages.Add("verify");

        return stages.ToArray();
    }
}
