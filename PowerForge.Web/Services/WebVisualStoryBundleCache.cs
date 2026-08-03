namespace PowerForge.Web;

/// <summary>Caches validated visual-story bundles for one website build while their files remain unchanged.</summary>
internal sealed class WebVisualStoryBundleCache
{
    private readonly Func<string, WebVisualStoryBundle> _loader;
    private readonly Dictionary<string, CacheEntry> _entries;
    private readonly object _sync = new();

    internal WebVisualStoryBundleCache(Func<string, WebVisualStoryBundle>? loader = null)
    {
        _loader = loader ?? WebVisualStoryStager.Load;
        _entries = new Dictionary<string, CacheEntry>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    internal WebVisualStoryBundle Load(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        lock (_sync)
        {
            if (_entries.TryGetValue(fullPath, out var cached) && cached.IsCurrent())
                return cached.Bundle;

            var bundle = _loader(fullPath);
            _entries[fullPath] = CacheEntry.Capture(fullPath, bundle);
            return bundle;
        }
    }

    private sealed class CacheEntry
    {
        private CacheEntry(WebVisualStoryBundle bundle, FileState[] files)
        {
            Bundle = bundle;
            Files = files;
        }

        internal WebVisualStoryBundle Bundle { get; }
        private FileState[] Files { get; }

        internal static CacheEntry Capture(string manifestPath, WebVisualStoryBundle bundle)
        {
            var root = Path.GetDirectoryName(manifestPath)
                       ?? throw new InvalidOperationException("Visual-story manifest has no parent directory.");
            var files = new List<FileState>(bundle.Artifacts.Length + 1)
            {
                FileState.Capture(manifestPath)
            };
            foreach (var artifact in bundle.Artifacts)
            {
                files.Add(FileState.Capture(VisualStoryPathGuard.ResolveRelativePath(
                    root,
                    artifact.Path,
                    "cached visual-story artifact")));
            }
            return new CacheEntry(bundle, files.ToArray());
        }

        internal bool IsCurrent() => Files.All(static file => file.IsCurrent());
    }

    private readonly record struct FileState(
        string Path,
        long Length,
        long LastWriteUtcTicks,
        FileAttributes Attributes)
    {
        internal static FileState Capture(string path)
        {
            var info = new FileInfo(path);
            return new FileState(
                info.FullName,
                info.Length,
                info.LastWriteTimeUtc.Ticks,
                info.Attributes);
        }

        internal bool IsCurrent()
        {
            try
            {
                var info = new FileInfo(Path);
                return info.Exists &&
                       info.Length == Length &&
                       info.LastWriteTimeUtc.Ticks == LastWriteUtcTicks &&
                       info.Attributes == Attributes;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
