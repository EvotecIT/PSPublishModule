using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

internal static class BenchmarkFileUpdateLock
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    internal static FileStream Acquire(string destinationPath)
    {
        string lockDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))
                               ?? throw new InvalidOperationException(
                                   $"Unable to determine the benchmark evidence directory for '{destinationPath}'.");
        Directory.CreateDirectory(lockDirectory);
        string lockPath = CreateLockPath(destinationPath);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                FileStream stream = OpenLockFile(lockPath, lockDirectory);
                return stream;
            }
            catch (IOException)
            {
                if (stopwatch.Elapsed >= DefaultTimeout)
                    throw new TimeoutException(
                        $"Timed out waiting to update benchmark evidence catalog '{destinationPath}'.");
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException)
            {
                if (stopwatch.Elapsed >= DefaultTimeout)
                    throw new TimeoutException(
                        $"Timed out waiting to update benchmark evidence catalog '{destinationPath}'.");
                Thread.Sleep(25);
            }
        }
    }

    private static FileStream OpenLockFile(string lockPath, string lockDirectory)
    {
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            EnsureUnixLockPathIsNotSymbolicLink(lockPath);
            UnixFileMode requestedMode = GetUnixLockMode(lockDirectory);
            var stream = new FileStream(
                lockPath,
                new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    BufferSize = 1,
                    Options = FileOptions.None,
                    UnixCreateMode = requestedMode
                });
            try
            {
                EnsureUnixLockPathIsNotSymbolicLink(lockPath);
                UnixFileMode currentMode = File.GetUnixFileMode(lockPath);
                if ((currentMode & requestedMode) != requestedMode)
                    File.SetUnixFileMode(lockPath, currentMode | requestedMode);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
#endif
        return new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None);
    }

#if NET8_0_OR_GREATER
    private static UnixFileMode GetUnixLockMode(string lockDirectory)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Unix lock modes are not available on Windows.");

        UnixFileMode directoryMode = File.GetUnixFileMode(lockDirectory);
        UnixFileMode lockMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if ((directoryMode & UnixFileMode.GroupWrite) != 0)
            lockMode |= UnixFileMode.GroupRead | UnixFileMode.GroupWrite;
        if ((directoryMode & UnixFileMode.OtherWrite) != 0)
            lockMode |= UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
        return lockMode;
    }

    private static void EnsureUnixLockPathIsNotSymbolicLink(string lockPath)
    {
        if (new FileInfo(lockPath).LinkTarget is not null)
        {
            throw new InvalidOperationException(
                $"Benchmark evidence lock path '{lockPath}' is a symbolic link. Remove it before updating the catalog.");
        }
    }
#endif

    internal static string CreateLockPath(string destinationPath)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullPath)
                           ?? throw new InvalidOperationException(
                               $"Unable to determine the benchmark evidence directory for '{destinationPath}'.");
        return Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{CreatePathHash(fullPath)}.lock");
    }

    internal static string CreatePathHash(string destinationPath, bool? caseInsensitive = null)
    {
        string normalizedPath = Path.GetFullPath(destinationPath);
        bool normalizeCase = caseInsensitive ?? IsCaseInsensitivePath(normalizedPath);
        if (normalizeCase)
            normalizedPath = normalizedPath.ToUpperInvariant();
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }

    internal static bool IsCaseInsensitivePath(string destinationPath)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            string probeName = ".PfCaseProbe-" + Guid.NewGuid().ToString("N");
            string probePath = Path.Combine(directory, probeName);
            string alternatePath = Path.Combine(directory, probeName.ToLowerInvariant());
            try
            {
                using (new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose))
                {
                    return File.Exists(alternatePath);
                }
            }
            catch (IOException)
            {
                // Fall back to the platform default when the target volume cannot be probed.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall back to the platform default when the target volume cannot be probed.
            }
            finally
            {
                try
                {
                    if (File.Exists(probePath))
                        File.Delete(probePath);
                }
                catch
                {
                    // A DeleteOnClose probe may already be gone or independently protected.
                }
            }
        }

        return Path.DirectorySeparatorChar == '\\' ||
               RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }
}
