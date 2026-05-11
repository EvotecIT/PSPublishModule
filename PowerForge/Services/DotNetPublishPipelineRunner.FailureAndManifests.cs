using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static string ExtractLastNonEmptyLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lines = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = (lines[i] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }

        return (text ?? string.Empty).Trim();
    }

    private static string ToSafeFileName(string? input, string fallback)
    {
        var s = string.IsNullOrWhiteSpace(input) ? fallback : input!.Trim();
        if (string.IsNullOrWhiteSpace(s)) s = fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var c = ch == ':' ? '_' : ch;
            if (Array.IndexOf(invalid, c) >= 0) sb.Append('_');
            else sb.Append(c);
        }

        return sb.ToString();
    }

    private static DotNetPublishFailure? BuildFailure(DotNetPublishPlan plan, Exception ex, out string errorMessage)
    {
        errorMessage = ex?.Message ?? "dotnet publish failed.";

        if (ex is not DotNetPublishStepException stepEx)
            return null;

        var inner = stepEx.InnerException ?? stepEx;
        errorMessage = inner.Message;

        var failure = new DotNetPublishFailure
        {
            StepKey = stepEx.Step.Key ?? string.Empty,
            StepKind = stepEx.Step.Kind,
            TargetName = stepEx.Step.TargetName,
            Framework = stepEx.Step.Framework,
            Runtime = stepEx.Step.Runtime,
            InstallerId = stepEx.Step.InstallerId,
            StorePackageId = stepEx.Step.StorePackageId,
            GateId = stepEx.Step.GateId,
        };

        if (inner is not DotNetPublishCommandException cmdEx)
            return failure;

        failure.ExitCode = cmdEx.ExitCode;
        failure.CommandLine = cmdEx.CommandLine;
        failure.WorkingDirectory = cmdEx.WorkingDirectory;
        failure.StdOutTail = TailLines(cmdEx.StdOut, maxLines: 80, maxChars: 8000);
        failure.StdErrTail = TailLines(cmdEx.StdErr, maxLines: 80, maxChars: 8000);
        failure.LogPath = TryWriteFailureLog(plan, stepEx.Step, cmdEx);

        return failure;
    }

    private static string? TryWriteFailureLog(DotNetPublishPlan plan, DotNetPublishStep step, DotNetPublishCommandException ex)
    {
        try
        {
            var logDir = Path.Combine(plan.ProjectRoot, "Artifacts", "DotNetPublish", "logs");
            Directory.CreateDirectory(logDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safeKey = ToSafeFileName(step?.Key, "step");
            var fileName = $"dotnetpublish-failure-{stamp}-{safeKey}.log";
            var fullPath = Path.Combine(logDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("=== dotnet publish failure ===");
            sb.AppendLine($"Step: {step?.Key} ({step?.Kind})");
            if (!string.IsNullOrWhiteSpace(step?.TargetName)) sb.AppendLine($"Target: {step!.TargetName}");
            if (!string.IsNullOrWhiteSpace(step?.Framework)) sb.AppendLine($"Framework: {step!.Framework}");
            if (!string.IsNullOrWhiteSpace(step?.Runtime)) sb.AppendLine($"Runtime: {step!.Runtime}");
            sb.AppendLine($"ExitCode: {ex.ExitCode}");
            sb.AppendLine($"WorkingDirectory: {ex.WorkingDirectory}");
            sb.AppendLine($"CommandLine: {ex.CommandLine}");
            sb.AppendLine();
            sb.AppendLine("--- stdout ---");
            if (!string.IsNullOrWhiteSpace(ex.StdOut)) sb.AppendLine(ex.StdOut.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("--- stderr ---");
            if (!string.IsNullOrWhiteSpace(ex.StdErr)) sb.AppendLine(ex.StdErr.TrimEnd());

            File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return fullPath;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> BuildMsBuildPropertyArgs(IReadOnlyDictionary<string, string> props)
    {
        if (props is null || props.Count == 0) return Array.Empty<string>();
        return props
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Select(kv => $"/p:{kv.Key}={kv.Value}");
    }

    internal static string ResolvePath(string baseDir, string path)
    {
        var p = (path ?? string.Empty).Trim();
        if (!IsWindows())
            p = p.Replace('\\', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(p)) return Path.GetFullPath(baseDir);
        if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    internal static void EnsurePathWithinRoot(string rootPath, string path, string label)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new InvalidOperationException("ProjectRoot is required for path safety checks.");

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{label} must not be empty.");

        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(path);

        if (PathsEqual(root, candidate)) return;

        var rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(rootWithSep, comparison))
        {
            throw new InvalidOperationException(
                $"{label} resolves outside ProjectRoot and is blocked by policy. " +
                $"Path='{candidate}', ProjectRoot='{root}'. " +
                "Set DotNet.AllowOutputOutsideProjectRoot or DotNet.AllowManifestOutsideProjectRoot to true if this is intentional.");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }

    private static (string? ManifestJson, string? ManifestText, string? ChecksumsPath) WriteManifests(
        DotNetPublishPlan plan,
        List<DotNetPublishArtefactResult> artefacts,
        List<DotNetPublishStorePackageResult>? storePackages = null,
        List<DotNetPublishMsiBuildResult>? msiBuilds = null)
    {
        var orderedArtefacts = (artefacts ?? new List<DotNetPublishArtefactResult>())
            .OrderBy(a => a.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Framework, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Runtime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Style.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.OutputDir, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedStorePackages = (storePackages ?? new List<DotNetPublishStorePackageResult>())
            .OrderBy(a => a.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Framework, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Runtime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Style.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.OutputDir, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedMsiBuilds = (msiBuilds ?? new List<DotNetPublishMsiBuildResult>())
            .OrderBy(a => a.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Framework, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Runtime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Style.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.InstallerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var manifestEntries = BuildManifestEntries(plan.ProjectRoot, orderedArtefacts, orderedStorePackages, orderedMsiBuilds);

        var jsonPath = plan.Outputs.ManifestJsonPath;
        var txtPath = plan.Outputs.ManifestTextPath;
        var checksumsPath = plan.Outputs.ChecksumsPath;

        if (!plan.AllowManifestOutsideProjectRoot)
        {
            if (!string.IsNullOrWhiteSpace(jsonPath))
                EnsurePathWithinRoot(plan.ProjectRoot, jsonPath!, "ManifestJsonPath");
            if (!string.IsNullOrWhiteSpace(txtPath))
                EnsurePathWithinRoot(plan.ProjectRoot, txtPath!, "ManifestTextPath");
            if (!string.IsNullOrWhiteSpace(checksumsPath))
                EnsurePathWithinRoot(plan.ProjectRoot, checksumsPath!, "ChecksumsPath");
        }

        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
            var json = JsonSerializer.Serialize(manifestEntries, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(jsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        if (!string.IsNullOrWhiteSpace(txtPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(txtPath))!);
            var lines = new List<string>();
            foreach (var a in orderedArtefacts)
            {
                var mb = a.TotalBytes / 1024d / 1024d;
                var exeMb = a.ExeBytes.HasValue ? (a.ExeBytes.Value / 1024d / 1024d) : 0;
                var zip = string.IsNullOrWhiteSpace(a.ZipPath) ? string.Empty : $" zip={a.ZipPath}";
                lines.Add($"{a.Target} ({a.Framework}, {a.Runtime}) -> {a.OutputDir} ({a.Files} files, {mb:N1} MB; exe {exeMb:N1} MB){zip}");
            }

            foreach (var store in orderedStorePackages)
            {
                var files = EnumerateStorePackageFiles(store).ToArray();
                lines.Add($"Store {store.StorePackageId} from {store.Target} ({store.Framework}, {store.Runtime}, {store.Style}) -> {store.OutputDir} ({files.Length} files)");
            }

            foreach (var build in orderedMsiBuilds)
            {
                var files = EnumerateExistingFiles(build.OutputFiles).ToArray();
                var outputDir = ResolveOutputDirectory(files);
                var version = string.IsNullOrWhiteSpace(build.Version) ? string.Empty : $" version={build.Version}";
                lines.Add($"MSI {build.InstallerId} from {build.Target} ({build.Framework}, {build.Runtime}, {build.Style}) -> {outputDir} ({files.Length} files{version})");
            }

            File.WriteAllLines(txtPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        if (!string.IsNullOrWhiteSpace(checksumsPath))
        {
            var filesToHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var a in orderedArtefacts)
            {
                foreach (var file in EnumerateFilesSafe(a.OutputDir, "*", SearchOption.AllDirectories))
                {
                    var full = Path.GetFullPath(file);
                    if (!File.Exists(full)) continue;
                    filesToHash[full] = ToManifestRelativePath(plan.ProjectRoot, full);
                }

                if (!string.IsNullOrWhiteSpace(a.ZipPath))
                {
                    var zip = Path.GetFullPath(a.ZipPath!);
                    if (File.Exists(zip))
                        filesToHash[zip] = ToManifestRelativePath(plan.ProjectRoot, zip);
                }
            }

            foreach (var store in orderedStorePackages)
            {
                foreach (var file in EnumerateStorePackageFiles(store))
                {
                    var full = Path.GetFullPath(file);
                    filesToHash[full] = ToManifestRelativePath(plan.ProjectRoot, full);
                }
            }

            foreach (var build in orderedMsiBuilds)
            {
                foreach (var file in EnumerateExistingFiles(build.OutputFiles))
                {
                    var full = Path.GetFullPath(file);
                    filesToHash[full] = ToManifestRelativePath(plan.ProjectRoot, full);
                }
            }

            if (!string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath!))
            {
                var full = Path.GetFullPath(jsonPath!);
                filesToHash[full] = ToManifestRelativePath(plan.ProjectRoot, full);
            }

            if (!string.IsNullOrWhiteSpace(txtPath) && File.Exists(txtPath!))
            {
                var full = Path.GetFullPath(txtPath!);
                filesToHash[full] = ToManifestRelativePath(plan.ProjectRoot, full);
            }

            var checksumLines = filesToHash
                .Select(kv => new { FullPath = kv.Key, Relative = kv.Value })
                .OrderBy(k => k.Relative, StringComparer.OrdinalIgnoreCase)
                .Select(k => $"{ComputeSha256(k.FullPath)} *{k.Relative}")
                .ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(checksumsPath!))!);
            File.WriteAllLines(checksumsPath!, checksumLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return (jsonPath, txtPath, checksumsPath);
    }

    private static List<DotNetPublishManifestEntry> BuildManifestEntries(
        string projectRoot,
        IReadOnlyList<DotNetPublishArtefactResult> orderedArtefacts,
        IReadOnlyList<DotNetPublishStorePackageResult> orderedStorePackages,
        IReadOnlyList<DotNetPublishMsiBuildResult> orderedMsiBuilds)
    {
        var entries = orderedArtefacts
            .Select(a => new DotNetPublishManifestEntry
            {
                Category = a.Category.ToString(),
                Target = a.Target,
                BundleId = EmptyToNull(a.BundleId),
                InstallerId = EmptyToNull(a.InstallerId),
                StorePackageId = EmptyToNull(a.StorePackageId),
                Kind = a.Kind == DotNetPublishTargetKind.Unknown ? null : a.Kind.ToString(),
                Runtime = a.Runtime,
                Framework = a.Framework,
                Style = a.Style.ToString(),
                PublishDir = a.PublishDir,
                OutputDir = a.OutputDir,
                ZipPath = EmptyToNull(a.ZipPath),
                OutputFiles = ToManifestOutputFiles(projectRoot, a.OutputFiles),
                Files = a.Files,
                TotalBytes = a.TotalBytes,
                ExePath = EmptyToNull(a.ExePath),
                ExeBytes = a.ExeBytes,
                Cleanup = HasCleanup(a.Cleanup) ? a.Cleanup : null,
                ServicePackage = a.ServicePackage,
                StateTransfer = a.StateTransfer,
                SignedFiles = a.SignedFiles > 0 ? a.SignedFiles : null
            })
            .ToList();

        foreach (var store in orderedStorePackages)
        {
            var files = EnumerateStorePackageFiles(store).ToArray();
            entries.Add(new DotNetPublishManifestEntry
            {
                Category = DotNetPublishArtefactCategory.StorePackage.ToString(),
                StorePackageId = store.StorePackageId,
                Target = store.Target,
                Runtime = store.Runtime,
                Framework = store.Framework,
                Style = store.Style.ToString(),
                PublishDir = store.OutputDir,
                OutputDir = store.OutputDir,
                OutputFiles = ToManifestOutputFiles(projectRoot, files),
                Files = files.Length,
                TotalBytes = SumFileBytes(files)
            });
        }

        foreach (var build in orderedMsiBuilds)
        {
            var files = EnumerateExistingFiles(build.OutputFiles).ToArray();
            var outputDir = ResolveOutputDirectory(files);
            entries.Add(new DotNetPublishManifestEntry
            {
                Category = DotNetPublishArtefactCategory.Installer.ToString(),
                InstallerId = build.InstallerId,
                Target = build.Target,
                Runtime = build.Runtime,
                Framework = build.Framework,
                Style = build.Style.ToString(),
                PublishDir = outputDir,
                OutputDir = outputDir,
                OutputFiles = ToManifestOutputFiles(projectRoot, files),
                Files = files.Length,
                TotalBytes = SumFileBytes(files),
                SignedFiles = build.SignedFiles is { Length: > 0 } ? build.SignedFiles.Length : null
            });
        }

        return entries;
    }

    // Disk manifest projection: keep JSON readability/stability separate from runtime result models.
    private sealed class DotNetPublishManifestEntry
    {
        public string Category { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string? BundleId { get; set; }
        public string? InstallerId { get; set; }
        public string? StorePackageId { get; set; }
        public string? Kind { get; set; }
        public string Runtime { get; set; } = string.Empty;
        public string Framework { get; set; } = string.Empty;
        public string Style { get; set; } = string.Empty;
        public string PublishDir { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;
        public string? ZipPath { get; set; }
        public string[]? OutputFiles { get; set; }
        public int Files { get; set; }
        public long TotalBytes { get; set; }
        public string? ExePath { get; set; }
        public long? ExeBytes { get; set; }
        public DotNetPublishCleanupResult? Cleanup { get; set; }
        public DotNetPublishServicePackageResult? ServicePackage { get; set; }
        public DotNetPublishStateTransferResult? StateTransfer { get; set; }
        public int? SignedFiles { get; set; }
    }

    private static bool HasCleanup(DotNetPublishCleanupResult? cleanup)
    {
        return cleanup is not null
            && (cleanup.PdbRemoved != 0
                || cleanup.DocsRemoved != 0
                || cleanup.RefPruned);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string[]? ToManifestOutputFiles(string projectRoot, IEnumerable<string>? files)
    {
        var outputFiles = (files ?? Array.Empty<string>())
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(file => Path.IsPathRooted(file)
                ? ToManifestRelativePath(projectRoot, file)
                : file.Replace('\\', '/'))
            .ToArray();

        return outputFiles.Length == 0 ? null : outputFiles;
    }

    private static IEnumerable<string> EnumerateStorePackageFiles(DotNetPublishStorePackageResult store)
    {
        return EnumerateExistingFiles((store.OutputFiles ?? Array.Empty<string>())
            .Concat(store.UploadFiles ?? Array.Empty<string>())
            .Concat(store.SymbolFiles ?? Array.Empty<string>()));
    }

    private static IEnumerable<string> EnumerateExistingFiles(IEnumerable<string>? files)
    {
        foreach (var file in files ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file))
                continue;

            var full = Path.GetFullPath(file);
            if (File.Exists(full))
                yield return full;
        }
    }

    private static long SumFileBytes(IEnumerable<string> files)
    {
        long bytes = 0;
        foreach (var file in files)
        {
            try
            {
                bytes += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // best effort for manifest summaries
            }
            catch (UnauthorizedAccessException)
            {
                // best effort for manifest summaries
            }
        }

        return bytes;
    }

    private static string ResolveOutputDirectory(IReadOnlyList<string> files)
    {
        return files.Count == 0
            ? string.Empty
            : Path.GetDirectoryName(Path.GetFullPath(files[0])) ?? string.Empty;
    }

    private static string ToManifestRelativePath(string projectRoot, string fullPath)
    {
        var root = Path.GetFullPath(projectRoot);
        var file = Path.GetFullPath(fullPath);
        var relative = GetRelativePath(root, file);
        if (string.IsNullOrWhiteSpace(relative))
            relative = Path.GetFileName(file);
        return relative.Replace('\\', '/');
    }

    internal static string GetRelativePath(string relativeTo, string path)
    {
#if NET472
        var basePath = Path.GetFullPath(relativeTo)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var baseUri = new Uri(basePath);
        var targetUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        for (var i = 0; i < hash.Length; i++)
            sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

}
