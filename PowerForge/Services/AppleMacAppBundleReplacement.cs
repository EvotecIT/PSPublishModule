using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerForge;

internal static class AppleMacAppBundleReplacement
{
    private const int AtCurrentWorkingDirectory = -2;
    private const uint RenameSwap = 0x00000002;

    internal static FileStream AcquireInstallLock(string destination)
        => AppleLocalDeploymentLock.Acquire(Path.GetFullPath(destination), $"install destination '{destination}'");

    internal static string? RecoverInterruptedReplacement(string destination)
    {
        var installRoot = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"Install destination has no parent: {destination}");
        var appName = Path.GetFileName(destination);
        var backups = Directory.EnumerateDirectories(installRoot, $".{appName}.powerforge-backup-*")
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .ToList();

        if (!Directory.Exists(destination) && backups.Count > 0)
        {
            Directory.Move(backups[0], destination);
            backups.RemoveAt(0);
        }

        var warnings = new List<string>();
        foreach (var path in backups.Concat(Directory.EnumerateDirectories(installRoot, $".{appName}.powerforge-stage-*")))
        {
            if (!TryDeleteDirectory(path, out var warning) && warning is not null)
                warnings.Add(warning);
        }
        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }

    internal static string? Replace(
        string stage,
        string destination,
        string backup,
        Action validateStage)
    {
        if (validateStage is null)
            throw new ArgumentNullException(nameof(validateStage));

        if (!Directory.Exists(destination))
        {
            validateStage();
            Directory.Move(stage, destination);
            return null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            validateStage();
            if (RenameAtxNp(AtCurrentWorkingDirectory, stage, AtCurrentWorkingDirectory, destination, RenameSwap) != 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException($"Atomic app replacement failed: {new Win32Exception(error).Message}");
            }
            return TryDeleteDirectory(stage, out var warning) ? null : warning;
        }

        Directory.Move(destination, backup);
        try
        {
            validateStage();
            Directory.Move(stage, destination);
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }
        return TryDeleteDirectory(backup, out var fallbackWarning) ? null : fallbackWarning;
    }

    internal static bool TryDeleteDirectory(string path, out string? warning)
    {
        warning = null;
        if (!Directory.Exists(path))
            return true;

        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warning = $"Could not remove temporary app bundle '{path}': {exception.Message}";
            return false;
        }
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "renameatx_np", SetLastError = true)]
    private static extern int RenameAtxNp(int fromDirectory, string from, int toDirectory, string to, uint flags);
}
