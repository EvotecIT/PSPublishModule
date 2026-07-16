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
        var outPathArg = ResolveServerPlanOutputDirectory(subArgs);
        var archivePath = TryGetOptionValue(subArgs, "--archive") ?? "encrypted-secrets.tar.gz.age";
        var outputRoot = ResolveRestoreSecretsPlanOutputPath(outPathArg, manifest);
        Directory.CreateDirectory(outputRoot);

        var warnings = new List<string>();
        if (!string.Equals(manifest.BackupTarget?.Encryption, "age", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Restore script currently assumes age encryption.");
        if (manifest.Secrets?.Length is null or 0)
            warnings.Add("Manifest does not define any secrets.");
        if (string.IsNullOrWhiteSpace(manifest.BackupTarget?.RecipientEnv) &&
            string.IsNullOrWhiteSpace(manifest.BackupTarget?.Recipient))
            warnings.Add("backupTarget.recipient and backupTarget.recipientEnv are not configured.");

        var managedPaths = (manifest.Paths ?? Array.Empty<PowerForgeServerPath>())
            .Where(static path => !string.IsNullOrWhiteSpace(path.Path))
            .GroupBy(static path => path.Path!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var secrets = (manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
            .Select(secret =>
            {
                managedPaths.TryGetValue(secret.Path ?? string.Empty, out var managedPath);
                return new PowerForgeServerRestoreSecretEntry
                {
                    Id = secret.Id,
                    Path = secret.Path,
                    Env = secret.Env,
                    RestoreMode = secret.RestoreMode,
                    RequiredFor = secret.RequiredFor is { Length: > 0 } ? string.Join(", ", secret.RequiredFor) : null,
                    Owner = secret.Owner ?? managedPath?.Owner,
                    Group = secret.Group ?? managedPath?.Group,
                    Mode = secret.Mode ?? managedPath?.Mode
                };
            })
            .ToArray();
        var allowedArchivePaths = GetEncryptedRestorePaths(manifest);

        var markdownPath = Path.Combine(outputRoot, "restore-secrets-plan.md");
        var scriptPath = Path.Combine(outputRoot, "restore-secrets.sh");
        WriteRestoreSecretsMarkdown(markdownPath, manifest, archivePath, allowedArchivePaths, secrets, warnings);
        WriteRestoreSecretsScript(scriptPath, archivePath, allowedArchivePaths, secrets);

        var result = new PowerForgeServerRestoreSecretsPlanResult
        {
            ManifestPath = manifestPath,
            OutputPath = outputRoot,
            MarkdownPath = markdownPath,
            ScriptPath = scriptPath,
            ArchivePath = archivePath,
            Encryption = manifest.BackupTarget?.Encryption,
            RecipientEnv = manifest.BackupTarget?.RecipientEnv,
            AllowedArchivePaths = allowedArchivePaths,
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
        IReadOnlyList<string> allowedArchivePaths,
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
        builder.AppendLine("The generated script decrypts into a mode-700 temporary directory, validates every archive member against the encrypted capture allowlist, rejects unsafe links and special files, and refuses to restore unless `POWERFORGE_RESTORE_SECRETS_CONFIRM=YES` is set.");
        builder.AppendLine();
        builder.AppendLine("## Allowed archive paths");
        builder.AppendLine();
        foreach (var allowedPath in allowedArchivePaths)
            builder.AppendLine($"- `{allowedPath}`");
        builder.AppendLine();
        builder.AppendLine("## Secrets");
        builder.AppendLine();
        foreach (var secret in secrets)
        {
            var ownership = string.IsNullOrWhiteSpace(secret.Owner) && string.IsNullOrWhiteSpace(secret.Group)
                ? "archive ownership"
                : $"{secret.Owner ?? string.Empty}:{secret.Group ?? string.Empty}";
            builder.AppendLine($"- `{secret.Id}` -> `{secret.Path ?? secret.Env ?? "manual"}` ({secret.RestoreMode ?? "file"}; {ownership}; mode {secret.Mode ?? "from archive"})");
        }

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
        IReadOnlyList<string> allowedArchivePaths,
        IReadOnlyList<PowerForgeServerRestoreSecretEntry> secrets)
        => File.WriteAllText(path, BuildRestoreSecretsScript(archivePath, allowedArchivePaths, secrets));

    internal static string BuildRestoreSecretsScript(
        string archivePath,
        IReadOnlyList<string> allowedArchivePaths,
        IReadOnlyList<PowerForgeServerRestoreSecretEntry> secrets)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -Eeuo pipefail");
        builder.AppendLine("umask 077");
        builder.AppendLine();
        builder.AppendLine("# Generated by powerforge-web server restore-secrets-plan.");
        builder.AppendLine("# Run on the target host after copying the encrypted archive there.");
        builder.AppendLine();
        builder.AppendLine($"default_archive={ShellQuote(archivePath)}");
        builder.AppendLine("archive=\"${1:-$default_archive}\"");
        builder.AppendLine("tmp_dir=\"$(mktemp -d)\"");
        builder.AppendLine("chmod 0700 \"$tmp_dir\"");
        builder.AppendLine("trap 'rm -rf \"$tmp_dir\"' EXIT");
        builder.AppendLine();
        builder.AppendLine("command -v age >/dev/null || { echo 'age is required to decrypt secrets' >&2; exit 2; }");
        builder.AppendLine("command -v python3 >/dev/null || { echo 'python3 is required to validate the secret archive' >&2; exit 2; }");
        builder.AppendLine("test -f \"$archive\" || { echo \"encrypted archive not found: $archive\" >&2; exit 2; }");
        builder.AppendLine("age -d -o \"$tmp_dir/secrets.tar.gz\" \"$archive\"");
        builder.AppendLine("echo 'Encrypted secret archive contents:'");
        builder.AppendLine("tar -tzf \"$tmp_dir/secrets.tar.gz\"");
        builder.AppendLine();
        builder.AppendLine("cat >\"$tmp_dir/allowed-paths\" <<'POWERFORGE_ALLOWED_PATHS'");
        foreach (var allowedPath in allowedArchivePaths)
            builder.AppendLine(allowedPath.TrimStart('/'));
        builder.AppendLine("POWERFORGE_ALLOWED_PATHS");
        builder.AppendLine("python3 - \"$tmp_dir/secrets.tar.gz\" \"$tmp_dir/allowed-paths\" <<'POWERFORGE_VALIDATE_ARCHIVE'");
        builder.AppendLine("import os");
        builder.AppendLine("import posixpath");
        builder.AppendLine("import sys");
        builder.AppendLine("import tarfile");
        builder.AppendLine();
        builder.AppendLine("archive_path, allowlist_path = sys.argv[1:3]");
        builder.AppendLine("with open(allowlist_path, encoding='utf-8') as stream:");
        builder.AppendLine("    allowed = tuple(line.strip().strip('/') for line in stream if line.strip())");
        builder.AppendLine("if not allowed:");
        builder.AppendLine("    raise SystemExit('Encrypted archive allowlist is empty.')");
        builder.AppendLine();
        builder.AppendLine("def is_allowed(path):");
        builder.AppendLine("    return any(path == root or path.startswith(root + '/') for root in allowed)");
        builder.AppendLine();
        builder.AppendLine("def reject_existing_symlink(path):");
        builder.AppendLine("    destination = '/'");
        builder.AppendLine("    for component in path.split('/'):");
        builder.AppendLine("        destination = posixpath.join(destination, component)");
        builder.AppendLine("        if os.path.islink(destination):");
        builder.AppendLine("            raise SystemExit(f'Existing symlink blocks safe restore: {destination}')");
        builder.AppendLine();
        builder.AppendLine("seen = set()");
        builder.AppendLine("members = []");
        builder.AppendLine("symlink_targets = {}");
        builder.AppendLine("with tarfile.open(archive_path, mode='r:gz') as archive:");
        builder.AppendLine("    for member in archive.getmembers():");
        builder.AppendLine("        original = member.name");
        builder.AppendLine("        normalized = posixpath.normpath(original)");
        builder.AppendLine("        if original.startswith('/') or normalized in ('', '.', '..') or normalized.startswith('../'):");
        builder.AppendLine("            raise SystemExit(f'Unsafe archive path: {original}')");
        builder.AppendLine("        if normalized in seen:");
        builder.AppendLine("            raise SystemExit(f'Duplicate archive path: {original}')");
        builder.AppendLine("        seen.add(normalized)");
        builder.AppendLine("        if not is_allowed(normalized):");
        builder.AppendLine("            raise SystemExit(f'Archive path is outside the manifest allowlist: {original}')");
        builder.AppendLine("        reject_existing_symlink(normalized)");
        builder.AppendLine("        if member.islnk():");
        builder.AppendLine("            raise SystemExit(f'Hard links are not allowed in secret archives: {original}')");
        builder.AppendLine("        if member.issym():");
        builder.AppendLine("            if posixpath.isabs(member.linkname):");
        builder.AppendLine("                raise SystemExit(f'Absolute symlink target is not allowed: {original}')");
        builder.AppendLine("            target = posixpath.normpath(posixpath.join(posixpath.dirname(normalized), member.linkname))");
        builder.AppendLine("            if target == '..' or target.startswith('../') or not is_allowed(target):");
        builder.AppendLine("                raise SystemExit(f'Symlink target is outside the manifest allowlist: {original} -> {member.linkname}')");
        builder.AppendLine("            reject_existing_symlink(target)");
        builder.AppendLine("            symlink_targets[normalized] = target");
        builder.AppendLine("        elif not (member.isfile() or member.isdir()):");
        builder.AppendLine("            raise SystemExit(f'Unsupported archive member type: {original}')");
        builder.AppendLine("        if member.mode & 0o7000:");
        builder.AppendLine("            raise SystemExit(f'Archive member has unsafe special permission bits: {original}')");
        builder.AppendLine("        members.append((member, normalized))");
        builder.AppendLine();
        builder.AppendLine("for member, normalized in members:");
        builder.AppendLine("    if not member.issym() and any(normalized.startswith(link + '/') for link in symlink_targets):");
        builder.AppendLine("        raise SystemExit(f'Archive member traverses an archive symlink: {member.name}')");
        builder.AppendLine("for link, target in symlink_targets.items():");
        builder.AppendLine("    if target == link or target.startswith(link + '/') or any(target == other or target.startswith(other + '/') for other in symlink_targets if other != link):");
        builder.AppendLine("        raise SystemExit(f'Archive symlink chains are not allowed: {link}')");
        builder.AppendLine("POWERFORGE_VALIDATE_ARCHIVE");
        builder.AppendLine();
        builder.AppendLine("if [ \"${POWERFORGE_RESTORE_SECRETS_CONFIRM:-}\" != \"YES\" ]; then");
        builder.AppendLine("  echo 'Set POWERFORGE_RESTORE_SECRETS_CONFIRM=YES to extract secrets to /.' >&2");
        builder.AppendLine("  exit 3");
        builder.AppendLine("fi");
        builder.AppendLine("[ \"$(id -u)\" -eq 0 ] || { echo 'Secret restore must run as root.' >&2; exit 3; }");
        builder.AppendLine();
        builder.AppendLine("tar --no-same-owner --no-same-permissions --no-overwrite-dir --no-acls --no-selinux --no-xattrs -xzf \"$tmp_dir/secrets.tar.gz\" -C /");
        if (secrets.Any(secret =>
                string.Equals(secret.RestoreMode, "directory", StringComparison.OrdinalIgnoreCase) &&
                (!string.IsNullOrWhiteSpace(secret.Owner) ||
                 !string.IsNullOrWhiteSpace(secret.Group) ||
                 !string.IsNullOrWhiteSpace(secret.Mode))))
        {
            builder.AppendLine("apply_directory_metadata() {");
            builder.AppendLine("  python3 - \"$@\" <<'POWERFORGE_APPLY_DIRECTORY_METADATA'");
            builder.AppendLine("import grp");
            builder.AppendLine("import os");
            builder.AppendLine("import posixpath");
            builder.AppendLine("import pwd");
            builder.AppendLine("import sys");
            builder.AppendLine("import tarfile");
            builder.AppendLine();
            builder.AppendLine("archive_path, root, owner_name, group_name, directory_mode, file_mode = sys.argv[1:]");
            builder.AppendLine("root_normalized = root.strip('/')");
            builder.AppendLine("uid = pwd.getpwnam(owner_name).pw_uid if owner_name else -1");
            builder.AppendLine("gid = grp.getgrnam(group_name).gr_gid if group_name else -1");
            builder.AppendLine("with tarfile.open(archive_path, mode='r:gz') as archive:");
            builder.AppendLine("    for member in archive.getmembers():");
            builder.AppendLine("        normalized = posixpath.normpath(member.name)");
            builder.AppendLine("        if normalized != root_normalized and not normalized.startswith(root_normalized + '/'):");
            builder.AppendLine("            continue");
            builder.AppendLine("        target = '/' + normalized");
            builder.AppendLine("        os.lstat(target)");
            builder.AppendLine("        if uid != -1 or gid != -1:");
            builder.AppendLine("            os.chown(target, uid, gid, follow_symlinks=False)");
            builder.AppendLine("        if member.isdir() and directory_mode:");
            builder.AppendLine("            os.chmod(target, int(directory_mode, 8), follow_symlinks=False)");
            builder.AppendLine("        elif member.isfile() and file_mode:");
            builder.AppendLine("            os.chmod(target, int(file_mode, 8), follow_symlinks=False)");
            builder.AppendLine("POWERFORGE_APPLY_DIRECTORY_METADATA");
            builder.AppendLine("}");
        }
        foreach (var secret in secrets
                     .Where(secret =>
                         !string.IsNullOrWhiteSpace(secret.Path) &&
                         allowedArchivePaths.Any(allowedPath => PathContains(allowedPath, secret.Path!)))
                     .OrderBy(secret => secret.Path!.Count(static character => character == '/')))
        {
            var pathValue = ShellQuote(secret.Path!);
            var isDirectory = string.Equals(secret.RestoreMode, "directory", StringComparison.OrdinalIgnoreCase);
            if (isDirectory)
            {
                if (!string.IsNullOrWhiteSpace(secret.Owner) ||
                    !string.IsNullOrWhiteSpace(secret.Group) ||
                    !string.IsNullOrWhiteSpace(secret.Mode))
                {
                    var fileMode = string.IsNullOrWhiteSpace(secret.Mode)
                        ? string.Empty
                        : Convert.ToString(
                            Convert.ToInt32(secret.Mode, 8) & Convert.ToInt32("666", 8),
                            8).PadLeft(3, '0');
                    builder.AppendLine($"if [ -d {pathValue} ]; then apply_directory_metadata \"$tmp_dir/secrets.tar.gz\" {pathValue} {ShellQuote(secret.Owner ?? string.Empty)} {ShellQuote(secret.Group ?? string.Empty)} {ShellQuote(secret.Mode ?? string.Empty)} {ShellQuote(fileMode)}; fi");
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(secret.Owner) || !string.IsNullOrWhiteSpace(secret.Group))
            {
                var ownership = (secret.Owner, secret.Group) switch
                {
                    ({ Length: > 0 } owner, { Length: > 0 } group) => $"{owner}:{group}",
                    ({ Length: > 0 } owner, _) => owner,
                    (_, { Length: > 0 } group) => $":{group}",
                    _ => throw new InvalidOperationException("Restore ownership metadata is empty.")
                };
                builder.AppendLine($"if [ -e {pathValue} ] || [ -L {pathValue} ]; then chown -h {ShellQuote(ownership)} -- {pathValue}; fi");
            }
            if (!string.IsNullOrWhiteSpace(secret.Mode))
                builder.AppendLine($"if [ -e {pathValue} ]; then chmod {secret.Mode} -- {pathValue}; fi");
        }
        builder.AppendLine("echo 'Secrets restored. Run server verify next.'");

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
