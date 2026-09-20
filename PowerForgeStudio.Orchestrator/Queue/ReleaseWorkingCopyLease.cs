namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>Excludes cooperating artifact-mutating release operations sharing one local journal.</summary>
internal static class ReleaseWorkingCopyLease
{
    internal static FileStream Acquire(string databasePath, string root)
    {
        var normalized = Host.PowerForgeStudioHostPaths.NormalizeWorkspaceRoot(root);
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
        var directory = Path.GetFullPath(databasePath) + ".locks";
        Directory.CreateDirectory(directory);
        try { return new FileStream(Path.Combine(directory, key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("Another release operation may be using this working copy. Wait for it to finish before starting another session.", ex); }
    }
}
