using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace PowerForge.Web;

/// <summary>Contains guarded remote-download behavior for asset optimization.</summary>
public static partial class WebAssetOptimizer
{
    private static bool DownloadRewriteAsset(
        AssetRewriteSpec rewrite,
        string destinationPath,
        IReadOnlySet<string> protectedStoryArtifacts)
    {
        if (string.IsNullOrWhiteSpace(rewrite.SourceUrl))
            return false;
        if (!Uri.TryCreate(rewrite.SourceUrl, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps)
        {
            Trace.TraceWarning($"Asset rewrite sourceUrl is not a valid https URL: {rewrite.SourceUrl}");
            return false;
        }
        if (!IsAllowedRewriteSourceUri(sourceUri, rewrite.SourceUrlAllowedHosts))
        {
            Trace.TraceWarning($"Asset rewrite sourceUrl host is not allowed for remote download: {sourceUri.Host}");
            return false;
        }

        try
        {
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            if (destinationPath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                var css = DownloadText(sourceUri, rewrite.UserAgent);
                if (rewrite.DownloadDependencies)
                {
                    css = RewriteDownloadedCssDependencies(
                        css,
                        sourceUri,
                        destinationPath,
                        rewrite.UserAgent,
                        protectedStoryArtifacts);
                }
                File.WriteAllText(destinationPath, css, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return true;
            }
            File.WriteAllBytes(destinationPath, DownloadBytes(sourceUri, rewrite.UserAgent));
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Asset rewrite download failed for {rewrite.SourceUrl}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string RewriteDownloadedCssDependencies(
        string css,
        Uri sourceUri,
        string destinationPath,
        string? userAgent,
        IReadOnlySet<string> protectedStoryArtifacts)
    {
        if (string.IsNullOrWhiteSpace(css))
            return css;
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDir))
            return css;

        var downloaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return CssUrlRegex.Replace(css, match =>
        {
            var url = match.Groups["url"].Value.Trim();
            if (!TryResolveDownloadUri(sourceUri, url, out var resolvedUri))
                return match.Value;
            if (!downloaded.TryGetValue(resolvedUri.AbsoluteUri, out var fileName))
            {
                fileName = BuildDownloadedAssetFileName(resolvedUri);
                var localPath = Path.Combine(destinationDir, fileName);
                if (IsDownloadedDependencyPathProtected(destinationPath, fileName, protectedStoryArtifacts))
                {
                    Trace.TraceWarning($"Asset rewrite dependency destination is protected by a visual-story manifest: {localPath}");
                    return match.Value;
                }
                try
                {
                    File.WriteAllBytes(localPath, DownloadBytes(resolvedUri, userAgent));
                    downloaded[resolvedUri.AbsoluteUri] = fileName;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Asset rewrite dependency download failed for {resolvedUri}: {ex.GetType().Name}: {ex.Message}");
                    return match.Value;
                }
            }
            var quote = match.Groups["quote"].Value;
            return $"url({quote}{fileName}{quote})";
        });
    }

    internal static bool IsDownloadedDependencyPathProtectedForTesting(
        string destinationPath,
        string fileName,
        IReadOnlySet<string> protectedStoryArtifacts)
        => IsDownloadedDependencyPathProtected(destinationPath, fileName, protectedStoryArtifacts);

    private static bool IsDownloadedDependencyPathProtected(
        string destinationPath,
        string fileName,
        IReadOnlySet<string> protectedStoryArtifacts)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
                                   ?? throw new ArgumentException("Destination path must include a directory.", nameof(destinationPath));
        return protectedStoryArtifacts.Contains(Path.GetFullPath(Path.Combine(destinationDirectory, fileName)));
    }

    private static bool TryResolveDownloadUri(Uri baseUri, string rawUrl, out Uri resolvedUri)
    {
        resolvedUri = baseUri;
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            rawUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.StartsWith("#", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absoluteUri))
        {
            resolvedUri = absoluteUri;
            return resolvedUri.Scheme == Uri.UriSchemeHttp || resolvedUri.Scheme == Uri.UriSchemeHttps;
        }
        if (rawUrl.StartsWith("//", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate($"{baseUri.Scheme}:{rawUrl}", UriKind.Absolute, out var protocolRelativeUri))
                return false;
            resolvedUri = protocolRelativeUri;
            return true;
        }
        if (!Uri.TryCreate(baseUri, rawUrl, out var relativeUri))
            return false;
        resolvedUri = relativeUri;
        return resolvedUri.Scheme == Uri.UriSchemeHttp || resolvedUri.Scheme == Uri.UriSchemeHttps;
    }

