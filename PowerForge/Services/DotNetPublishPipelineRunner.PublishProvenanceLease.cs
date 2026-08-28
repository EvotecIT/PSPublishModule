using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal sealed class PublishProvenanceLease : IDisposable
    {
        private readonly HashSet<string> _guardedPaths;
        private readonly Dictionary<string, string> _expectedHashes;
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
            _expectedHashes = new Dictionary<string, string>(comparer);
        }

        internal static PublishProvenanceLease Create(
            string? sourceRoot,
            IEnumerable<string> paths)
        {
            var lease = new PublishProvenanceLease(paths);
            try
            {
                lease.StartWatchers(sourceRoot);
                foreach (string path in lease._guardedPaths)
                {
                    if (!File.Exists(path))
                    {
                        throw new InvalidOperationException(
                            $"A proven publish input disappeared before it could be leased: {path}.");
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
            foreach (KeyValuePair<string, string> entry in _expectedHashes)
            {
                if (!File.Exists(entry.Key) ||
                    !string.Equals(
                        ComputeSha256Hex(File.ReadAllBytes(entry.Key)),
                        entry.Value,
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

        private void StartWatchers(string? sourceRoot)
        {
            var roots = new List<(string Path, bool Recursive)>();
            if (!string.IsNullOrWhiteSpace(sourceRoot))
            {
                string fullSourceRoot = Path.GetFullPath(sourceRoot!);
                if (Directory.Exists(fullSourceRoot) &&
                    _guardedPaths.Any(path => IsSameOrBelowBuildInputPath(path, fullSourceRoot)))
                {
                    roots.Add((fullSourceRoot, true));
                }
            }

            foreach (string directory in _guardedPaths
                         .Where(path => roots.All(root =>
                             !IsSameOrBelowBuildInputPath(path, root.Path)))
                         .Select(path => Path.GetDirectoryName(path)!)
                         .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                roots.Add((directory, false));
            }

            foreach ((string root, bool recursive) in roots)
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = recursive,
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

        private void MarkChanged(object sender, FileSystemEventArgs args)
        {
            if (_guardedPaths.Contains(Path.GetFullPath(args.FullPath)))
                Interlocked.Exchange(ref _changed, 1);
        }

        private void MarkChanged(object sender, RenamedEventArgs args)
        {
            if (_guardedPaths.Contains(Path.GetFullPath(args.FullPath)) ||
                _guardedPaths.Contains(Path.GetFullPath(args.OldFullPath)))
            {
                Interlocked.Exchange(ref _changed, 1);
            }
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
