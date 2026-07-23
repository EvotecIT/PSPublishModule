using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private const string SupportedServerActions = "inspect, plan, validate, capture, deploy, verify, scaffold, bootstrap-plan, restore-secrets-plan";

    internal static int HandleServer(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        if (subArgs.Length == 0)
            return Fail($"Missing server action. Supported actions: {SupportedServerActions}.", outputJson, logger, "web.server");

        var action = subArgs[0].ToLowerInvariant();
        var actionArgs = subArgs.Skip(1).ToArray();

        try
        {
            return action switch
            {
                "inspect" => HandleServerInspect(actionArgs, outputJson, logger, outputSchemaVersion),
                "plan" => HandleServerPlan(actionArgs, outputJson, logger, outputSchemaVersion),
                "validate" => HandleServerPlan(actionArgs, outputJson, logger, outputSchemaVersion),
                "capture" => HandleServerCapture(actionArgs, outputJson, logger, outputSchemaVersion),
                "verify" => HandleServerVerify(actionArgs, outputJson, logger, outputSchemaVersion),
                "deploy" => HandleServerDeploy(actionArgs, outputJson, logger, outputSchemaVersion),
                "scaffold" => HandleServerScaffold(actionArgs, outputJson, logger, outputSchemaVersion),
                "bootstrap-plan" => HandleServerBootstrapPlan(actionArgs, outputJson, logger, outputSchemaVersion),
                "restore-secrets-plan" => HandleServerRestoreSecretsPlan(actionArgs, outputJson, logger, outputSchemaVersion),
                _ => Fail($"Unknown server action '{subArgs[0]}'. Supported actions: {SupportedServerActions}.", outputJson, logger, "web.server")
            };
        }
        catch (InvalidOperationException ex)
        {
            return Fail($"Server action failed: {ex.Message}", outputJson, logger, "web.server");
        }
    }

    private static int HandleServerPlan(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.plan");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var fullManifestPath = loaded.ManifestPath!;
        var manifest = loaded.Manifest;

        var warnings = new List<string>();
        if (manifest.SchemaVersion != 2)
            warnings.Add("schemaVersion should be 2 for this recovery engine revision.");
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
            AccountCount = manifest.Accounts?.Length ?? 0,
            PackageCount = GetDeclaredPackageNames(manifest.Packages).Length,
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
        logger.Info($"Repositories: {result.RepositoryCount}; accounts: {result.AccountCount}; packages: {result.PackageCount}; services: {result.SystemdServiceCount}; timers: {result.SystemdTimerCount}");
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
                         TryGetOptionValue(subArgs, "--output-dir");
        var dryRun = HasOption(subArgs, "--dry-run");
        var skipFiles = HasOption(subArgs, "--skip-files");
        var skipEncrypted = HasOption(subArgs, "--skip-encrypted");
        var failOnFailure = HasOption(subArgs, "--fail-on-failure");
        var sshCommand = TryGetOptionValue(subArgs, "--ssh") ?? "ssh";
        var target = BuildServerSshTarget(manifest.Target);

        var outputRoot = ResolveCaptureOutputPath(outPathArg, manifest);
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(Path.Combine(outputRoot, "commands"));

        var warnings = new List<string>();
        var commandResults = new List<PowerForgeServerCaptureCommandResult>();
        var copiedManifestPath = Path.Combine(outputRoot, "manifest.json");

        var commandList = manifest.Capture?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>();
        var plainFiles = manifest.Capture?.PlainFiles ?? Array.Empty<PowerForgeServerManagedFile>();
        var encryptedFiles = manifest.Capture?.EncryptedFiles ?? Array.Empty<PowerForgeServerManagedFile>();
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
            using var captureLock = AcquireRemoteOperationLocks(
                sshCommand,
                target,
                manifest.OperationLocks ?? Array.Empty<string>(),
                waitSeconds: 900);
            for (var commandIndex = 0; commandIndex < commandList.Length; commandIndex++)
            {
                var command = commandList[commandIndex];
                if (command.Sensitive)
                    continue;
                captureLock?.EnsureHeld($"before capture command '{command.Id}'");
                var result = CaptureRemoteCommand(
                    sshCommand,
                    target,
                    command,
                    Path.Combine(outputRoot, "commands"),
                    commandIndex);
                captureLock?.EnsureHeld($"after capture command '{command.Id}'");
                commandResults.Add(result);
                if (!result.Success && command.Required)
                    warnings.Add($"Required capture command '{result.Id}' failed with exit code {result.ExitCode}.");
            }

            if (!skipFiles && plainFiles.Length > 0 && plainArchivePath is not null)
            {
                captureLock?.EnsureHeld("before plain archive capture");
                var archiveResult = CaptureRemoteTarArchive(sshCommand, target, plainFiles, plainArchivePath);
                captureLock?.EnsureHeld("after plain archive capture");
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
                    captureLock?.EnsureHeld("before encrypted archive capture");
                    var encryptedResult = CaptureRemoteEncryptedTarArchive(
                        sshCommand,
                        target,
                        encryptedFiles,
                        encryptedArchivePath,
                        recipient);
                    captureLock?.EnsureHeld("after encrypted archive capture");
                    if (!encryptedResult.Success)
                    {
                        warnings.Add($"Encrypted capture failed with exit code {encryptedResult.ExitCode}.");
                        if (!string.IsNullOrWhiteSpace(encryptedResult.Stderr))
                            File.WriteAllText(Path.Combine(outputRoot, "encrypted-secrets.stderr.txt"), encryptedResult.Stderr);
                    }
                }
            }
        }

        HydrateCapturedRepositoryRefs(manifest, commandResults, warnings);
        var capturedManifestOptions = new JsonSerializerOptions(WebCliJson.Options) { WriteIndented = true };
        var capturedManifestContext = new PowerForgeWebCliJsonContext(capturedManifestOptions);
        File.WriteAllText(
            copiedManifestPath,
            JsonSerializer.Serialize(manifest, capturedManifestContext.PowerForgeServerRecoveryManifest));

        var success = dryRun || warnings.Count == 0;
        var exitCode = success || !failOnFailure ? 0 : 1;
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
                Success = success || !failOnFailure,
                ExitCode = exitCode,
                Config = "web.serverrecovery",
                ConfigPath = manifestPath,
                Result = WebCliJson.SerializeToElement(resultSummary, WebCliJson.Context.PowerForgeServerCaptureResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return exitCode;
        }

        logger.Success(success
            ? (dryRun ? "Server capture dry run completed." : "Server capture completed.")
            : "Server capture completed with warnings.");
        logger.Info($"Output: {outputRoot}");
        logger.Info($"Command captures: {commandResults.Count}");
        if (!string.IsNullOrWhiteSpace(plainArchivePath))
            logger.Info($"Plain archive: {plainArchivePath}");
        if (!string.IsNullOrWhiteSpace(encryptedArchivePath))
            logger.Info($"Encrypted archive: {encryptedArchivePath}");
        logger.Info($"Restore checklist: {checklistPath}");
        foreach (var warning in warnings)
            logger.Warn(warning);

        return exitCode;
    }

    internal static void HydrateCapturedRepositoryRefs(
        PowerForgeServerRecoveryManifest manifest,
        IReadOnlyCollection<PowerForgeServerCaptureCommandResult> commandResults,
        ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(commandResults);
        ArgumentNullException.ThrowIfNull(warnings);

        foreach (var repository in manifest.Repositories ?? Array.Empty<PowerForgeServerRepository>())
        {
            var commandIds = GetRepositoryRefCaptureCommandIds(repository);
            if (commandIds.Length == 0)
                continue;

            // A dynamic ref must never fall back to a stale source-manifest value.
            repository.Ref = null;
            var revisions = new List<string>(commandIds.Length);
            var captureFailed = false;
            foreach (var commandId in commandIds)
            {
                var result = commandResults.SingleOrDefault(candidate =>
                    string.Equals(candidate.Id, commandId, StringComparison.Ordinal));
                if (result is null || !result.Success || string.IsNullOrWhiteSpace(result.StdoutPath) || !File.Exists(result.StdoutPath))
                {
                    warnings.Add($"Repository '{repository.Role}' revision capture is missing or failed for command '{commandId}'.");
                    captureFailed = true;
                    continue;
                }

                var revision = File.ReadAllText(result.StdoutPath).Trim().ToLowerInvariant();
                if (!PowerForge.Web.WebEngineLockFile.IsCommitSha(revision))
                {
                    warnings.Add($"Repository '{repository.Role}' revision capture is not an exact commit for command '{commandId}'.");
                    captureFailed = true;
                    continue;
                }

                revisions.Add(revision);
            }

            if (captureFailed)
                continue;

            var distinctRevisions = revisions.Distinct(StringComparer.Ordinal).ToArray();
            if (distinctRevisions.Length != 1)
            {
                warnings.Add($"Repository '{repository.Role}' revision captures disagree across commands '{string.Join("', '", commandIds)}'.");
                continue;
            }

            repository.Ref = distinctRevisions[0];
        }
    }

    private static string[] GetRepositoryRefCaptureCommandIds(PowerForgeServerRepository repository)
    {
        if (repository.RefCaptureCommandIds is { Length: > 0 })
            return repository.RefCaptureCommandIds;
        return string.IsNullOrWhiteSpace(repository.RefCaptureCommandId)
            ? Array.Empty<string>()
            : [repository.RefCaptureCommandId];
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

            var validationErrors = ValidateServerRecoveryManifest(manifest);
            if (validationErrors.Length > 0)
                throw new InvalidOperationException("Server recovery manifest validation failed: " + string.Join(" ", validationErrors));

            return (manifest, fullManifestPath, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return (null, null, Fail($"Failed to load server recovery manifest: {ex.Message}", outputJson, logger, commandName));
        }
    }

    internal static string? ResolveServerPlanOutputDirectory(string[] subArgs)
        => TryGetOptionValue(subArgs, "--out") ??
           TryGetOptionValue(subArgs, "--output-dir");

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
        string commandOutputDirectory,
        int commandIndex)
    {
        var id = BuildCaptureCommandOutputStem(commandIndex, command.Id);
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

    internal static string BuildCaptureCommandOutputStem(int commandIndex, string? commandId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commandIndex);
        var id = SanitizeFileName(string.IsNullOrWhiteSpace(commandId) ? "command" : commandId);
        const int maximumReadableIdLength = 80;
        if (id.Length > maximumReadableIdLength)
            id = id[..maximumReadableIdLength];
        return $"{commandIndex:D4}-{id}";
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

    internal static string BuildRemoteTarScript(PowerForgeServerManagedFile[] files)
    {
        var captureFiles = GetRemoteCaptureFiles(files, allowWildcards: false);

        var script = new StringBuilder("set -e; sudo -n tar -czf - ");
        if (!captureFiles.Any(static file => file.Required))
            script.Append("--ignore-failed-read ");
        script
            .AppendJoin(' ', captureFiles.Select(static file => file.Target));
        return script.ToString();
    }

    internal static string BuildRemoteEncryptedTarScript(PowerForgeServerManagedFile[] files, string recipient)
    {
        var command = BuildRemoteEncryptedCaptureCommand(files, recipient, quoteArguments: true);
        return $"sudo -n {command}";
    }

    internal static string BuildRemoteEncryptedCaptureSudoersCommand(PowerForgeServerManagedFile[] files, string recipient)
        => BuildRemoteEncryptedCaptureCommand(files, recipient, quoteArguments: false);

    private static string BuildRemoteEncryptedCaptureCommand(
        PowerForgeServerManagedFile[] files,
        string recipient,
        bool quoteArguments)
    {
        var captureFiles = GetRemoteCaptureFiles(files, allowWildcards: false);
        if (string.IsNullOrWhiteSpace(recipient) ||
            !recipient.StartsWith("age1", StringComparison.Ordinal) ||
            recipient.Any(static character => !(character is >= 'a' and <= 'z' || character is >= '0' and <= '9')))
            throw new InvalidOperationException("Remote encrypted capture requires an age public recipient beginning with age1.");

        static string Raw(string value) => value;
        Func<string, string> format = quoteArguments ? ShellQuote : Raw;
        var requiredFiles = captureFiles.Where(static file => file.Required).ToArray();
        var optionalFiles = captureFiles.Where(static file => !file.Required).ToArray();
        var arguments = new List<string>
        {
            "/usr/local/sbin/powerforge-server-encrypted-capture",
            "--recipient",
            format(recipient)
        };
        if (requiredFiles.Length == 0)
            arguments.Add("--ignore-failed-read");
        arguments.Add("--");
        arguments.AddRange(requiredFiles.Length == 0
            ? optionalFiles.Select(file => format(file.Target))
            : requiredFiles.Select(file => format(file.Target)));
        if (requiredFiles.Length > 0 && optionalFiles.Length > 0)
        {
            arguments.Add("--optional");
            arguments.AddRange(optionalFiles.Select(file => format(file.Target)));
        }
        return string.Join(' ', arguments);
    }

    private static (string Target, bool Required)[] GetRemoteCaptureFiles(
        IEnumerable<PowerForgeServerManagedFile> files,
        bool allowWildcards)
    {
        var captureFiles = files
            .Where(static file => !string.IsNullOrWhiteSpace(file.Target))
            .Select(static file => (Target: file.Target!, file.Required))
            .ToArray();
        if (captureFiles.Length == 0)
            throw new InvalidOperationException("No target paths are defined for capture.");

        foreach (var file in captureFiles)
        {
            var path = file.Target;
            var valid = path.StartsWith("/", StringComparison.Ordinal) &&
                path != "/" &&
                !path.Contains("//", StringComparison.Ordinal) &&
                !path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(static segment => segment is "." or "..") &&
                path.All(character => IsAsciiLetterOrDigit(character) ||
                character is '/' or '.' or '_' or '-' ||
                (allowWildcards && character is '*' or '?' or '[' or ']'));
            if (!valid)
                throw new InvalidOperationException($"Capture path contains unsupported characters: {path}");
        }

        return captureFiles;
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

    internal static string[] BuildServerRecoveryStages(PowerForgeServerRecoveryManifest manifest)
    {
        var stages = new List<string> { "inspect" };
        if (manifest.Capture is not null)
            stages.Add("capture");
        if (HasServerRecoveryBootstrapWork(manifest))
            stages.Add("bootstrap");
        if (manifest.Secrets?.Length > 0)
            stages.Add("restore-secrets");
        if (manifest.Deploy?.Commands?.Length > 0)
            stages.Add("deploy");
        if (manifest.Verify?.Commands?.Length > 0 || manifest.Verify?.Urls?.Length > 0)
            stages.Add("verify");

        return stages.ToArray();
    }

    private static bool HasServerRecoveryBootstrapWork(PowerForgeServerRecoveryManifest manifest)
    {
        var packages = manifest.Packages;
        var apache = manifest.Apache;
        var systemd = manifest.Systemd;
        return manifest.Bootstrap?.Commands?.Length > 0 ||
               manifest.Repositories?.Length > 0 ||
               manifest.Accounts?.Length > 0 ||
               manifest.Paths?.Any(static path =>
                   path.Kind?.Equals("directory", StringComparison.OrdinalIgnoreCase) == true ||
                   !string.IsNullOrWhiteSpace(path.Source)) == true ||
               packages?.Apt?.Length > 0 ||
               packages?.ApacheModules?.Length > 0 ||
               packages?.DotnetSdks?.Length > 0 ||
               packages?.Powershell == true ||
               apache?.Modules?.Length > 0 ||
               apache?.Sites?.Length > 0 ||
               apache?.Conf?.Length > 0 ||
               systemd?.Services?.Length > 0 ||
               systemd?.Timers?.Length > 0 ||
               manifest.Firewall is not null ||
               manifest.Secrets?.Any(static secret => secret.RequiredDuringBootstrap != false) == true;
    }
}
