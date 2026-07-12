using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Resolves the latest package version from NuGet sources (v3 index or local paths).
/// </summary>
public sealed class NuGetPackageVersionResolver
{
    private readonly ILogger _logger;
    private readonly HttpClient _client;
    private readonly Dictionary<string, string?> _packageBaseCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new resolver.
    /// </summary>
    public NuGetPackageVersionResolver(ILogger logger, HttpClient? client = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? new HttpClient();
    }

    /// <summary>
    /// Resolves the latest version from the provided sources (URLs or local folders).
    /// </summary>
    public Version? ResolveLatest(
        string packageId,
        IReadOnlyList<string>? sources,
        RepositoryCredential? credential,
        bool includePrerelease)
        => ResolveLatest(packageId, sources, credential, credentialsBySource: null, includePrerelease);

    /// <summary>
    /// Resolves the latest version from the provided sources with optional source-scoped credentials.
    /// </summary>
    public Version? ResolveLatest(
        string packageId,
        IReadOnlyList<string>? sources,
        RepositoryCredential? credential,
        IReadOnlyDictionary<string, RepositoryCredential>? credentialsBySource,
        bool includePrerelease)
    {
        var latest = ResolveLatestPackageVersion(packageId, sources, credential, credentialsBySource, includePrerelease);
        if (string.IsNullOrWhiteSpace(latest))
            return null;

        return Version.TryParse(PackageVersionUtility.GetNumericVersion(latest!), out var parsed)
            ? parsed
            : null;
    }

    internal string? ResolveLatestPackageVersion(
        string packageId,
        IReadOnlyList<string>? sources,
        RepositoryCredential? credential,
        IReadOnlyDictionary<string, RepositoryCredential>? credentialsBySource,
        bool includePrerelease)
    {
        if (string.IsNullOrWhiteSpace(packageId)) return null;

        var normalizedSources = (sources ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedSources.Length == 0)
            normalizedSources = new[] { "https://api.nuget.org/v3/index.json" };

        string? latest = null;
        foreach (var source in normalizedSources)
        {
            string? candidate = null;
            var sourceCredential = ResolveCredentialForSource(source, credentialsBySource) ?? credential;
            if (IsLocalPath(source))
                candidate = ResolvePackageVersionFromLocalPath(packageId, source, includePrerelease);
            else
                candidate = ResolvePackageVersionFromV3Source(packageId, source, sourceCredential, includePrerelease);

            if (candidate is null) continue;
            if (latest is null || PackageVersionUtility.Compare(candidate, latest) > 0)
                latest = candidate;
        }

        return latest;
    }

    private static RepositoryCredential? ResolveCredentialForSource(
        string source,
        IReadOnlyDictionary<string, RepositoryCredential>? credentialsBySource)
    {
        if (credentialsBySource is null || credentialsBySource.Count == 0)
            return null;

        var normalized = source.Trim();
        if (credentialsBySource.TryGetValue(normalized, out var credential))
            return credential;

        var trimmed = normalized.TrimEnd('/');
        if (credentialsBySource.TryGetValue(trimmed, out credential))
            return credential;

        if (normalized.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
        {
            var withoutIndex = normalized.Substring(0, normalized.Length - "/index.json".Length).TrimEnd('/');
            if (credentialsBySource.TryGetValue(withoutIndex, out credential))
                return credential;
        }

        return null;
    }

    private static bool IsLocalPath(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return uri.IsFile;

        return Path.IsPathRooted(source) || source.StartsWith(".", StringComparison.Ordinal);
    }

    private string? ResolvePackageVersionFromLocalPath(string packageId, string path, bool includePrerelease)
    {
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            if (!Directory.Exists(full))
            {
                _logger.Verbose($"NuGet version resolve: local path not found: {full}");
                return null;
            }

            var prefix = packageId + ".";
            string? latest = null;
            foreach (var file in Directory.EnumerateFiles(full, "*.nupkg", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name is null) continue;
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                var verText = name.Substring(prefix.Length);
                if (verText.EndsWith(".symbols", StringComparison.OrdinalIgnoreCase))
                    verText = verText.Substring(0, verText.Length - 8);

                if (!TryNormalizeVersion(verText, includePrerelease, out var version))
                    continue;

                if (latest is null || PackageVersionUtility.Compare(version, latest) > 0) latest = version;
            }

            return latest;
        }
        catch (Exception ex)
        {
            _logger.Verbose($"NuGet version resolve: local path error: {ex.Message}");
            return null;
        }
    }

