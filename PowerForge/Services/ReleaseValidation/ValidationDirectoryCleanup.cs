namespace PowerForge;

/// <summary>Best-effort removal of disposable validation directories without replacing the probe result.</summary>
internal static class ValidationDirectoryCleanup
{
    internal static void TryDelete(string path, Action<string>? warning = null)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { DeleteTree(new DirectoryInfo(path)); return; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt < 2) { Thread.Sleep(25); continue; }
                var message = $"Validation temporary directory could not be removed: {path}. {exception.Message}";
                // A logging sink must not replace success, failure, or caller cancellation either.
                try
                {
                    if (warning is not null) warning(message);
                    else System.Diagnostics.Trace.TraceWarning(message);
                }
                catch { }
            }
        }
    }

    private static void DeleteTree(DirectoryInfo directory)
    {
        if (!directory.Exists) return;
        // Probes can create links; remove the link itself, never its target.
        if ((directory.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            foreach (var entry in directory.GetFileSystemInfos())
            {
                if (entry is DirectoryInfo child) DeleteTree(child);
                else
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                    entry.Delete();
                }
            }
        }
        directory.Delete();
    }
}
