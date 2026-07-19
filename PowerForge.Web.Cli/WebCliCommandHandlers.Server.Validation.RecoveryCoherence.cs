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
        }
        else if (deployCommands.Any(static command =>
                     command.Command?.Contains("powerforge-site-deploy", StringComparison.Ordinal) == true))
        {
            errors.Add("A deploy command that invokes powerforge-site-deploy must set deploy.operationLockOwner to 'command' to avoid nested lock acquisition.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < locks.Length; index++)
        {
            var path = NormalizeCapturePath(locks[index], $"operationLocks[{index}]", errors);
            if (path is null)
                continue;
            if (!IsValidOperationLockPath(path))
                errors.Add($"operationLocks[{index}] must be an exact .lock file directly below /var/lock.");
            if (!seen.Add(path))
                errors.Add($"operationLocks[{index}] duplicates lock path '{path}'.");
        }
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
