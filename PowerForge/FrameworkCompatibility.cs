using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge;

internal static class FrameworkCompatibility
{
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
