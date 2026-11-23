namespace PowerForge;

/// <summary>Owns disposable compiler workspaces and safely scavenges abandoned compiler-only state.</summary>
internal sealed class PowerShellCompilationWorkspace : IDisposable
{
    private const string WorkspacePrefix = "ps-";
    private const string OwnershipFileName = ".powerforge-compiler-workspace";
    private const string LockFileName = ".powerforge-active.lock";
    private const string KeepFileName = ".powerforge-keep";
    private static readonly TimeSpan StaleAge = TimeSpan.FromHours(24);
    private FileStream? _lock;
    private readonly bool _keep;

    private PowerShellCompilationWorkspace(string path, FileStream workspaceLock, bool keep)
    {
        Path = path;
        _lock = workspaceLock;
        _keep = keep;
    }

    internal string Path { get; }

    internal static PowerShellCompilationWorkspace Create(bool keep, bool offlineRestore = false)
    {
        var powerForgeTempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PowerForge");
        var workspaceRoot = System.IO.Path.Combine(powerForgeTempRoot, "powershell-compilation");
        Directory.CreateDirectory(workspaceRoot);
        CleanupStaleWorkspaces(workspaceRoot, DateTime.UtcNow - StaleAge);

        var path = System.IO.Path.Combine(workspaceRoot, WorkspacePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        PowerShellCompilationBuildIsolation.Write(path, requireSdkSelection: false, offlineRestore);
        File.WriteAllText(System.IO.Path.Combine(path, OwnershipFileName), "PowerForge PowerShell compilation workspace.");
        if (keep)
            File.WriteAllText(System.IO.Path.Combine(path, KeepFileName), "This generated workspace was retained by KeepBuildWorkspace.");
        var workspaceLock = new FileStream(
            System.IO.Path.Combine(path, LockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        return new PowerShellCompilationWorkspace(path, workspaceLock, keep);
    }

    internal static int CleanupStaleWorkspaces(string root, DateTime cutoffUtc)
    {
        if (!Directory.Exists(root)) return 0;
        var removed = 0;
        foreach (var directory in Directory.EnumerateDirectories(root, WorkspacePrefix + "*", SearchOption.TopDirectoryOnly))
        {
            if (!IsDirectCompilerWorkspace(root, directory)) continue;
            if (!File.Exists(System.IO.Path.Combine(directory, OwnershipFileName))) continue;
            if (File.Exists(System.IO.Path.Combine(directory, KeepFileName))) continue;
            if (Directory.GetLastWriteTimeUtc(directory) > cutoffUtc) continue;
            FileStream? workspaceLock = null;
            try
            {
                workspaceLock = new FileStream(
                    System.IO.Path.Combine(directory, LockFileName),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            workspaceLock.Dispose();
            if (TryDeleteDirectory(directory)) removed++;
        }

        return removed;
    }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
        if (!_keep)
            TryDeleteDirectory(Path);
    }

    private static bool IsDirectCompilerWorkspace(string root, string directory)
    {
        var fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var fullDirectory = System.IO.Path.GetFullPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return System.IO.Path.GetFileName(fullDirectory).StartsWith(WorkspacePrefix, StringComparison.OrdinalIgnoreCase) &&
               PowerShellCompilationPathSafety.PathEquals(System.IO.Path.GetDirectoryName(fullDirectory), fullRoot);
    }

    private static bool TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (!Directory.Exists(path)) return true;
            try
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 3)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch
            {
                return false;
            }
        }
        return !Directory.Exists(path);
    }
}
