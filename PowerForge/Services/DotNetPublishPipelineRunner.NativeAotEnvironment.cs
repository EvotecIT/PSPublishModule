namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string BuildTrustedNativeAotPath(
        string dotNetExecutablePath,
        string? inheritedPath)
    {
        string dotNetDirectory = Path.GetDirectoryName(Path.GetFullPath(dotNetExecutablePath))
            ?? throw new InvalidOperationException("The trusted dotnet executable has no containing directory.");
        StringComparer comparer = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var trustedRoots = new HashSet<string>(comparer);
        var requiredDirectories = new List<string>();
        if (IsWindows())
        {
            AddTrustedNativeToolchainRoot(
                trustedRoots,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            AddTrustedNativeToolchainRoot(
                trustedRoots,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddTrustedNativeToolchainRoot(
                trustedRoots,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(systemDirectory))
                requiredDirectories.Add(systemDirectory);
        }
        else
        {
            AddTrustedNativeToolchainRoot(trustedRoots, "/usr/bin");
            AddTrustedNativeToolchainRoot(trustedRoots, "/usr/sbin");
            AddTrustedNativeToolchainRoot(trustedRoots, "/bin");
            AddTrustedNativeToolchainRoot(trustedRoots, "/sbin");
            AddTrustedNativeToolchainRoot(trustedRoots, "/Applications/Xcode.app");
            AddTrustedNativeToolchainRoot(trustedRoots, "/Library/Developer/CommandLineTools");
            requiredDirectories.Add("/usr/bin");
            requiredDirectories.Add("/usr/sbin");
        }

        var admitted = new List<string> { dotNetDirectory };
        IEnumerable<string> candidates = (inheritedPath ?? string.Empty)
            .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Concat(requiredDirectories);
        foreach (string candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }
            if (!Directory.Exists(fullPath) || admitted.Contains(fullPath, comparer))
                continue;
            string? allowedRoot = trustedRoots.FirstOrDefault(root =>
                IsSameOrBelowBuildInputPath(fullPath, root));
            if (allowedRoot is null ||
                IsReparsePointPath(allowedRoot) ||
                HasReparsePointInExistingAncestors(fullPath, allowedRoot))
            {
                continue;
            }
            admitted.Add(fullPath);
        }
        return string.Join(Path.PathSeparator.ToString(), admitted);
    }

    private static void AddTrustedNativeToolchainRoot(ISet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
                roots.Add(fullPath);
        }
        catch
        {
            // Ignore unavailable platform roots; NativeAOT will fail closed if its toolchain is absent.
        }
    }

    internal sealed class TrustedNativeAotPathSnapshot : IDisposable
    {
        private readonly string _path;
        private readonly string[] _directories;
        private readonly Dictionary<string, NativeToolFileSnapshot> _files;
        private readonly List<FileSystemWatcher> _watchers = new();
        private int _changed;
        private bool _disposed;

        private TrustedNativeAotPathSnapshot(
            string path,
            string[] directories,
            Dictionary<string, NativeToolFileSnapshot> files)
        {
            _path = path;
            _directories = directories;
            _files = files;
        }

        internal static TrustedNativeAotPathSnapshot Create(string path)
        {
            string[] directories = NormalizePathDirectories(path);
            Dictionary<string, NativeToolFileSnapshot> first = CaptureFiles(directories);
            TrustedNativeAotPathSnapshot? snapshot = null;
            try
            {
                var files = new Dictionary<string, NativeToolFileSnapshot>(
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                snapshot = new TrustedNativeAotPathSnapshot(path, directories, files);
                snapshot.StartWatchers();
                Dictionary<string, NativeToolFileSnapshot> second = CaptureFiles(directories);
                foreach (KeyValuePair<string, NativeToolFileSnapshot> file in second)
                    files[file.Key] = file.Value;
                if (Volatile.Read(ref snapshot._changed) != 0 || !SnapshotsEqual(first, second))
                    ThrowChanged();
                return snapshot;
            }
            catch
            {
                snapshot?.Dispose();
                throw;
            }
        }

        internal void EnsurePath(string path)
        {
            if (!string.Equals(
                    path,
                    _path,
                    IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The NativeAOT tool search path changed after its executables were admitted.");
            }
        }

        internal void ValidateUnchanged(bool verifyHashes)
        {
            if (Volatile.Read(ref _changed) != 0)
                ThrowChanged();
            string[] currentFiles = EnumerateNativeToolFiles(_directories).ToArray();
            if (currentFiles.Length != _files.Count || currentFiles.Any(path => !_files.ContainsKey(path)))
                ThrowChanged();
            foreach (string path in currentFiles)
            {
                NativeToolFileSnapshot expected = _files[path];
                var info = new FileInfo(path);
                if (!info.Exists ||
                    info.Length != expected.Length ||
                    info.LastWriteTimeUtc != expected.LastWriteTimeUtc ||
                    (verifyHashes &&
                     !string.Equals(ComputeNativeToolSha256(path), expected.Sha256, StringComparison.OrdinalIgnoreCase)))
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
            foreach (string directory in _directories)
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = false,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size |
                                   NotifyFilters.Security
                };
                watcher.Changed += MarkChangedIfNativeTool;
                watcher.Created += MarkChangedIfNativeTool;
                watcher.Deleted += MarkChangedIfNativeTool;
                watcher.Renamed += MarkChangedIfNativeTool;
                watcher.Error += MarkChanged;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }

        private void MarkChangedIfNativeTool(object sender, FileSystemEventArgs args)
        {
            if (AffectsNativeTool(args.FullPath))
                Interlocked.Exchange(ref _changed, 1);
        }

        private void MarkChangedIfNativeTool(object sender, RenamedEventArgs args)
        {
            if (AffectsNativeTool(args.OldFullPath) || AffectsNativeTool(args.FullPath))
                Interlocked.Exchange(ref _changed, 1);
        }

        private void MarkChanged(object sender, ErrorEventArgs args)
            => Interlocked.Exchange(ref _changed, 1);

        private bool AffectsNativeTool(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (_files.ContainsKey(fullPath))
                return true;
            if (!IsWindows())
                return true;
            return IsNativeToolCandidate(fullPath);
        }

        internal bool AffectsNativeToolForTest(string path)
            => AffectsNativeTool(path);

        private static string[] NormalizePathDirectories(string path)
            => path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => Path.GetFullPath(value.Trim().Trim('"')))
                .Where(Directory.Exists)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();

        private static Dictionary<string, NativeToolFileSnapshot> CaptureFiles(
            IEnumerable<string> directories)
        {
            var files = new Dictionary<string, NativeToolFileSnapshot>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string path in EnumerateNativeToolFiles(directories))
            {
                var info = new FileInfo(path);
                files[path] = new NativeToolFileSnapshot(
                    info.Length,
                    info.LastWriteTimeUtc,
                    ComputeNativeToolSha256(path));
            }
            return files;
        }

        private static IEnumerable<string> EnumerateNativeToolFiles(IEnumerable<string> directories)
        {
            StringComparer comparer = IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            return directories
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                .Where(IsNativeToolCandidate)
                .Select(Path.GetFullPath)
                .Distinct(comparer)
                .OrderBy(path => path, comparer);
        }

        private static bool IsNativeToolCandidate(string path)
        {
            if (IsWindows())
            {
                string extension = Path.GetExtension(path);
                return extension.Equals(".com", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
            }
#if NET8_0_OR_GREATER
            try
            {
                if (OperatingSystem.IsWindows())
                    return false;
                UnixFileMode mode = File.GetUnixFileMode(path);
                return (mode & (UnixFileMode.UserExecute |
                                UnixFileMode.GroupExecute |
                                UnixFileMode.OtherExecute)) != 0;
            }
            catch
            {
                return false;
            }
#else
            return true;
#endif
        }

        private static string ComputeNativeToolSha256(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create();
            return ToUpperHex(hash.ComputeHash(stream));
        }

        private static bool SnapshotsEqual(
            IReadOnlyDictionary<string, NativeToolFileSnapshot> first,
            IReadOnlyDictionary<string, NativeToolFileSnapshot> second)
        {
            if (first.Count != second.Count)
                return false;
            foreach (KeyValuePair<string, NativeToolFileSnapshot> file in first)
            {
                if (!second.TryGetValue(file.Key, out NativeToolFileSnapshot? candidate) ||
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
                "The admitted NativeAOT compiler/linker path changed during publish.");

        private sealed class NativeToolFileSnapshot
        {
            internal NativeToolFileSnapshot(long length, DateTime lastWriteTimeUtc, string sha256)
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
