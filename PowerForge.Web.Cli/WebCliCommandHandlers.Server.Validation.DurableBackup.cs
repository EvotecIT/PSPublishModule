namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static void ValidateDurableBackup(
        PowerForgeServerDurableBackup? backup,
        ICollection<string> errors)
    {
        if (backup is null)
            return;

        var exportRoot = NormalizeCapturePath(backup.ExportRoot, "durableBackup.exportRoot", errors);
        if (exportRoot is not null)
        {
            if (exportRoot is "/" or "/etc" or "/home" or "/root" or "/srv" or "/var" or "/var/lib")
                errors.Add("durableBackup.exportRoot must be a dedicated directory below a system data root.");
            if (HasTrailingPathSeparator(backup.ExportRoot))
                errors.Add("durableBackup.exportRoot must not end with '/'.");
        }

        if (!IsValidDurableUnixName(backup.ExportGroup))
            errors.Add("durableBackup.exportGroup must be a valid Linux group name.");
        if (backup.StagingRetentionHours is < 24 or > 720)
            errors.Add("durableBackup.stagingRetentionHours must be from 24 through 720.");

        var recipient = backup.Recipient;
        if (string.IsNullOrWhiteSpace(recipient) ||
            recipient.Length <= 4 ||
            !recipient.StartsWith("age1", StringComparison.Ordinal) ||
            recipient.Any(static character => !(character is >= 'a' and <= 'z' || character is >= '0' and <= '9')))
            errors.Add("durableBackup requires a literal age public recipient in durableBackup.recipient.");

        var databaseIds = new HashSet<string>(StringComparer.Ordinal);
        var databaseNames = new HashSet<string>(StringComparer.Ordinal);
        var databases = backup.Databases ?? Array.Empty<PowerForgeServerDurableBackupDatabase>();
        if (databases.Length == 0)
            errors.Add("durableBackup.databases requires at least one database.");
        foreach (var database in databases)
        {
            if (string.IsNullOrWhiteSpace(database.Id) || !IsSafeIdentifier(database.Id) || !databaseIds.Add(database.Id))
                errors.Add($"Durable backup database id '{database.Id}' is missing, unsafe, or duplicated.");
            if (!string.Equals(database.Provider, "postgresql", StringComparison.Ordinal))
                errors.Add($"Durable backup database '{database.Id}' provider must be postgresql.");
            if (string.IsNullOrWhiteSpace(database.Database) ||
                database.Database.Length > 63 ||
                !(database.Database[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_') ||
                database.Database.Any(static character => !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) ||
                !databaseNames.Add(database.Database))
                errors.Add($"Durable backup database name '{database.Database}' is missing, unsafe, or duplicated.");
        }

        var encryptedFiles = backup.EncryptedFiles ?? Array.Empty<PowerForgeServerManagedFile>();
        if (encryptedFiles.Length == 0)
            errors.Add("durableBackup.encryptedFiles requires at least one file.");
        var encryptedPaths = ValidateCaptureEntries(encryptedFiles, "durableBackup.encryptedFiles", sensitive: true, errors);

        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        var artifactPaths = new HashSet<string>(StringComparer.Ordinal);
        var artifacts = backup.ArtifactStores ?? Array.Empty<PowerForgeServerDurableBackupArtifactStore>();
        if (artifacts.Length == 0)
            errors.Add("durableBackup.artifactStores requires at least one immutable artifact store.");
        foreach (var artifact in artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Id) || !IsSafeIdentifier(artifact.Id) || !artifactIds.Add(artifact.Id))
                errors.Add($"Durable backup artifact id '{artifact.Id}' is missing, unsafe, or duplicated.");
            var path = NormalizeCapturePath(artifact.Path, $"durableBackup.artifactStores[{artifact.Id}].path", errors);
            if (path is null)
                continue;
            if (HasTrailingPathSeparator(artifact.Path) || path.IndexOfAny(['*', '?', '[']) >= 0 || !artifactPaths.Add(path))
                errors.Add($"Durable backup artifact path '{artifact.Path}' must be exact, unique, and must not end with '/'.");
            if (exportRoot is not null && (PathContains(exportRoot, path) || PathContains(path, exportRoot)))
                errors.Add($"Durable backup artifact path '{path}' must not overlap durableBackup.exportRoot.");
        }

        if (exportRoot is not null && encryptedPaths.Any(path => PathContains(exportRoot, path) || PathContains(path, exportRoot)))
            errors.Add("durableBackup.encryptedFiles must not overlap durableBackup.exportRoot.");
        foreach (var encryptedPath in encryptedPaths)
        {
            foreach (var artifactPath in artifactPaths)
            {
                if (PathContains(encryptedPath, artifactPath) || PathContains(artifactPath, encryptedPath))
                    errors.Add($"Durable encrypted path '{encryptedPath}' must not overlap artifact store '{artifactPath}'.");
            }
        }
    }

    private static bool IsValidDurableUnixName(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 32 &&
           (value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_') &&
           value.All(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
}
