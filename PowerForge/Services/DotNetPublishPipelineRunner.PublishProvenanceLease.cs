using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal sealed class PublishProvenanceLease : IDisposable
    {
        private readonly HashSet<string> _guardedPaths;
        private readonly HashSet<string> _absentDirectoryAncestors;
        private readonly Dictionary<string, string?> _expectedHashes;
        private readonly List<FileStream> _leases = new();
        private readonly List<FileSystemWatcher> _watchers = new();
        private int _changed;
        private bool _disposed;

        private PublishProvenanceLease(IEnumerable<string> paths)
        {
            StringComparer comparer = IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            _guardedPaths = new HashSet<string>(
                paths.Select(Path.GetFullPath),
                comparer);
            _expectedHashes = new Dictionary<string, string?>(comparer);
            _absentDirectoryAncestors = new HashSet<string>(comparer);
            foreach (string path in _guardedPaths)
            {
                string? directory = Path.GetDirectoryName(path);
                while (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    _absentDirectoryAncestors.Add(directory);
                    directory = Path.GetDirectoryName(directory);
                }
            }
        }

        internal static PublishProvenanceLease Create(IEnumerable<string> paths)
        {
            var lease = new PublishProvenanceLease(paths);
            try
            {
                lease.StartWatchers();
                foreach (string path in lease._guardedPaths)
                {
                    if (!File.Exists(path))
                    {
                        lease._expectedHashes[path] = null;
                        continue;
                    }

                    var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                    lease._leases.Add(stream);
                    lease._expectedHashes[path] = ComputeStreamSha256(stream);
                }
                lease.ValidateUnchanged();
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        internal void EnsureCovers(IEnumerable<string> paths)
        {
            string[] missing = paths
                .Select(Path.GetFullPath)
                .Where(path => !_guardedPaths.Contains(path))
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "Publish provenance discovered build inputs after the immutable lease was acquired: " +
                    string.Join(", ", missing));
            }
        }

        internal void ValidateUnchanged()
        {
            if (Volatile.Read(ref _changed) != 0)
            {
                throw new InvalidOperationException(
                    "A proven project or import input changed while publish was running.");
            }
            if (_absentDirectoryAncestors.Any(Directory.Exists))
            {
                throw new InvalidOperationException(
                    "A previously absent build-control directory appeared while publish was running.");
            }
            foreach (KeyValuePair<string, string?> entry in _expectedHashes)
            {
                if (entry.Value is null)
                {
                    if (File.Exists(entry.Key))
                    {
                        throw new InvalidOperationException(
                            $"A previously absent build-control input appeared while publish was running: {entry.Key}.");
                    }
                    continue;
                }
                if (!File.Exists(entry.Key) ||
                    !string.Equals(
                        ComputeSha256Hex(File.ReadAllBytes(entry.Key)),
                        entry.Value!,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"A proven project or import input changed while publish was running: {entry.Key}.");
                }
            }
            if (Volatile.Read(ref _changed) != 0)
            {
                throw new InvalidOperationException(
                    "A proven project or import input changed while publish was running.");
            }
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
            foreach (FileStream lease in _leases)
                lease.Dispose();
        }

        private void StartWatchers()
        {
            foreach (string directory in _guardedPaths
                         .Select(FindNearestExistingDirectory)
                         .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
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
                watcher.Changed += MarkChanged;
                watcher.Created += MarkChanged;
                watcher.Deleted += MarkChanged;
                watcher.Renamed += MarkChanged;
                watcher.Error += (_, _) => Interlocked.Exchange(ref _changed, 1);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }

        internal static string[] BuildGuardedPaths(
            IEnumerable<string> publishInputFiles,
            IEnumerable<NoBuildPublishInput> provenPublishInputs)
        {
            IEnumerable<string> paths = publishInputFiles.Concat(
                provenPublishInputs.Select(input => input.FullPath));

            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
        }

        private void MarkChanged(object sender, FileSystemEventArgs args)
        {
            if (AffectsGuardedPath(args.FullPath))
                Interlocked.Exchange(ref _changed, 1);
        }

        private void MarkChanged(object sender, RenamedEventArgs args)
        {
            if (AffectsGuardedPath(args.FullPath) || AffectsGuardedPath(args.OldFullPath))
            {
                Interlocked.Exchange(ref _changed, 1);
            }
        }

        private bool AffectsGuardedPath(string changedPath)
        {
            string fullChangedPath = Path.GetFullPath(changedPath);
            return _guardedPaths.Contains(fullChangedPath) ||
                   _absentDirectoryAncestors.Contains(fullChangedPath);
        }

        internal bool AffectsGuardedPathForTest(string changedPath)
            => AffectsGuardedPath(changedPath);

        private static string FindNearestExistingDirectory(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                directory = Path.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    $"A provenance input has no observable existing directory ancestor: {path}.");
            }
            return directory!;
        }

        private static string ComputeStreamSha256(Stream stream)
        {
            using SHA256 hash = SHA256.Create();
            byte[] value = hash.ComputeHash(stream);
            stream.Position = 0;
            return ToUpperHex(value);
        }
    }
}