    private string? ResolvePackageVersionFromV3Source(string packageId, string source, RepositoryCredential? credential, bool includePrerelease)
    {
        try
        {
            var baseAddress = ResolvePackageBaseAddress(source, credential);
            if (string.IsNullOrWhiteSpace(baseAddress))
                return null;

            var id = packageId.ToLowerInvariant();
            var indexUrl = baseAddress!.TrimEnd('/') + "/" + id + "/index.json";
            var request = new HttpRequestMessage(HttpMethod.Get, indexUrl);
            ApplyCredential(request, credential);

            var response = _client.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                _logger.Verbose($"NuGet version resolve: {packageId} not found in {source} ({(int)response.StatusCode}).");
                return null;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("versions", out var versions))
                return null;

            string? latest = null;
            foreach (var v in versions.EnumerateArray())
            {
                var text = v.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!TryNormalizeVersion(text, includePrerelease, out var version)) continue;
                if (latest is null || PackageVersionUtility.Compare(version, latest) > 0) latest = version;
            }

            return latest;
        }
        catch (Exception ex)
        {
            _logger.Verbose($"NuGet version resolve: {packageId} from {source} failed: {ex.Message}");
            return null;
        }
    }

    private string? ResolvePackageBaseAddress(string source, RepositoryCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        if (_packageBaseCache.TryGetValue(source, out var cached))
            return cached;

        if (!source.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = source.TrimEnd('/');
            _packageBaseCache[source] = normalized;
            return normalized;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, source);
            ApplyCredential(request, credential);
            var response = _client.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                _logger.Verbose($"NuGet source index not found: {source} ({(int)response.StatusCode}).");
                _packageBaseCache[source] = null;
                return null;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("resources", out var resources))
                return null;

            foreach (var res in resources.EnumerateArray())
            {
                var typeValue = string.Empty;
                if (res.TryGetProperty("@type", out var typeElement))
                {
                    if (typeElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in typeElement.EnumerateArray())
                        {
                            if (t.ValueKind == JsonValueKind.String &&
                                t.GetString()!.IndexOf("PackageBaseAddress", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                typeValue = t.GetString() ?? string.Empty;
                                break;
                            }
                        }
                    }
                    else if (typeElement.ValueKind == JsonValueKind.String)
                    {
                        typeValue = typeElement.GetString() ?? string.Empty;
                    }
                }

                if (typeValue.IndexOf("PackageBaseAddress", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (res.TryGetProperty("@id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                {
                    var addr = idElement.GetString();
                    if (!string.IsNullOrWhiteSpace(addr))
                    {
                        _packageBaseCache[source] = addr;
                        return addr;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Verbose($"NuGet source index parse failed: {source}. {ex.Message}");
        }

        _packageBaseCache[source] = null;
        return null;
    }

    private static void ApplyCredential(HttpRequestMessage request, RepositoryCredential? credential)
    {
        if (credential is null) return;

        var user = credential.UserName;
        var secret = credential.Secret;

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(secret))
        {
            var raw = Encoding.ASCII.GetBytes($"{user}:{secret}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            return;
        }

        if (!string.IsNullOrWhiteSpace(secret))
            request.Headers.Add("X-NuGet-ApiKey", secret);
    }

    private static bool TryNormalizeVersion(string text, bool includePrerelease, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (!PackageVersionUtility.TryNormalizeExact(text, out var normalized))
            return false;
        if (!includePrerelease && PackageVersionUtility.GetPrereleaseVersion(normalized).Length > 0)
            return false;

        version = normalized;
        return true;
    }
}
