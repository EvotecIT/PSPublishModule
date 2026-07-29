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
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
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
        bool normalizeCase = caseInsensitive ??
            (Path.DirectorySeparatorChar == '\\' ||
             RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
        if (normalizeCase)
            normalizedPath = normalizedPath.ToUpperInvariant();
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }
}
