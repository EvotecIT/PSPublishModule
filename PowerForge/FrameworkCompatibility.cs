using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge;

internal static class FrameworkCompatibility
{
    private static readonly FileSystemPathComparisonCache PathComparisonCache = new(
        IsCaseSensitiveDirectory,
        DefaultPathStringComparison);

    internal static StringComparer PathComparer { get; } = new FileSystemAwarePathComparer();

    public static T NotNull<T>(T value, string paramName) where T : class
    {
        if (value is null)
            throw new ArgumentNullException(paramName);

        return value;
    }

    public static string NotNullOrWhiteSpace(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", paramName);

        return value;
    }

    public static bool IsWindows()
    {
#if NET472
        return true;
#else
        return OperatingSystem.IsWindows();
#endif
    }

    public static StringComparison PathStringComparison()
        => DefaultPathStringComparison();

    private static StringComparison DefaultPathStringComparison()
        => IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static StringComparison GetPathStringComparison(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                return PathComparisonCache.GetComparison(directory);
        }
        catch
        {
            // fall back to the platform default below
        }

        return IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : PathStringComparison();
    }

    public static StringComparison GetPathStringComparisonForPath(string path)
    {
        var current = Path.GetFullPath(path);
        if (!Directory.Exists(current))
            current = Path.GetDirectoryName(current) ?? current;

        while (!Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                return PathStringComparison();
            current = parent;
        }

        return GetPathStringComparison(current);
    }

    public static string GetRelativePath(string relativeTo, string path)
    {
#if NET472
        var basePath = Path.GetFullPath(relativeTo)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var baseUri = new Uri(basePath);
        var targetUri = new Uri(Path.GetFullPath(path));
        // Note: this URI-based fallback does not round-trip literal '%' path segments on .NET Framework.
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

    private static bool IsCaseSensitiveDirectory(string directory)
    {
        var probeName = "powerforge-case-" + Guid.NewGuid().ToString("N") + "a.tmp";
        var probePath = Path.Combine(directory, probeName);
        var alternatePath = Path.Combine(directory, probeName.ToUpperInvariant());
        try
        {
            File.WriteAllText(probePath, string.Empty);
            return !File.Exists(alternatePath);
        }
        finally
        {
            TryDeleteFile(probePath);
            TryDeleteFile(alternatePath);
        }
    }

    private static bool IsMacOS()
    {
#if NET472
        return false;
#else
        return OperatingSystem.IsMacOS();
#endif
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private sealed class FileSystemAwarePathComparer : StringComparer
    {
        public override int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return string.Compare(x, y, ResolveComparison(x, y));
        }

        public override bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return string.Equals(x, y, ResolveComparison(x, y));
        }

        public override int GetHashCode(string obj)
            => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);

        private static StringComparison ResolveComparison(string first, string second)
            => GetPathStringComparisonForPath(first) == StringComparison.OrdinalIgnoreCase ||
               GetPathStringComparisonForPath(second) == StringComparison.OrdinalIgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }

    public static string GetSha256Hex(X509Certificate2 certificate)
    {
#if NET472
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(certificate.RawData);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
#else
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
#endif
    }

    public static Task<Stream> ReadAsStreamAsync(HttpContent content, CancellationToken cancellationToken)
    {
#if NET472
        // NET472: HttpContent.ReadAsStreamAsync does not accept a CancellationToken.
        // Cancellation is only checked eagerly before the read begins.
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStreamAsync();
#else
        return content.ReadAsStreamAsync(cancellationToken);
#endif
    }
}

internal sealed class FileSystemPathComparisonCache
{
    private readonly ConcurrentDictionary<string, Lazy<StringComparison>> _comparisons;
    private readonly Func<string, bool> _caseSensitivityProbe;
    private readonly Func<StringComparison> _fallback;

    internal FileSystemPathComparisonCache(
        Func<string, bool> caseSensitivityProbe,
        Func<StringComparison> fallback)
    {
        _caseSensitivityProbe = caseSensitivityProbe ?? throw new ArgumentNullException(nameof(caseSensitivityProbe));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        // Windows directories can independently opt into case sensitivity, so case-variant
        // paths must never share a cached probe result even when the host default is insensitive.
        _comparisons = new ConcurrentDictionary<string, Lazy<StringComparison>>(StringComparer.Ordinal);
    }

    internal StringComparison GetComparison(string directory)
    {
        string key = NormalizeDirectoryKey(directory);
        return _comparisons.GetOrAdd(
            key,
            path => new Lazy<StringComparison>(
                () => Probe(path),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private StringComparison Probe(string directory)
    {
        try
        {
            return _caseSensitivityProbe(directory)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
        }
        catch
        {
            return _fallback();
        }
    }

    private static string NormalizeDirectoryKey(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string? root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.Ordinal)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
