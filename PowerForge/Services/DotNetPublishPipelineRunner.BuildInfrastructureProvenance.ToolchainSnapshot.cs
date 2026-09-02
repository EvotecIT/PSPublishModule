using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal sealed class TrustedDotNetInstallationSnapshot : IDisposable
    {
        private readonly string _executablePath;
        private readonly string _installationRoot;
        private readonly string? _selectedSdkDirectory;
        private readonly string[] _capturedRoots;
        private readonly string[] _observedRoots;
        private readonly Dictionary<string, FileSnapshot> _files;
        private readonly List<FileSystemWatcher> _watchers = new();
        private int _changed;
        private bool _disposed;

        private TrustedDotNetInstallationSnapshot(
            string executablePath,
            string installationRoot,
            string? selectedSdkDirectory,
            string[] capturedRoots,
            string[] observedRoots,
            Dictionary<string, FileSnapshot> files)
        {
            _executablePath = executablePath;
            _installationRoot = installationRoot;
            _selectedSdkDirectory = selectedSdkDirectory;
            _capturedRoots = capturedRoots;
            _observedRoots = observedRoots;
            _files = files;
        }

        internal static TrustedDotNetInstallationSnapshot Create(
            string executablePath,
            string? workingDirectory = null)
        {
            string fullExecutablePath = Path.GetFullPath(executablePath);
            string installationRoot = Path.GetDirectoryName(fullExecutablePath)!;
            string[] capturedRoots = ResolveCapturedRoots(
                fullExecutablePath,
                installationRoot,
                string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.CurrentDirectory
                    : Path.GetFullPath(workingDirectory),
                out string? selectedSdkDirectory);
            string[] observedRoots = ResolveObservedRoots(installationRoot, capturedRoots);
            Dictionary<string, FileSnapshot> firstFiles = CaptureFiles(capturedRoots);
            TrustedDotNetInstallationSnapshot? snapshot = null;
            try
            {
                var files = new Dictionary<string, FileSnapshot>(
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                snapshot = new TrustedDotNetInstallationSnapshot(
                    fullExecutablePath,
                    installationRoot,
                    selectedSdkDirectory,
                    capturedRoots,
                    observedRoots,
                    files);
                snapshot.StartWatchers();
                Dictionary<string, FileSnapshot> secondFiles = CaptureFiles(capturedRoots);
                foreach (KeyValuePair<string, FileSnapshot> file in secondFiles)
                    files[file.Key] = file.Value;
                if (!files.ContainsKey(fullExecutablePath) ||
                    Volatile.Read(ref snapshot._changed) != 0 ||
                    !SnapshotsEqual(firstFiles, secondFiles))
                {
                    throw new InvalidOperationException(
                        "The selected dotnet SDK/runtime closure changed while it was being admitted.");
                }
                return snapshot;
            }
            catch
            {
                snapshot?.Dispose();
                throw;
            }
        }

        internal void EnsureSelection(string executablePath, string workingDirectory)
        {
            if (!string.Equals(
                    Path.GetFullPath(executablePath),
                    _executablePath,
                    IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The selected dotnet executable changed after its SDK/runtime closure was admitted.");
            }
            if (_selectedSdkDirectory is null)
                return;
            string sdkRoot = Path.Combine(_installationRoot, "sdk");
            string selectedSdkDirectory = ResolveSelectedSdkDirectory(
                sdkRoot,
                Path.GetFullPath(workingDirectory));
            if (!string.Equals(
                    selectedSdkDirectory,
                    _selectedSdkDirectory,
                    IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A dotnet child selected an SDK outside the admitted SDK/runtime closure.");
            }
        }

        internal void ValidateUnchanged(bool verifyHashes)
        {
            if (Volatile.Read(ref _changed) != 0)
                ThrowChanged();

            string[] currentFiles = EnumerateCapturedFiles(_capturedRoots).ToArray();
            if (currentFiles.Length != _files.Count || currentFiles.Any(path => !_files.ContainsKey(path)))
                ThrowChanged();
            foreach (string path in currentFiles)
            {
                FileSnapshot expected = _files[path];
                var info = new FileInfo(path);
                if (!info.Exists ||
                    info.Length != expected.Length ||
                    info.LastWriteTimeUtc != expected.LastWriteTimeUtc ||
                    (verifyHashes &&
                     !string.Equals(ComputeFileSha256(path), expected.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    ThrowChanged();
                }
            }
            if (Volatile.Read(ref _changed) != 0)
                ThrowChanged();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (FileSystemWatcher watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }

        private void StartWatchers()
        {
            var watched = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string root in _observedRoots)
            {
                if (Directory.Exists(root))
                {
                    AddWatcher(root, includeSubdirectories: true, watched);
                    string? parent = Path.GetDirectoryName(root);
                    if (!string.IsNullOrWhiteSpace(parent))
                        AddWatcher(parent!, includeSubdirectories: false, watched);
                }
                else
                {
                    string? parent = Path.GetDirectoryName(root);
                    if (!string.IsNullOrWhiteSpace(parent))
                        AddWatcher(parent!, includeSubdirectories: false, watched);
                }
            }
        }

        private void AddWatcher(
            string path,
            bool includeSubdirectories,
            HashSet<string> watched)
        {
            string fullPath = Path.GetFullPath(path);
            string key = fullPath + "\0" + includeSubdirectories;
            if (!watched.Add(key))
                return;
            var watcher = new FileSystemWatcher(fullPath)
            {
                IncludeSubdirectories = includeSubdirectories,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.Security
            };
            watcher.Changed += MarkChanged;
            watcher.Created += MarkChanged;
            watcher.Deleted += MarkChanged;
            watcher.Renamed += MarkChanged;
            watcher.Error += MarkChanged;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        private void MarkChanged(object sender, FileSystemEventArgs args)
        {
            if (AffectsCapturedClosure(args.FullPath))
                Interlocked.Exchange(ref _changed, 1);
        }

        private void MarkChanged(object sender, RenamedEventArgs args)
        {
            if (AffectsCapturedClosure(args.FullPath) || AffectsCapturedClosure(args.OldFullPath))
                Interlocked.Exchange(ref _changed, 1);
        }

        private void MarkChanged(object sender, ErrorEventArgs args)
            => Interlocked.Exchange(ref _changed, 1);

        private bool AffectsCapturedClosure(string path)
        {
            string fullPath = Path.GetFullPath(path);
            StringComparison comparison = IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return _observedRoots.Any(root =>
                string.Equals(fullPath, root, comparison) ||
                (Directory.Exists(root) && IsSameOrBelowBuildInputPath(fullPath, root)));
        }

        internal bool AffectsCapturedClosureForTest(string path)
            => AffectsCapturedClosure(path);

        private static string[] ResolveObservedRoots(
            string installationRoot,
            IEnumerable<string> capturedRoots)
        {
            var roots = new List<string>(capturedRoots);
            AddDirectoryIfPresent(roots, Path.Combine(installationRoot, "packs"));
            AddDirectoryIfPresent(roots, Path.Combine(installationRoot, "sdk-manifests"));
            AddDirectoryIfPresent(roots, Path.Combine(installationRoot, "metadata", "workloads"));
            return NormalizeCapturedRoots(roots);
        }

        private static string[] ResolveCapturedRoots(
            string executablePath,
            string installationRoot,
            string workingDirectory,
            out string? selectedSdkDirectory)
        {
            var roots = new List<string> { executablePath };
            string sdkRoot = Path.Combine(installationRoot, "sdk");
            if (!Directory.Exists(sdkRoot))
            {
                selectedSdkDirectory = null;
                roots.Add(installationRoot);
                return NormalizeCapturedRoots(roots);
            }

            selectedSdkDirectory = ResolveSelectedSdkDirectory(sdkRoot, workingDirectory);
            roots.Add(selectedSdkDirectory);
            AddDirectoryIfPresent(roots, Path.Combine(installationRoot, "host"));
            AddDirectoryIfPresent(roots, Path.Combine(installationRoot, "shared"));
            return NormalizeCapturedRoots(roots);
        }

        private static string ResolveSelectedSdkDirectory(string sdkRoot, string workingDirectory)
        {
            (NuGetVersion Version, string Path)[] installed = Directory
                .EnumerateDirectories(sdkRoot)
                .Select(path => (Parsed: NuGetVersion.TryParse(Path.GetFileName(path), out NuGetVersion? version), Version: version, Path: path))
                .Where(candidate => candidate.Parsed && candidate.Version is not null)
                .Select(candidate => (candidate.Version!, Path.GetFullPath(candidate.Path)))
                .OrderBy(candidate => candidate.Item1)
                .ToArray();
            if (installed.Length == 0)
            {
                throw new InvalidOperationException(
                    $"The selected dotnet installation contains no admissible SDK directories: {sdkRoot}.");
            }

            string? globalJsonPath = FindNearestGlobalJson(workingDirectory);
            if (globalJsonPath is null)
                return installed[installed.Length - 1].Path;

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
                if (!document.RootElement.TryGetProperty("sdk", out JsonElement sdk) ||
                    !sdk.TryGetProperty("version", out JsonElement versionElement) ||
                    !NuGetVersion.TryParse(versionElement.GetString(), out NuGetVersion? requested) ||
                    requested is null)
                {
                    return installed[installed.Length - 1].Path;
                }

                string rollForward = sdk.TryGetProperty("rollForward", out JsonElement rollForwardElement)
                    ? rollForwardElement.GetString() ?? "patch"
                    : "patch";
                bool allowPrerelease = !sdk.TryGetProperty("allowPrerelease", out JsonElement allowPrereleaseElement) ||
                                       allowPrereleaseElement.ValueKind != JsonValueKind.False;
                (NuGetVersion Version, string Path)[] candidates = installed
                    .Where(candidate => allowPrerelease || !candidate.Version.IsPrerelease)
                    .Where(candidate => IsSdkRollForwardCandidate(candidate.Version, requested, rollForward))
                    .ToArray();
                if (candidates.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"The selected dotnet SDK requested by {globalJsonPath} is not installed.");
                }
                return SelectSdkRollForwardCandidate(candidates, rollForward).Path;
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"The selected dotnet SDK could not be resolved from {globalJsonPath}.",
                    exception);
            }
        }

        private static bool IsSdkRollForwardCandidate(
            NuGetVersion candidate,
            NuGetVersion requested,
            string rollForward)
        {
            if (rollForward.Equals("disable", StringComparison.OrdinalIgnoreCase))
                return candidate == requested;
            if (candidate < requested)
                return false;
            if (rollForward.Equals("patch", StringComparison.OrdinalIgnoreCase) ||
                rollForward.Equals("latestPatch", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Major == requested.Major &&
                       candidate.Minor == requested.Minor &&
                       candidate.Patch / 100 == requested.Patch / 100;
            }
            if (rollForward.Equals("feature", StringComparison.OrdinalIgnoreCase) ||
                rollForward.Equals("latestFeature", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Major == requested.Major && candidate.Minor == requested.Minor;
            }
            if (rollForward.Equals("minor", StringComparison.OrdinalIgnoreCase) ||
                rollForward.Equals("latestMinor", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Major == requested.Major;
            }
            if (rollForward.Equals("major", StringComparison.OrdinalIgnoreCase) ||
                rollForward.Equals("latestMajor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            throw new InvalidOperationException($"Unsupported global.json SDK rollForward policy: {rollForward}.");
        }

        private static (NuGetVersion Version, string Path) SelectSdkRollForwardCandidate(
            (NuGetVersion Version, string Path)[] candidates,
            string rollForward)
        {
            if (rollForward.StartsWith("latest", StringComparison.OrdinalIgnoreCase) ||
                rollForward.Equals("patch", StringComparison.OrdinalIgnoreCase) ||
                rollForward.Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                return candidates[candidates.Length - 1];
            }
            if (rollForward.Equals("feature", StringComparison.OrdinalIgnoreCase))
            {
                int featureBand = candidates.Min(candidate => candidate.Version.Patch / 100);
                return candidates.Where(candidate => candidate.Version.Patch / 100 == featureBand).Last();
            }
            if (rollForward.Equals("minor", StringComparison.OrdinalIgnoreCase))
            {
                int minor = candidates.Min(candidate => candidate.Version.Minor);
                int featureBand = candidates
                    .Where(candidate => candidate.Version.Minor == minor)
                    .Min(candidate => candidate.Version.Patch / 100);
                return candidates
                    .Where(candidate => candidate.Version.Minor == minor &&
                                        candidate.Version.Patch / 100 == featureBand)
                    .Last();
            }
            if (rollForward.Equals("major", StringComparison.OrdinalIgnoreCase))
            {
                int major = candidates.Min(candidate => candidate.Version.Major);
                int minor = candidates
                    .Where(candidate => candidate.Version.Major == major)
                    .Min(candidate => candidate.Version.Minor);
                int featureBand = candidates
                    .Where(candidate => candidate.Version.Major == major && candidate.Version.Minor == minor)
                    .Min(candidate => candidate.Version.Patch / 100);
                return candidates
                    .Where(candidate => candidate.Version.Major == major &&
                                        candidate.Version.Minor == minor &&
                                        candidate.Version.Patch / 100 == featureBand)
                    .Last();
            }
            return candidates[candidates.Length - 1];
        }

        private static string? FindNearestGlobalJson(string workingDirectory)
        {
            string? directory = Path.GetFullPath(workingDirectory);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                string candidate = Path.Combine(directory, "global.json");
                if (File.Exists(candidate))
                    return candidate;
                directory = Path.GetDirectoryName(directory);
            }
            return null;
        }

        private static void AddDirectoryIfPresent(List<string> roots, string path)
        {
            if (Directory.Exists(path))
                roots.Add(path);
        }

        private static string[] NormalizeCapturedRoots(IEnumerable<string> roots)
            => roots
                .Select(Path.GetFullPath)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();

        private static string ComputeFileSha256(string path)
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using SHA256 hash = SHA256.Create();
            return ToUpperHex(hash.ComputeHash(stream));
        }

        private static Dictionary<string, FileSnapshot> CaptureFiles(IEnumerable<string> roots)
        {
            var files = new Dictionary<string, FileSnapshot>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string path in EnumerateCapturedFiles(roots))
            {
                var info = new FileInfo(path);
                files[path] = new FileSnapshot(
                    info.Length,
                    info.LastWriteTimeUtc,
                    ComputeFileSha256(path));
            }
            return files;
        }

        private static IEnumerable<string> EnumerateCapturedFiles(IEnumerable<string> roots)
        {
            StringComparer comparer = IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            return roots
                .SelectMany(root => File.Exists(root)
                    ? [Path.GetFullPath(root)]
                    : Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(Path.GetFullPath))
                .Distinct(comparer);
        }

        private static bool SnapshotsEqual(
            IReadOnlyDictionary<string, FileSnapshot> first,
            IReadOnlyDictionary<string, FileSnapshot> second)
        {
            if (first.Count != second.Count)
                return false;
            foreach (KeyValuePair<string, FileSnapshot> file in first)
            {
                if (!second.TryGetValue(file.Key, out FileSnapshot? candidate) ||
                    file.Value.Length != candidate.Length ||
                    file.Value.LastWriteTimeUtc != candidate.LastWriteTimeUtc ||
                    !string.Equals(file.Value.Sha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static void ThrowChanged()
            => throw new InvalidOperationException(
                "The selected dotnet SDK/runtime closure changed after admission.");

        private sealed class FileSnapshot
        {
            internal FileSnapshot(long length, DateTime lastWriteTimeUtc, string sha256)
            {
                Length = length;
                LastWriteTimeUtc = lastWriteTimeUtc;
                Sha256 = sha256;
            }

            internal long Length { get; }

            internal DateTime LastWriteTimeUtc { get; }

            internal string Sha256 { get; }
        }
    }
}
