using System.Diagnostics;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private readonly string _msiReservationOwner = Guid.NewGuid().ToString("N");

    internal static void EnsureVersionedOutputDoesNotExist(
        string? outputDirectory,
        string? outputName,
        string? version,
        bool allowOutputOverwrite,
        string installerId)
    {
        if (allowOutputOverwrite || string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(outputName))
        {
            throw new InvalidOperationException(
                $"Versioned MSI installer '{installerId}' requires explicit OutputPath and OutputName values " +
                "so immutable output protection can resolve the exact target file.");
        }

        var outputPath = Path.Combine(outputDirectory!, outputName! + ".msi");
        if (!File.Exists(outputPath))
            return;

        throw new InvalidOperationException(
            $"MSI output '{outputPath}' already exists for immutable version {version}. " +
            $"Advance the version state for installer '{installerId}' or explicitly set " +
            $"Versioning.AllowOutputOverwrite=true for a non-release rebuild.");
    }

    internal static IDisposable ReserveVersionedOutput(
        string? outputDirectory,
        string? outputName,
        string? version,
        bool allowOutputOverwrite,
        string installerId,
        string reservationOwner)
    {
        EnsureVersionedOutputDoesNotExist(
            outputDirectory,
            outputName,
            version,
            allowOutputOverwrite,
            installerId);
        if (allowOutputOverwrite || string.IsNullOrWhiteSpace(version))
            return EmptyReservation.Instance;

        var outputPath = Path.Combine(outputDirectory!, outputName! + ".msi");
        var reservationPath = outputPath + ".powerforge-reservation";
        FileStream stream;
        try
        {
            stream = new FileStream(
                reservationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"MSI output '{outputPath}' is already reserved by another publish. " +
                "Wait for that publish to finish or advance the version state.",
                ex);
        }

        try
        {
            if (File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"MSI output '{outputPath}' appeared while immutable version {version} was being reserved.");
            }

            var reservation = Encoding.UTF8.GetBytes(
                $"owner={reservationOwner}{Environment.NewLine}" +
                $"version={version}{Environment.NewLine}" +
                $"createdUtc={DateTime.UtcNow:o}{Environment.NewLine}");
            stream.Write(reservation, 0, reservation.Length);
            stream.Flush(flushToDisk: true);
            return new OutputReservation(stream, reservationPath);
        }
        catch
        {
            stream.Dispose();
            TryDeleteReservation(reservationPath);
            throw;
        }
    }

    internal static SourceProvenance ReadSourceProvenance(string projectRoot)
    {
        var gitRevision = ReadGitText(projectRoot, "rev-parse HEAD");
        var environmentRevision = Environment.GetEnvironmentVariable("GITHUB_SHA")?.Trim();
        var revision = string.IsNullOrWhiteSpace(gitRevision) ? environmentRevision : gitRevision;
        var status = ReadGitText(projectRoot, "status --porcelain --untracked-files=all");
        return new SourceProvenance(
            string.IsNullOrWhiteSpace(revision) ? null : revision,
            status is null ? null : status.Length > 0);
    }

    private static string? ReadGitText(string projectRoot, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(5000) || process.ExitCode != 0) return null;
            return output;
        }
        catch
        {
            return null;
        }
    }

    internal sealed class SourceProvenance
    {
        public SourceProvenance(string? revision, bool? dirty)
        {
            Revision = revision;
            Dirty = dirty;
        }

        public string? Revision { get; }

        public bool? Dirty { get; }
    }

    private static void TryDeleteReservation(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A stale reservation sidecar is safer than silently reusing an artifact identity.
        }
    }

    private sealed class OutputReservation : IDisposable
    {
        private readonly string _path;
        private FileStream? _stream;

        public OutputReservation(FileStream stream, string path)
        {
            _stream = stream;
            _path = path;
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
            TryDeleteReservation(_path);
        }
    }

    private sealed class EmptyReservation : IDisposable
    {
        internal static EmptyReservation Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
