using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Owns an exclusive lease for one mutating project-build workspace.
/// </summary>
internal sealed class ProjectBuildOperationLock : IDisposable
{
    private readonly FileStream _stream;

    private ProjectBuildOperationLock(FileStream stream)
    {
        _stream = stream;
    }

    internal static ProjectBuildOperationLock Acquire(ProjectBuildPreparedContext preparation)
    {
        if (preparation is null)
            throw new ArgumentNullException(nameof(preparation));

        var workspacePath = ResolveWorkspacePath(preparation);
        var lockPath = ResolveLockPath(workspacePath);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        FileStream? stream = null;
        try
        {
            stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            stream.SetLength(0);
            var payload = Encoding.UTF8.GetBytes(
                $"pid={System.Diagnostics.Process.GetCurrentProcess().Id}{Environment.NewLine}" +
                $"workspace={workspacePath}{Environment.NewLine}" +
                $"startedAt={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
            stream.Write(payload, 0, payload.Length);
            stream.Flush(flushToDisk: true);
            return new ProjectBuildOperationLock(stream);
        }
        catch (IOException exception)
        {
            stream?.Dispose();
            throw new InvalidOperationException(
                $"Another project build is already using workspace '{workspacePath}'. " +
                "Wait for it to finish before starting another build or publish operation that uses the same staging/output paths.",
                exception);
        }
    }

    internal static string ResolveLockPath(string workspacePath)
    {
        var normalizedPath = Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.DirectorySeparatorChar == '\\')
            normalizedPath = normalizedPath.ToUpperInvariant();

        byte[] hash;
        using (var algorithm = SHA256.Create())
            hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));

        var key = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), "PowerForge", "project-build-locks", $"{key}.lock");
    }

    private static string ResolveWorkspacePath(ProjectBuildPreparedContext preparation)
    {
        var workspacePath = preparation.StagingPath;
        if (string.IsNullOrWhiteSpace(workspacePath))
            workspacePath = preparation.OutputPath;
        if (string.IsNullOrWhiteSpace(workspacePath))
            workspacePath = preparation.ReleaseZipOutputPath;
        if (string.IsNullOrWhiteSpace(workspacePath))
            workspacePath = preparation.RootPath;
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new InvalidOperationException("Project build workspace could not be resolved.");

        return Path.GetFullPath(workspacePath);
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
