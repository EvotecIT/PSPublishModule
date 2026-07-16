namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static string[] ValidateServerRecoveryManifest(PowerForgeServerRecoveryManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        if (manifest.SchemaVersion <= 0)
            errors.Add("schemaVersion must be a positive integer.");
        if (string.IsNullOrWhiteSpace(manifest.Target?.SshAlias) && string.IsNullOrWhiteSpace(manifest.Target?.Host))
            errors.Add("target must declare sshAlias or host.");

        var plainFiles = manifest.Capture?.PlainFiles ?? Array.Empty<PowerForgeServerManagedFile>();
        var encryptedFiles = manifest.Capture?.EncryptedFiles ?? Array.Empty<PowerForgeServerManagedFile>();
        var plainPaths = ValidateCaptureEntries(plainFiles, "capture.plainFiles", sensitive: false, errors);
        var encryptedPaths = ValidateCaptureEntries(encryptedFiles, "capture.encryptedFiles", sensitive: true, errors);
        var retention = manifest.BackupTarget?.Retention;
        if (retention?.KeepDays is not null)
            errors.Add("backupTarget.retention.keepDays is not implemented; use keepLatestInTree for current-tree retention.");
        if (retention?.KeepLatestInTree is not null && retention.KeepLatest is not null)
            errors.Add("backupTarget.retention must not set both keepLatestInTree and its deprecated keepLatest alias.");
        ValidateBackupTarget(manifest.BackupTarget, plainFiles, encryptedFiles, errors);
        ValidateRepositories(manifest.Repositories, manifest.Capture?.Commands, errors);

        var managedPathIds = new HashSet<string>(StringComparer.Ordinal);
        var managedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in manifest.Paths ?? Array.Empty<PowerForgeServerPath>())
        {
            if (string.IsNullOrWhiteSpace(path.Id) || !managedPathIds.Add(path.Id))
                errors.Add($"Managed path id '{path.Id}' is missing or duplicated.");
            var normalizedPath = NormalizeCapturePath(path.Path, $"paths[{path.Id}].path", errors);
            if (normalizedPath is not null)
            {
                if (normalizedPath.IndexOfAny(['*', '?', '[']) >= 0)
                    errors.Add($"Managed path '{path.Id}' must use an exact path.");
                if (!managedPaths.Add(normalizedPath))
                    errors.Add($"Managed path '{normalizedPath}' is duplicated.");
            }
            if (!IsValidUnixIdentity(path.Owner))
                errors.Add($"Managed path '{path.Id}' has an invalid owner.");
            if (!IsValidUnixIdentity(path.Group))
                errors.Add($"Managed path '{path.Id}' has an invalid group.");
            if (!IsValidMode(path.Mode))
                errors.Add($"Managed path '{path.Id}' has an invalid mode.");
        }

        foreach (var plainPath in plainPaths)
        {
            foreach (var encryptedPath in encryptedPaths.Where(encryptedPath => CapturePathsMayOverlap(plainPath, encryptedPath)))
                errors.Add($"Plain capture path '{plainPath}' overlaps encrypted capture path '{encryptedPath}'.");
        }

        var secretIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var secret in manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
        {
            if (string.IsNullOrWhiteSpace(secret.Id) || !secretIds.Add(secret.Id))
                errors.Add($"Secret id '{secret.Id}' is missing or duplicated.");
            if (!string.Equals(secret.Capture, "exclude", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(secret.Capture, "encrypted", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Secret '{secret.Id}' has unsupported capture mode '{secret.Capture}'.");
            if (!string.IsNullOrWhiteSpace(secret.RestoreMode) &&
                !string.Equals(secret.RestoreMode, "file", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(secret.RestoreMode, "directory", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Secret '{secret.Id}' has unsupported restore mode '{secret.RestoreMode}'.");
            if (!IsValidUnixIdentity(secret.Owner))
                errors.Add($"Secret '{secret.Id}' has an invalid restore owner.");
            if (!IsValidUnixIdentity(secret.Group))
                errors.Add($"Secret '{secret.Id}' has an invalid restore group.");
            if (!IsValidMode(secret.Mode))
                errors.Add($"Secret '{secret.Id}' has an invalid restore mode.");

            string? secretPath = null;
            if (!string.IsNullOrWhiteSpace(secret.Path))
                secretPath = NormalizeCapturePath(secret.Path, $"secrets[{secret.Id}].path", errors);

            if (string.Equals(secret.Capture, "exclude", StringComparison.OrdinalIgnoreCase))
            {
                if (secretPath is not null && plainPaths.Any(path => CapturePathsMayOverlap(secretPath, path)))
                    errors.Add($"Excluded secret '{secret.Id}' at '{secretPath}' overlaps capture.plainFiles and could be published without encryption.");
                if (secretPath is not null && encryptedPaths.Any(path => CapturePathsMayOverlap(secretPath, path)))
                    errors.Add($"Excluded secret '{secret.Id}' at '{secretPath}' overlaps capture.encryptedFiles and would not be excluded.");
                continue;
            }

            if (!string.Equals(secret.Capture, "encrypted", StringComparison.OrdinalIgnoreCase))
                continue;

            if (secretPath is null)
            {
                errors.Add($"Encrypted secret '{secret.Id}' must declare a path covered by capture.encryptedFiles.");
                continue;
            }

            if (!encryptedPaths.Any(path => PathContains(path, secretPath)))
                errors.Add($"Encrypted secret '{secret.Id}' at '{secretPath}' is not covered by capture.encryptedFiles.");
            if (plainPaths.Any(path => CapturePathsMayOverlap(secretPath, path)))
                errors.Add($"Encrypted secret '{secret.Id}' at '{secretPath}' overlaps capture.plainFiles.");
        }

        for (var leftIndex = 0; leftIndex < encryptedPaths.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < encryptedPaths.Length; rightIndex++)
            {
                if (CapturePathsMayOverlap(encryptedPaths[leftIndex], encryptedPaths[rightIndex]))
                    errors.Add($"Encrypted capture paths '{encryptedPaths[leftIndex]}' and '{encryptedPaths[rightIndex]}' overlap.");
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateRepositories(
        IEnumerable<PowerForgeServerRepository>? repositories,
        IEnumerable<PowerForgeServerNamedCommand>? captureCommands,
        ICollection<string> errors)
    {
        var captureCommandsById = (captureCommands ?? Array.Empty<PowerForgeServerNamedCommand>())
            .Where(static command => !string.IsNullOrWhiteSpace(command.Id))
            .GroupBy(static command => command.Id!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var index = 0;
        foreach (var repository in repositories ?? Array.Empty<PowerForgeServerRepository>())
        {
            var field = $"repositories[{index}]";
            NormalizeCapturePath(repository.Path, $"{field}.path", errors);

            var prerequisites = new HashSet<string>(StringComparer.Ordinal);
            var prerequisiteIndex = 0;
            foreach (var path in repository.BootstrapRequiredFiles ?? Array.Empty<string>())
            {
                var normalized = NormalizeCapturePath(path, $"{field}.bootstrapRequiredFiles[{prerequisiteIndex}]", errors);
                if (normalized is not null && !prerequisites.Add(normalized))
                    errors.Add($"{field}.bootstrapRequiredFiles[{prerequisiteIndex}] duplicates prerequisite path '{normalized}'.");
                prerequisiteIndex++;
            }

            var hasIdentity = !string.IsNullOrWhiteSpace(repository.SshIdentityFile);
            var hasKnownHosts = !string.IsNullOrWhiteSpace(repository.SshKnownHostsFile);
            if (hasIdentity != hasKnownHosts)
            {
                errors.Add($"{field} must declare both sshIdentityFile and sshKnownHostsFile.");
                index++;
                continue;
            }

            if (hasIdentity)
            {
                var identity = NormalizeCapturePath(repository.SshIdentityFile, $"{field}.sshIdentityFile", errors);
                var knownHosts = NormalizeCapturePath(repository.SshKnownHostsFile, $"{field}.sshKnownHostsFile", errors);
                if (identity is not null && !prerequisites.Contains(identity))
                    errors.Add($"{field}.bootstrapRequiredFiles must include sshIdentityFile '{identity}'.");
                if (knownHosts is not null && !prerequisites.Contains(knownHosts))
                    errors.Add($"{field}.bootstrapRequiredFiles must include sshKnownHostsFile '{knownHosts}'.");
            }

            if (!string.IsNullOrWhiteSpace(repository.RefCaptureCommandId))
            {
                var commandId = repository.RefCaptureCommandId;
                if (!IsSafeIdentifier(commandId))
                {
                    errors.Add($"{field}.refCaptureCommandId contains unsupported characters.");
                }
                else if (!captureCommandsById.TryGetValue(commandId, out var commands) || commands.Length != 1)
                {
                    errors.Add($"{field}.refCaptureCommandId must identify exactly one capture command.");
                }
                else if (!commands[0].Required || commands[0].Sensitive)
                {
                    errors.Add($"{field}.refCaptureCommandId must identify a required, non-sensitive capture command.");
                }
            }

            index++;
        }
    }

    private static bool IsSafeIdentifier(string value)
        => value.Length <= 64 &&
           (IsAsciiLetterOrDigit(value[0]) || value[0] == '_') &&
           value.All(static character => IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static void ValidateBackupTarget(
        PowerForgeServerBackupTarget? backupTarget,
        IReadOnlyCollection<PowerForgeServerManagedFile> plainFiles,
        IReadOnlyCollection<PowerForgeServerManagedFile> encryptedFiles,
        ICollection<string> errors)
    {
        if (backupTarget is null)
            return;

        if (!string.Equals(backupTarget.Type, "github", StringComparison.OrdinalIgnoreCase))
            errors.Add("backupTarget.type must be github.");
        if (!IsRepositoryName(backupTarget.Repository))
            errors.Add("backupTarget.repository must be an owner/repository name.");
        if (!IsSafeRepositoryRelativeValue(backupTarget.Branch, allowSlash: true))
            errors.Add("backupTarget.branch contains unsupported characters.");
        if (!IsSafeRepositoryRelativeValue(backupTarget.Path, allowSlash: true))
            errors.Add("backupTarget.path must be a safe repository-relative path.");
        if (!string.Equals(backupTarget.Encryption, "age", StringComparison.OrdinalIgnoreCase))
            errors.Add("backupTarget.encryption must be age.");
        if (string.IsNullOrWhiteSpace(backupTarget.Recipient) && string.IsNullOrWhiteSpace(backupTarget.RecipientEnv))
            errors.Add("backupTarget must declare recipient or recipientEnv.");
        if (!string.IsNullOrWhiteSpace(backupTarget.RecipientEnv) && !IsEnvironmentVariableName(backupTarget.RecipientEnv))
            errors.Add("backupTarget.recipientEnv must be an environment variable name.");

        var retention = backupTarget.Retention;
        var keepLatest = retention?.KeepLatestInTree ?? retention?.KeepLatest;
        if (keepLatest is null)
            errors.Add("backupTarget.retention.keepLatestInTree is required.");
        else if (keepLatest is < 1 or > 365)
            errors.Add("backupTarget.retention.keepLatestInTree must be from 1 through 365.");

        if (plainFiles.Count == 0)
            errors.Add("backup capture requires at least one capture.plainFiles entry.");
        if (encryptedFiles.Count == 0)
            errors.Add("backup capture requires at least one capture.encryptedFiles entry.");
    }

    internal static string[] GetEncryptedRestorePaths(PowerForgeServerRecoveryManifest manifest)
    {
        var errors = new List<string>();
        var paths = ValidateCaptureEntries(
            manifest.Capture?.EncryptedFiles ?? Array.Empty<PowerForgeServerManagedFile>(),
            "capture.encryptedFiles",
            sensitive: true,
            errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("Server recovery manifest validation failed: " + string.Join(" ", errors));
        return paths;
    }

    private static string[] ValidateCaptureEntries(
        IEnumerable<PowerForgeServerManagedFile> entries,
        string section,
        bool sensitive,
        ICollection<string> errors)
    {
        var paths = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var entry in entries)
        {
            var path = NormalizeCapturePath(entry.Target, $"{section}[{index}].target", errors);
            if (path is not null)
            {
                paths.Add(path);
                if (!seenPaths.Add(path))
                    errors.Add($"{section}[{index}] duplicates capture path '{path}'.");
                if (sensitive && path.IndexOfAny(['*', '?', '[']) >= 0)
                    errors.Add($"{section}[{index}] must use an exact path so restore can enforce an archive allowlist.");
            }
            if (entry.Sensitive is null)
            {
                if (sensitive)
                    errors.Add($"{section}[{index}] must explicitly set sensitive=true.");
            }
            else if (entry.Sensitive.Value != sensitive)
            {
                var expectation = sensitive ? "must" : "must not";
                errors.Add($"{section}[{index}] {expectation} set sensitive=true.");
            }
            index++;
        }
        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? NormalizeCapturePath(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
            return null;
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Contains('\\'))
        {
            errors.Add($"{field} must be a canonical Unix path without surrounding whitespace or backslashes.");
            return null;
        }

        var normalized = value.TrimEnd('/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal) || normalized == "/")
        {
            errors.Add($"{field} must be an absolute path below '/'.");
            return null;
        }

        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
        {
            errors.Add($"{field} must not contain '.' or '..' segments.");
            return null;
        }

        if (normalized.Contains("//", StringComparison.Ordinal))
        {
            errors.Add($"{field} must not contain empty path segments.");
            return null;
        }

        if (normalized.Any(static character => !(IsAsciiLetterOrDigit(character) || character is '/' or '.' or '_' or '-' or '*' or '?' or '[' or ']')))
        {
            errors.Add($"{field} contains unsupported characters.");
            return null;
        }

        return normalized;
    }

    private static bool CapturePathsMayOverlap(string left, string right)
    {
        var leftPrefix = GetCaptureStaticPrefix(left);
        var rightPrefix = GetCaptureStaticPrefix(right);
        return PathContains(leftPrefix, rightPrefix) || PathContains(rightPrefix, leftPrefix);
    }

    private static string GetCaptureStaticPrefix(string path)
    {
        var wildcardIndex = path.IndexOfAny(['*', '?', '[']);
        if (wildcardIndex < 0)
            return path;

        var slashIndex = path.LastIndexOf('/', wildcardIndex);
        return slashIndex <= 0 ? "/" : path[..slashIndex];
    }

    private static bool PathContains(string parent, string candidate)
        => candidate.Equals(parent, StringComparison.Ordinal) ||
           candidate.StartsWith(parent.EndsWith("/", StringComparison.Ordinal) ? parent : parent + "/", StringComparison.Ordinal);

    private static bool IsValidUnixIdentity(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           (value.Length <= 32 &&
            (IsAsciiLetterOrDigit(value[0]) || value[0] == '_') &&
            value.All(static character => IsAsciiLetterOrDigit(character) || character is '_' or '-'));

    private static bool IsValidMode(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           (value.Length is 3 or 4 && value.All(static character => character is >= '0' and <= '7'));

    private static bool IsAsciiLetterOrDigit(char value)
        => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool IsRepositoryName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(static part =>
            part.Length > 0 && part.All(static character => IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-'));
    }

    private static bool IsSafeRepositoryRelativeValue(string? value, bool allowSlash)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.StartsWith("/", StringComparison.Ordinal) &&
           !value.Contains("..", StringComparison.Ordinal) &&
           value.All(character => IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-' || (allowSlash && character == '/'));

    private static bool IsEnvironmentVariableName(string value)
        => value.Length > 0 &&
           (IsAsciiLetterOrDigit(value[0]) || value[0] == '_') &&
           !char.IsDigit(value[0]) &&
           value.All(static character => IsAsciiLetterOrDigit(character) || character == '_');
}
