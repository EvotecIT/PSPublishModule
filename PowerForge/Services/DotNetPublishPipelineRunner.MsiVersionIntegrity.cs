using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, MsiVersionStateWrite>>
        MsiVersionStateWrites = new(StringComparer.Ordinal);

    internal static string CreateMsiReservationOwner()
        => Guid.NewGuid().ToString("N");

    internal static void EnsureVersionedOutputDoesNotExist(
        string? outputDirectory,
        string? outputName,
        string? version,
        bool allowOutputOverwrite,
        string installerId)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(outputName))
        {
            throw new InvalidOperationException(
                $"Versioned MSI installer '{installerId}' requires explicit OutputPath and OutputName values " +
                "so immutable output protection can resolve the exact target file.");
        }

        if (allowOutputOverwrite)
        {
            return;
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
        if (string.IsNullOrWhiteSpace(version))
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
            if (!allowOutputOverwrite && File.Exists(outputPath))
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

    internal static SourceProvenance ReadSourceProvenance(
        string projectRoot,
        IEnumerable<string>? generatedPaths = null,
        IEnumerable<string>? trackedGeneratedPaths = null)
    {
        var gitRevision = ReadGitText(projectRoot, "rev-parse HEAD");
        var environmentRevision = Environment.GetEnvironmentVariable("GITHUB_SHA")?.Trim();
        var revision = string.IsNullOrWhiteSpace(gitRevision) ? environmentRevision : gitRevision;
        var gitRoot = ReadGitText(projectRoot, "rev-parse --show-toplevel");
        if (string.IsNullOrWhiteSpace(gitRoot))
        {
            return new SourceProvenance(
                string.IsNullOrWhiteSpace(revision) ? null : revision,
                null);
        }

        var trackedStatus = ReadGitRawText(gitRoot!, "status --porcelain=v1 -z --untracked-files=no");
        var untrackedOutput = ReadGitText(gitRoot!, "ls-files --others --exclude-standard -z");
        bool? dirty = trackedStatus is null || untrackedOutput is null
            ? null
            : HasTrackedSourceChanges(
                projectRoot,
                gitRoot!,
                trackedStatus,
                trackedGeneratedPaths)
              || HasUntrackedSourceFiles(
                projectRoot,
                gitRoot!,
                untrackedOutput,
                generatedPaths);
        return new SourceProvenance(
            string.IsNullOrWhiteSpace(revision) ? null : revision,
            dirty);
    }

    private static IEnumerable<string> EnumerateGeneratedProvenancePaths(
        DotNetPublishPlan plan,
        IEnumerable<DotNetPublishArtefactResult> artefacts,
        IEnumerable<DotNetPublishStorePackageResult> storePackages,
        IEnumerable<DotNetPublishMsiBuildResult> msiBuilds)
    {
        yield return plan.Outputs.ManifestJsonPath ?? string.Empty;
        yield return plan.Outputs.ManifestTextPath ?? string.Empty;
        yield return plan.Outputs.ChecksumsPath ?? string.Empty;
        yield return plan.Outputs.RunReportPath ?? string.Empty;
        yield return plan.Outputs.RunReportMarkdownPath ?? string.Empty;

        foreach (var version in plan.MsiVersions.Values)
            yield return version.StatePath ?? string.Empty;

        foreach (var step in plan.Steps)
        {
            yield return step.StagingPath ?? string.Empty;
            yield return step.ManifestPath ?? string.Empty;
            yield return step.HarvestPath ?? string.Empty;
            yield return step.BundleOutputPath ?? string.Empty;
            yield return step.BundleZipPath ?? string.Empty;
            yield return step.StorePackageOutputPath ?? string.Empty;
        }

        foreach (var artefact in artefacts)
        {
            yield return artefact.PublishDir;
            yield return artefact.OutputDir;
            yield return artefact.ZipPath ?? string.Empty;
            foreach (var outputFile in artefact.OutputFiles ?? Array.Empty<string>())
                yield return outputFile;
        }

        foreach (var storePackage in storePackages)
        {
            yield return storePackage.OutputDir;
            foreach (var outputFile in EnumerateStorePackageFiles(storePackage))
                yield return outputFile;
        }

        foreach (var msiBuild in msiBuilds)
        {
            yield return msiBuild.VersionStatePath ?? string.Empty;
            if (msiBuild.GeneratedProject)
                yield return Path.GetDirectoryName(msiBuild.ProjectPath) ?? string.Empty;
            foreach (var outputFile in msiBuild.OutputFiles ?? Array.Empty<string>())
                yield return outputFile;
        }
    }

    private static IEnumerable<string> EnumerateTrackedGeneratedProvenancePaths(
        DotNetPublishPlan plan,
        IEnumerable<DotNetPublishMsiBuildResult> msiBuilds)
    {
        foreach (var statePath in EnumeratePlannedMsiVersionStatePaths(plan))
            yield return statePath;

        foreach (var version in plan.MsiVersions.Values)
            yield return version.StatePath ?? string.Empty;

        foreach (var msiBuild in msiBuilds)
            yield return msiBuild.VersionStatePath ?? string.Empty;
    }

    internal static IEnumerable<string> EnumeratePlannedMsiVersionStatePaths(DotNetPublishPlan plan)
    {
        foreach (var step in plan.Steps.Where(step => step.Kind == DotNetPublishStepKind.MsiBuild))
        {
            var installer = plan.Installers.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, step.InstallerId, StringComparison.OrdinalIgnoreCase));
            if (installer?.Versioning is not { Enabled: true, Monotonic: true })
                continue;

            var tokens = BuildMsiVersionTemplateTokens(plan, installer, step);
            yield return ResolveMsiVersionStatePath(plan, installer, tokens);
        }
    }

    internal static void RecordMsiVersionStateWrite(
        string reservationOwner,
        string statePath,
        string previousContentHash,
        string contentHash)
    {
        if (string.IsNullOrWhiteSpace(reservationOwner)
            || string.IsNullOrWhiteSpace(statePath)
            || string.IsNullOrWhiteSpace(previousContentHash)
            || string.IsNullOrWhiteSpace(contentHash))
            return;

        var writes = MsiVersionStateWrites.GetOrAdd(
            reservationOwner,
            _ => new ConcurrentDictionary<string, MsiVersionStateWrite>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
        writes.AddOrUpdate(
            Path.GetFullPath(statePath),
            _ => new MsiVersionStateWrite(previousContentHash, contentHash, isContinuous: true),
            (_, priorWrite) => new MsiVersionStateWrite(
                priorWrite.PreviousContentHash,
                contentHash,
                priorWrite.IsContinuous
                && string.Equals(
                    priorWrite.ContentHash,
                    previousContentHash,
                    StringComparison.Ordinal)));
    }

    internal static string[] GetVerifiedMsiVersionStateWrites(
        string projectRoot,
        IReadOnlyDictionary<string, string> cleanTrackedGeneratedPaths,
        string reservationOwner)
    {
        if (!MsiVersionStateWrites.TryGetValue(reservationOwner, out var writes))
            return Array.Empty<string>();

        var gitRoot = ReadGitText(projectRoot, "rev-parse --show-toplevel");
        if (string.IsNullOrWhiteSpace(gitRoot))
            return Array.Empty<string>();

        var verified = new List<string>();
        foreach (var candidate in cleanTrackedGeneratedPaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate.Key);
                if (!writes.TryGetValue(fullPath, out var write)
                    || !write.IsContinuous
                    || !File.Exists(fullPath))
                    continue;

                var gitRelativePath = ToGitRelativeExclusion(projectRoot, gitRoot!, fullPath);
                if (string.IsNullOrWhiteSpace(gitRelativePath))
                    continue;

                var metadataDiff = ReadGitRawText(
                    gitRoot!,
                    $"diff --summary HEAD -- {QuoteGitPath(gitRelativePath!)}");
                if (metadataDiff is null || !string.IsNullOrWhiteSpace(metadataDiff))
                    continue;

                var actualHash = ComputeSha256Hex(File.ReadAllBytes(fullPath));
                if (string.Equals(write.PreviousContentHash, candidate.Value, StringComparison.Ordinal)
                    && string.Equals(actualHash, write.ContentHash, StringComparison.Ordinal))
                    verified.Add(fullPath);
            }
            catch
            {
                // Fail closed: an unreadable or invalid state path remains dirty.
            }
        }

        return verified.ToArray();
    }

    internal static IReadOnlyDictionary<string, string> CaptureCleanTrackedGeneratedProvenanceState(
        string projectRoot,
        IEnumerable<string>? generatedPaths)
    {
        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var state = new Dictionary<string, string>(comparison);
        foreach (var candidate in CaptureCleanTrackedGeneratedProvenancePaths(projectRoot, generatedPaths))
        {
            var fullPath = Path.GetFullPath(candidate);
            var content = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : Array.Empty<byte>();
            state[fullPath] = ComputeSha256Hex(content);
        }

        return state;
    }

    internal static void ClearMsiVersionStateWrites(string reservationOwner)
    {
        if (!string.IsNullOrWhiteSpace(reservationOwner))
            MsiVersionStateWrites.TryRemove(reservationOwner, out _);
    }

    private static string ComputeSha256Hex(byte[] content)
    {
        using var sha256 = SHA256.Create();
        return ToUpperHex(sha256.ComputeHash(content));
    }

    private static string ComputeSha256Hex(Stream stream)
    {
        using var sha256 = SHA256.Create();
        return ToUpperHex(sha256.ComputeHash(stream));
    }

    private static string ToUpperHex(byte[] value)
        => BitConverter.ToString(value).Replace("-", string.Empty);

    internal static string[] CaptureCleanTrackedGeneratedProvenancePaths(
        string projectRoot,
        IEnumerable<string>? generatedPaths)
    {
        var gitRoot = ReadGitText(projectRoot, "rev-parse --show-toplevel");
        if (string.IsNullOrWhiteSpace(gitRoot))
            return Array.Empty<string>();

        var trackedOutput = ReadGitRawText(gitRoot!, "status --porcelain=v1 -z --untracked-files=no");
        if (trackedOutput is null || !TryParseTrackedStatusPaths(trackedOutput, out var changedPaths))
            return Array.Empty<string>();

        var comparison = IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return (generatedPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectRoot, path)))
            .Select(path => new
            {
                FullPath = path,
                GitRelativePath = ToGitRelativeExclusion(projectRoot, gitRoot!, path)
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.GitRelativePath))
            .Where(candidate => !changedPaths.Any(path =>
                IsGeneratedPath(
                    path,
                    new[] { candidate.GitRelativePath! },
                    comparison)))
            .Select(candidate => candidate.FullPath)
            .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasUntrackedSourceFiles(
        string projectRoot,
        string gitRoot,
        string untrackedOutput,
        IEnumerable<string>? generatedPaths)
    {
        var comparison = IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var exclusions = BuildGeneratedPathExclusions(projectRoot, gitRoot, generatedPaths);

        foreach (var untrackedPath in untrackedOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedPath = untrackedPath.Replace('\\', '/').TrimStart('/');
            if (!IsGeneratedPath(normalizedPath, exclusions, comparison))
                return true;
        }

        return false;
    }

    private static bool HasTrackedSourceChanges(
        string projectRoot,
        string gitRoot,
        string trackedOutput,
        IEnumerable<string>? generatedPaths)
    {
        var comparison = IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var exclusions = BuildGeneratedPathExclusions(projectRoot, gitRoot, generatedPaths);
        if (!TryParseTrackedStatusPaths(trackedOutput, out var changedPaths))
            return true;

        foreach (var path in changedPaths)
        {
            if (!IsGeneratedPath(path, exclusions, comparison))
                return true;
        }

        return false;
    }

    private static bool TryParseTrackedStatusPaths(string trackedOutput, out string[] paths)
    {
        var parsed = new List<string>();
        var records = trackedOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (record.Length < 4 || record[2] != ' ')
            {
                paths = Array.Empty<string>();
                return false;
            }

            var status = record.Substring(0, 2);
            parsed.Add(record.Substring(3).Replace('\\', '/').TrimStart('/'));
            bool renameOrCopy = status.IndexOf('R') >= 0 || status.IndexOf('C') >= 0;
            if (!renameOrCopy)
                continue;

            if (++index >= records.Length)
            {
                paths = Array.Empty<string>();
                return false;
            }

            parsed.Add(records[index].Replace('\\', '/').TrimStart('/'));
        }

        paths = parsed.ToArray();
        return true;
    }

    private static string[] BuildGeneratedPathExclusions(
        string projectRoot,
        string gitRoot,
        IEnumerable<string>? generatedPaths)
        => (generatedPaths ?? Array.Empty<string>())
            .Select(path => ToGitRelativeExclusion(projectRoot, gitRoot, path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray()!;

    private static bool IsGeneratedPath(
        string path,
        IEnumerable<string> exclusions,
        StringComparison comparison)
        => exclusions.Any(exclusion =>
            string.Equals(path, exclusion, comparison)
            || path.StartsWith(exclusion + "/", comparison));

    private static string? ToGitRelativeExclusion(string projectRoot, string gitRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var root = Path.GetFullPath(gitRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(projectRoot, path));
        var comparison = IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(root, fullPath, comparison))
            return null;

        var rootPrefix = root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, comparison))
            return null;

        return fullPath.Substring(rootPrefix.Length)
            .Replace('\\', '/')
            .Trim('/');
    }

    private static string? ReadGitText(string projectRoot, string arguments)
    {
        var output = ReadGitRawText(projectRoot, arguments);
        return output?.Trim();
    }

    private static string? ReadGitRawText(string projectRoot, string arguments)
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
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000) || process.ExitCode != 0) return null;
            return output;
        }
        catch
        {
            return null;
        }
    }

    private static string QuoteGitPath(string path)
        => "\"" + path.Replace("\\", "/").Replace("\"", "\\\"") + "\"";

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

    private sealed class MsiVersionStateWrite
    {
        internal MsiVersionStateWrite(
            string previousContentHash,
            string contentHash,
            bool isContinuous)
        {
            PreviousContentHash = previousContentHash;
            ContentHash = contentHash;
            IsContinuous = isContinuous;
        }

        internal string PreviousContentHash { get; }

        internal string ContentHash { get; }

        internal bool IsContinuous { get; }
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
