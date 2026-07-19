namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static void ValidateOperationLocks(
        PowerForgeServerRecoveryManifest manifest,
        ICollection<string> errors)
    {
        var locks = manifest.OperationLocks ?? Array.Empty<string>();
        var deployCommands = manifest.Deploy?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>();
        var lockOwner = string.IsNullOrWhiteSpace(manifest.Deploy?.OperationLockOwner)
            ? "engine"
            : manifest.Deploy.OperationLockOwner;
        var normalizedLocks = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < locks.Length; index++)
        {
            var path = NormalizeCapturePath(locks[index], $"operationLocks[{index}]", errors);
            if (path is null)
                continue;
            normalizedLocks.Add(path);
            if (!IsValidOperationLockPath(path))
                errors.Add($"operationLocks[{index}] must be an exact .lock file directly below /var/lock.");
            if (!seen.Add(path))
                errors.Add($"operationLocks[{index}] duplicates lock path '{path}'.");
        }

        if (manifest.Capture is not null && deployCommands.Length > 0 && locks.Length == 0)
            errors.Add("operationLocks is required when a manifest contains both capture and deploy work.");
        if (lockOwner is not ("engine" or "command"))
            errors.Add("deploy.operationLockOwner must be 'engine' or 'command'.");
        if (string.Equals(lockOwner, "command", StringComparison.Ordinal))
        {
            if (locks.Length == 0)
                errors.Add("Command-owned deploy locking requires at least one declared operation lock.");
            if (deployCommands.Length != 1 || deployCommands[0].Sensitive)
                errors.Add("Command-owned deploy locking requires exactly one non-sensitive deploy command.");
            else if (deployCommands[0].Command?.Contains("/usr/local/sbin/powerforge-site-deploy", StringComparison.Ordinal) == true)
            {
                if (!TryGetPowerForgeSiteDeploySite(deployCommands[0].Command, out var site))
                {
                    errors.Add("Command-owned powerforge-site-deploy must use the canonical '[sudo -n] /usr/local/sbin/powerforge-site-deploy --site <id>' prefix.");
                }
                else
                {
                    var expectedLock = $"/var/lock/powerforge-site-{site}.lock";
                    if (normalizedLocks.Count != 1 || !string.Equals(normalizedLocks[0], expectedLock, StringComparison.Ordinal))
                        errors.Add($"Command-owned powerforge-site-deploy for site '{site}' requires exactly operationLocks ['{expectedLock}'].");
                }
            }
        }
        else if (deployCommands.Any(static command =>
                     command.Command?.Contains("powerforge-site-deploy", StringComparison.Ordinal) == true))
        {
            errors.Add("A deploy command that invokes powerforge-site-deploy must set deploy.operationLockOwner to 'command' to avoid nested lock acquisition.");
        }

    }

    private static bool TryGetPowerForgeSiteDeploySite(string? command, out string site)
    {
        var normalized = command?.Trim() ?? string.Empty;
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var commandIndex = tokens.Length >= 2 &&
                           string.Equals(tokens[0], "sudo", StringComparison.Ordinal) &&
                           string.Equals(tokens[1], "-n", StringComparison.Ordinal)
            ? 2
            : 0;
        if (tokens.Length <= commandIndex + 2 ||
            !string.Equals(tokens[commandIndex], "/usr/local/sbin/powerforge-site-deploy", StringComparison.Ordinal) ||
            !string.Equals(tokens[commandIndex + 1], "--site", StringComparison.Ordinal) ||
            tokens.Skip(commandIndex + 3).Any(static token => string.Equals(token, "--site", StringComparison.Ordinal)) ||
            tokens.Any(static token => token.Length == 0 || token.Any(static character =>
                !(IsAsciiLetterOrDigit(character) || character is '/' or '.' or '_' or '-' or ':'))))
        {
            site = string.Empty;
            return false;
        }

        site = tokens[commandIndex + 2];
        return site.Length is >= 1 and <= 63 &&
               site[0] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
               site.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-');
    }

    private static void ValidateLetsEncryptCapturePaths(
        IEnumerable<string> plainPaths,
        IEnumerable<string> encryptedPaths,
        ICollection<string> errors)
    {
        foreach (var plainPath in plainPaths.Where(IsLetsEncryptPrivateStatePath))
            errors.Add($"Let's Encrypt private state path '{plainPath}' must use encrypted capture.");
        foreach (var encryptedPath in encryptedPaths.Where(IsOverbroadLetsEncryptCapturePath))
            errors.Add($"Encrypted capture path '{encryptedPath}' is too broad; capture an exact certificate lineage or one exact ACME account directory.");
    }

    private static bool IsLetsEncryptPrivateStatePath(string path)
        => new[]
        {
            "/etc/letsencrypt/accounts",
            "/etc/letsencrypt/archive",
            "/etc/letsencrypt/live"
        }.Any(root => PathContains(root, path) || PathContains(path, root));

    private static bool IsOverbroadLetsEncryptCapturePath(string path)
    {
        const string accountsRoot = "/etc/letsencrypt/accounts";
        if (PathContains(path, accountsRoot))
            return true;
        if (PathStrictlyContains(accountsRoot, path))
        {
            var accountSegments = path[(accountsRoot.Length + 1)..]
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            return accountSegments.Length != 3;
        }

        foreach (var lineageRoot in new[] { "/etc/letsencrypt/archive", "/etc/letsencrypt/live" })
        {
            if (PathContains(path, lineageRoot))
                return true;
            if (PathStrictlyContains(lineageRoot, path) &&
                path[(lineageRoot.Length + 1)..].Contains('/', StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
