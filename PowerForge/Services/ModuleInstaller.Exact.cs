namespace PowerForge;

public sealed partial class ModuleInstaller
{
    private void CommitExactVersion(
        string preparedPath,
        string finalPath,
        Action<string>? validateCommittedDestination)
    {
        string? backupPath = null;
        if (Directory.Exists(finalPath))
        {
            backupPath = EnsureChildPath(Path.GetDirectoryName(finalPath)!, $".backup_install_{Guid.NewGuid():N}");
            Directory.Move(finalPath, backupPath);
        }

        try
        {
            try
            {
                Directory.Move(preparedPath, finalPath);
            }
            catch (IOException) when (!Directory.Exists(finalPath))
            {
                CopyDirectory(preparedPath, finalPath);
            }
            catch (UnauthorizedAccessException) when (!Directory.Exists(finalPath))
            {
                CopyDirectory(preparedPath, finalPath);
            }
            validateCommittedDestination?.Invoke(finalPath);
        }
        catch (Exception installError)
        {
            try
            {
                if (Directory.Exists(finalPath))
                    Directory.Delete(finalPath, recursive: true);
                if (backupPath is not null)
                    Directory.Move(backupPath, finalPath);
            }
            catch (Exception rollbackError)
            {
                throw new InvalidOperationException(
                    $"Exact install failed and the prior version could not be restored. Backup: '{backupPath}'.",
                    new AggregateException(installError, rollbackError));
            }
            throw;
        }

        if (backupPath is not null)
        {
            try { Directory.Delete(backupPath, recursive: true); }
            catch (Exception ex) { _logger.Warn($"Exact install succeeded, but prior-version backup cleanup failed at '{backupPath}': {ex.Message}"); }
        }
    }
}
