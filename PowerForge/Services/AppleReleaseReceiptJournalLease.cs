using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Serializes receipt-chain transactions that share either the latest receipt or immutable history path.
/// </summary>
internal sealed class AppleReleaseReceiptJournalLease : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly FileStream[] _streams;

    /// <summary>Existing canonical lock files held by this lease.</summary>
    internal string[] Paths { get; private set; } = Array.Empty<string>();

    private AppleReleaseReceiptJournalLease(FileStream[] streams)
    {
        _streams = streams;
    }

    internal static AppleReleaseReceiptJournalLease Acquire(PowerForgeAppleReleasePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var lockPaths = GetLockPaths(plan);
        var streams = new List<FileStream>(lockPaths.Length);
        try
        {
            foreach (var lockPath in lockPaths)
                streams.Add(AcquireOne(lockPath));
            return new AppleReleaseReceiptJournalLease(streams.ToArray()) { Paths = lockPaths };
        }
        catch
        {
            for (var index = streams.Count - 1; index >= 0; index--)
                streams[index].Dispose();
            throw;
        }
    }

    /// <summary>
    /// Exclusively probes existing, empty journal locks without creating files, changing
    /// permissions, or waiting for an active writer. Keep this lease during evidence reads.
    /// </summary>
    internal static AppleReleaseReceiptJournalLease InspectExisting(PowerForgeAppleReleasePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        var streams = new List<FileStream>();
        var paths = new List<string>();
        try
        {
            foreach (var path in GetLockPaths(plan))
            {
                AppleReleaseReceiptStore.EnsureSafeOutputPath(plan.ProjectRoot, path, "Apple receipt journal lock");
                if (Directory.Exists(path))
                    throw new InvalidOperationException($"Apple receipt journal lock must be a regular file: {path}");
                if (!File.Exists(path))
                    continue;
                var before = ExistingFilePathIdentityResolver.CapturePrivateFileMutationIdentity(path, "Apple receipt journal lock");
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1);
                streams.Add(stream);
                if (stream.Length != 0)
                    throw new InvalidOperationException($"Apple receipt journal lock is not empty: {path}");
                var after = ExistingFilePathIdentityResolver.ResolveStatus(stream.SafeFileHandle).MutationIdentity;
                if (!string.Equals(before, after, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Apple receipt journal lock changed during inspection: {path}");
                var hardLinkCount = Path.DirectorySeparatorChar == '\\'
                    ? ExistingFilePathIdentityResolver.ResolveHardLinkCount(stream.SafeFileHandle)
                    : ExistingFilePathIdentityResolver.ResolveHardLinkCounts(new[] { path })[0];
                if (hardLinkCount != 1)
                    throw new InvalidOperationException($"Apple receipt journal lock has multiple hard links: {path}");
                paths.Add(path);
            }
            return new AppleReleaseReceiptJournalLease(streams.ToArray()) { Paths = paths.ToArray() };
        }
        catch (Exception exception)
        {
            for (var index = streams.Count - 1; index >= 0; index--)
                streams[index].Dispose();
            throw new InvalidOperationException($"Apple receipt journal locks must be inactive, empty regular files before local evidence inspection. {exception.Message}", exception);
        }
    }

    private static string[] GetLockPaths(PowerForgeAppleReleasePlan plan)
        => new[]
            {
                CreateLockPath(plan.ReceiptPath),
                CreateLockPath(plan.ReceiptHistoryPath)
            }
            .Distinct(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

    private static FileStream AcquireOne(string lockPath)
    {
        var directory = Path.GetDirectoryName(lockPath)
                        ?? throw new InvalidOperationException($"Apple receipt journal lock path has no parent: {lockPath}");
        Directory.CreateDirectory(directory);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Apple receipt journal lock root must not be a symbolic link: {directory}");
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
#endif
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (File.Exists(lockPath) &&
                    (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Apple receipt journal lock must not be a symbolic link or reparse point: {lockPath}");
                }

#if NET8_0_OR_GREATER
                if (!OperatingSystem.IsWindows())
                {
                    return new FileStream(
                        lockPath,
                        new FileStreamOptions
                        {
                            Mode = FileMode.OpenOrCreate,
                            Access = FileAccess.ReadWrite,
                            Share = FileShare.None,
                            BufferSize = 1,
                            Options = FileOptions.None,
                            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                        });
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
            catch (IOException exception)
            {
                if (stopwatch.Elapsed < DefaultTimeout)
                {
                    Thread.Sleep(25);
                    continue;
                }

                throw new TimeoutException(
                    $"Timed out waiting for another process to finish updating the Apple receipt journal protected by '{lockPath}'.",
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                if (stopwatch.Elapsed < DefaultTimeout)
                {
                    Thread.Sleep(25);
                    continue;
                }

                throw new TimeoutException(
                    $"Timed out waiting for another process to finish updating the Apple receipt journal protected by '{lockPath}'.",
                    exception);
            }
        }
    }

    internal static string CreateLockPath(string resourcePath)
    {
        var fullPath = Path.GetFullPath(resourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hashPath = FrameworkCompatibility.GetPathStringComparisonForPath(fullPath) == StringComparison.OrdinalIgnoreCase
            ? fullPath.ToUpperInvariant()
            : fullPath;
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashPath));
        var key = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        var resourceDirectory = Path.GetDirectoryName(fullPath)
                                ?? throw new InvalidOperationException(
                                    $"Apple receipt journal resource path has no parent: {resourcePath}");
        return Path.Combine(
            resourceDirectory,
            ".powerforge-receipt-journal-locks",
            $"{key}.lock");
    }

    public void Dispose()
    {
        for (var index = _streams.Length - 1; index >= 0; index--)
            _streams[index].Dispose();
    }
}