    private static string BuildDownloadedAssetFileName(Uri uri)
    {
        var pathName = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(pathName))
            pathName = "asset";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            pathName = pathName.Replace(invalid, '-');
        var extension = Path.GetExtension(pathName);
        var stem = pathName[..Math.Max(0, pathName.Length - extension.Length)];
        if (string.IsNullOrWhiteSpace(stem))
            stem = "asset";
        var hash = ComputeShortHash(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        return $"{stem}.{hash}{extension}";
    }

    private static string DownloadText(Uri uri, string? userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var safeUserAgent = NormalizeHeaderSingleLine(userAgent);
        if (!string.IsNullOrWhiteSpace(safeUserAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", safeUserAgent);
        using var response = RewriteDownloadClient.Send(request);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static byte[] DownloadBytes(Uri uri, string? userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var safeUserAgent = NormalizeHeaderSingleLine(userAgent);
        if (!string.IsNullOrWhiteSpace(safeUserAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", safeUserAgent);
        using var response = RewriteDownloadClient.Send(request);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    internal static bool IsAllowedRewriteSourceUriForTesting(Uri uri, string[]? allowedHosts = null)
        => IsAllowedRewriteSourceUri(uri, allowedHosts);

    internal static string NormalizeHeaderSingleLineForTesting(string? value)
        => NormalizeHeaderSingleLine(value);

    private static string NormalizeHeaderSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var firstLine = value.Split(new[] { '\r', '\n' }, StringSplitOptions.None)[0].Trim();
        var builder = new StringBuilder(Math.Min(firstLine.Length, 512));
        foreach (var character in firstLine)
        {
            if (character is >= ' ' and <= '~')
                builder.Append(character);
            if (builder.Length >= 512)
                break;
        }
        return builder.ToString().Trim();
    }

    private static bool IsAllowedRewriteSourceUri(Uri uri, string[]? allowedHosts)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || IsUnsafeRemoteHost(uri.Host))
            return false;
        var configuredHosts = allowedHosts?
            .Select(static host => host?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty)
            .Where(static host => host.Length > 0)
            .ToArray() ?? Array.Empty<string>();
        if (configuredHosts.Length == 0)
            return false;
        var sourceHost = uri.Host.TrimEnd('.').ToLowerInvariant();
        foreach (var allowedHost in configuredHosts)
        {
            if (string.Equals(allowedHost, "*", StringComparison.Ordinal) ||
                string.Equals(sourceHost, allowedHost, StringComparison.OrdinalIgnoreCase))
                return true;
            if (allowedHost.StartsWith("*.", StringComparison.Ordinal) &&
                sourceHost.EndsWith(allowedHost[1..], StringComparison.OrdinalIgnoreCase) &&
                sourceHost.Length > allowedHost.Length - 1)
                return true;
        }
        return false;
    }

    private static bool IsUnsafeRemoteHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;
        var normalized = host.Trim().TrimEnd('.');
        if (normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(normalized, out var address) && IsUnsafeRemoteAddress(address);
    }

    private static bool IsUnsafeRemoteAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   bytes[0] == 0;
        }
        return address.IsIPv6LinkLocal ||
               address.IsIPv6SiteLocal ||
               address.IsIPv6Multicast ||
               address.Equals(IPAddress.IPv6None) ||
               address.Equals(IPAddress.IPv6Any);
    }
}
