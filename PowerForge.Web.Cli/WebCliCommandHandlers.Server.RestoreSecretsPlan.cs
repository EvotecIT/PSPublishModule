using System.Text;
using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerRestoreSecretsPlan(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.restore-secrets-plan");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var manifest = loaded.Manifest;
        var manifestPath = loaded.ManifestPath!;
        var outPathArg = TryGetOptionValue(subArgs, "--out") ??
                         TryGetOptionValue(subArgs, "--output") ??
                         TryGetOptionValue(subArgs, "--output-dir");
        var archivePath = TryGetOptionValue(subArgs, "--archive") ?? "encrypted-secrets.tar.gz.age";
        var outputRoot = ResolveRestoreSecretsPlanOutputPath(outPathArg, manifest);
        Directory.CreateDirectory(outputRoot);

        var warnings = new List<string>();
        if (!manifest.BackupTarget?.Encryption?.Equals("age", StringComparison.OrdinalIgnoreCase) == true)
            warnings.Add("Restore script currently assumes age encryption.");
        if (manifest.Secrets?.Length is null or 0)
            warnings.Add("Manifest does not define any secrets.");
        if (string.IsNullOrWhiteSpace(manifest.BackupTarget?.RecipientEnv) &&
            string.IsNullOrWhiteSpace(manifest.BackupTarget?.Recipient))
            warnings.Add("backupTarget.recipient and backupTarget.recipientEnv are not configured.");

        var secrets = (manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
            .Select(static secret => new PowerForgeServerRestoreSecretEntry
            {
                Id = secret.Id,
                Path = secret.Path,
                Env = secret.Env,
                RestoreMode = secret.RestoreMode,
                RequiredFor = secret.RequiredFor is { Length: > 0 } ? string.Join(", ", secret.RequiredFor) : null
            })
            .ToArray();

        var markdownPath = Path.Combine(outputRoot, "restore-secrets-plan.md");
        var scriptPath = Path.Combine(outputRoot, "restore-secrets.sh");
        WriteRestoreSecretsMarkdown(markdownPath, manifest, archivePath, secrets, warnings);
        WriteRestoreSecretsScript(scriptPath, archivePath, secrets);

        var result = new PowerForgeServerRestoreSecretsPlanResult
        {
            ManifestPath = manifestPath,
            OutputPath = outputRoot,
            MarkdownPath = markdownPath,
            ScriptPath = scriptPath,
            ArchivePath = archivePath,
            Encryption = manifest.BackupTarget?.Encryption,
            RecipientEnv = manifest.BackupTarget?.RecipientEnv,
            Secrets = secrets,
            Warnings = warnings.ToArray()
        };

        File.WriteAllText(
            Path.Combine(outputRoot, "restore-secrets-plan.json"),
            JsonSerializer.Serialize(result, WebCliJson.Context.PowerForgeServerRestoreSecretsPlanResult));

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.restore-secrets-plan",
                Success = true,
                ExitCode = 0,
                Config = "web.serverrecovery",
                ConfigPath = manifestPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.PowerForgeServerRestoreSecretsPlanResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return 0;
        }

        logger.Success("Server secret restore plan generated.");
        logger.Info($"Output: {outputRoot}");
        logger.Info($"Markdown: {markdownPath}");
        logger.Info($"Script draft: {scriptPath}");
        logger.Info($"Secrets: {secrets.Length}");
        foreach (var warning in warnings)
            logger.Warn(warning);

        return 0;
    }

    private static string ResolveRestoreSecretsPlanOutputPath(string? outPathArg, PowerForgeServerRecoveryManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(outPathArg))
            return Path.GetFullPath(outPathArg);

        var name = SanitizeFileName(string.IsNullOrWhiteSpace(manifest.Name) ? "server" : manifest.Name);
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "_server-state", name, "restore-secrets-plan"));
    }

    private static void WriteRestoreSecretsMarkdown(
        string path,
        PowerForgeServerRecoveryManifest manifest,
        string archivePath,
        IReadOnlyList<PowerForgeServerRestoreSecretEntry> secrets,
        IReadOnlyList<string> warnings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# PowerForge Secret Restore Plan");
        builder.AppendLine();
        builder.AppendLine($"Manifest: `{manifest.Name}`");
        builder.AppendLine($"Archive: `{archivePath}`");
        builder.AppendLine($"Encryption: `{manifest.BackupTarget?.Encryption ?? "unknown"}`");
        builder.AppendLine();
        builder.AppendLine("The generated script decrypts into a temporary directory, lists archive contents, and refuses to restore unless `POWERFORGE_RESTORE_SECRETS_CONFIRM=YES` is set.");
        builder.AppendLine();
        builder.AppendLine("## Secrets");
        builder.AppendLine();
        foreach (var secret in secrets)
            builder.AppendLine($"- `{secret.Id}` -> `{secret.Path ?? secret.Env ?? "manual"}` ({secret.RestoreMode ?? "file"})");

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

    private static void WriteRestoreSecretsScript(
        string path,
        string archivePath,
        IReadOnlyList<PowerForgeServerRestoreSecretEntry> secrets)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -Eeuo pipefail");
        builder.AppendLine();
        builder.AppendLine("# Generated by powerforge-web server restore-secrets-plan.");
        builder.AppendLine("# Run on the target host after copying the encrypted archive there.");
        builder.AppendLine();
        builder.AppendLine($"archive=\"${{1:-{archivePath}}}\"");
        builder.AppendLine("tmp_dir=\"$(mktemp -d)\"");
        builder.AppendLine("trap 'rm -rf \"$tmp_dir\"' EXIT");
        builder.AppendLine();
        builder.AppendLine("command -v age >/dev/null || { echo 'age is required to decrypt secrets' >&2; exit 2; }");
        builder.AppendLine("test -f \"$archive\" || { echo \"encrypted archive not found: $archive\" >&2; exit 2; }");
        builder.AppendLine("age -d -o \"$tmp_dir/secrets.tar.gz\" \"$archive\"");
        builder.AppendLine("echo 'Encrypted secret archive contents:'");
        builder.AppendLine("tar -tzf \"$tmp_dir/secrets.tar.gz\"");
        builder.AppendLine();
        builder.AppendLine("if [ \"${POWERFORGE_RESTORE_SECRETS_CONFIRM:-}\" != \"YES\" ]; then");
        builder.AppendLine("  echo 'Set POWERFORGE_RESTORE_SECRETS_CONFIRM=YES to extract secrets to /.' >&2");
        builder.AppendLine("  exit 3");
        builder.AppendLine("fi");
        builder.AppendLine();
        builder.AppendLine("tar -xzf \"$tmp_dir/secrets.tar.gz\" -C /");
        foreach (var secret in secrets.Where(static secret => !string.IsNullOrWhiteSpace(secret.Path)))
        {
            if (secret.RestoreMode?.Equals("directory", StringComparison.OrdinalIgnoreCase) == true)
                continue;
            var mode = secret.Path!.Contains("letsencrypt/credentials", StringComparison.OrdinalIgnoreCase) ||
                       secret.Path.Contains(".env", StringComparison.OrdinalIgnoreCase)
                ? "600"
                : "640";
            builder.AppendLine($"if [ -e {ShellQuote(secret.Path)} ]; then chmod {mode} {ShellQuote(secret.Path)}; fi");
        }
        builder.AppendLine("echo 'Secrets restored. Run server verify next.'");

        File.WriteAllText(path, builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
