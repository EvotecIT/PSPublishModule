using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static string[] ResolveOutputCandidates(string baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();
        if (IsExternalUri(value))
            return Array.Empty<string>();
        var resolved = ResolvePath(baseDir, value);
        if (string.IsNullOrWhiteSpace(resolved))
            return Array.Empty<string>();
        return new[] { Path.GetFullPath(resolved) };
    }

    private static bool AreExpectedOutputsPresent(string[] outputs)
    {
        if (outputs.Length == 0)
            return true;

        foreach (var output in outputs)
        {
            if (File.Exists(output))
                continue;
            if (Directory.Exists(output))
                continue;
            return false;
        }

        return true;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool? GetBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.True ? true :
               value.ValueKind == JsonValueKind.False ? false : null;
    }

    private static bool HasProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        return element.TryGetProperty(name, out _);
    }

    private static bool HasAnyProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || names is null || names.Length == 0)
            return false;

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (element.TryGetProperty(name, out _))
                return true;
        }

        return false;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var num)) return num;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static long? GetLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var num)) return num;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var num)) return num;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static int[] ParseIntList(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<int>();

        var list = new List<int>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            foreach (var token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), out var parsed) && parsed > 0)
                    list.Add(parsed);
            }
        }

        return list
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
    }

    private static WebApiDetailLevel ParseApiDetailLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return WebApiDetailLevel.None;
        return Enum.TryParse<WebApiDetailLevel>(value, true, out var parsed) ? parsed : WebApiDetailLevel.None;
    }

    private static void TryResolveApiFragmentsFromTheme(string siteConfigPath, ref string? header, ref string? footer)
    {
        if (string.IsNullOrWhiteSpace(siteConfigPath))
            return;

        try
        {
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(siteConfigPath, WebCliJson.Options);
            if (string.IsNullOrWhiteSpace(spec.DefaultTheme))
                return;

            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
            var themesRoot = string.IsNullOrWhiteSpace(plan.ThemesRoot)
                ? Path.Combine(plan.RootPath, "themes")
                : plan.ThemesRoot;
            var themeRoot = Path.Combine(themesRoot, spec.DefaultTheme);
            if (!Directory.Exists(themeRoot))
                return;

            var loader = new ThemeLoader();
            var manifest = loader.Load(themeRoot, themesRoot);
            if (manifest is null)
                return;

            if (string.IsNullOrWhiteSpace(header))
            {
                var candidate = loader.ResolvePartialPath(themeRoot, manifest, "api-header");
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    header = candidate;
                else
                {
                    candidate = loader.ResolvePartialPath(themeRoot, manifest, "header");
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                        header = candidate;
                }
            }

            if (string.IsNullOrWhiteSpace(footer))
            {
                var candidate = loader.ResolvePartialPath(themeRoot, manifest, "api-footer");
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    footer = candidate;
                else
                {
                    candidate = loader.ResolvePartialPath(themeRoot, manifest, "footer");
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                        footer = candidate;
                }
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static string[]? GetArrayOfStrings(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString() ?? string.Empty);
            else if (item.ValueKind != JsonValueKind.Null)
                list.Add(item.ToString());
        }
        return list.Count == 0 ? null : list.ToArray();
    }

    private static JsonElement[]? GetArrayOfObjects(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<JsonElement>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
                list.Add(item.Clone());
        }

        return list.Count == 0 ? null : list.ToArray();
    }

    private static WebApiDocsSourceUrlMapping[] GetApiDocsSourceUrlMappings(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || names is null || names.Length == 0)
            return Array.Empty<WebApiDocsSourceUrlMapping>();

        JsonElement array = default;
        var found = false;
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (!element.TryGetProperty(name, out var candidate))
                continue;
            if (candidate.ValueKind != JsonValueKind.Array)
                continue;
            array = candidate;
            found = true;
            break;
        }

        if (!found)
            return Array.Empty<WebApiDocsSourceUrlMapping>();

        var results = new List<WebApiDocsSourceUrlMapping>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var pathPrefix = GetString(item, "pathPrefix") ?? GetString(item, "prefix");
            var urlPattern = GetString(item, "urlPattern") ?? GetString(item, "url") ?? GetString(item, "sourceUrl");
            var stripPathPrefix = GetBool(item, "stripPathPrefix") ?? GetBool(item, "stripPrefix") ?? false;
            if (string.IsNullOrWhiteSpace(pathPrefix) || string.IsNullOrWhiteSpace(urlPattern))
                continue;

            results.Add(new WebApiDocsSourceUrlMapping
            {
                PathPrefix = pathPrefix,
                UrlPattern = urlPattern,
                StripPathPrefix = stripPathPrefix
            });
        }

        return results.ToArray();
    }

    private static WebSitemapEntry[] GetSitemapEntries(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return Array.Empty<WebSitemapEntry>();
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<WebSitemapEntry>();

        var list = new List<WebSitemapEntry>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var path = GetString(item, "path") ?? GetString(item, "route") ?? GetString(item, "url");
            if (string.IsNullOrWhiteSpace(path)) continue;

            var alternates = new List<WebSitemapAlternate>();
            if (item.TryGetProperty("alternates", out var alternatesElement) &&
                alternatesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var alternate in alternatesElement.EnumerateArray())
                {
                    if (alternate.ValueKind != JsonValueKind.Object)
                        continue;
                    var hrefLang = GetString(alternate, "hrefLang") ?? GetString(alternate, "hreflang");
                    var altPath = GetString(alternate, "path") ?? GetString(alternate, "route");
                    var altUrl = GetString(alternate, "url") ?? GetString(alternate, "href");
                    if (string.IsNullOrWhiteSpace(hrefLang) ||
                        (string.IsNullOrWhiteSpace(altPath) && string.IsNullOrWhiteSpace(altUrl)))
                        continue;
                    alternates.Add(new WebSitemapAlternate
                    {
                        HrefLang = hrefLang,
                        Path = string.IsNullOrWhiteSpace(altPath) ? "/" : altPath,
                        Url = altUrl
                    });
                }
            }

            list.Add(new WebSitemapEntry
            {
                Path = path,
                Title = GetString(item, "title"),
                Description = GetString(item, "description"),
                Section = GetString(item, "section"),
                ChangeFrequency = GetString(item, "changefreq") ?? GetString(item, "changeFrequency"),
                Priority = GetString(item, "priority"),
                LastModified = GetString(item, "lastmod") ?? GetString(item, "lastModified"),
                Alternates = alternates.ToArray(),
                ImageUrls = GetArrayOfStrings(item, "images") ?? Array.Empty<string>(),
                VideoUrls = GetArrayOfStrings(item, "videos") ?? Array.Empty<string>()
            });
        }
        return list.ToArray();
    }

    private static string? ResolvePath(string baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Path.IsPathRooted(value) ? value : Path.Combine(baseDir, value);
    }

    private static string ResolvePathWithinRoot(string baseDir, string? value, string defaultRelativePath)
    {
        var normalizedRoot = NormalizeRootPath(baseDir);
        var candidate = string.IsNullOrWhiteSpace(value)
            ? Path.Combine(baseDir, defaultRelativePath)
            : ResolvePath(baseDir, value);
        var resolved = Path.GetFullPath(candidate ?? Path.Combine(baseDir, defaultRelativePath));
        if (!IsPathWithinRoot(normalizedRoot, resolved))
            throw new InvalidOperationException($"Path must resolve under pipeline root: {value}");
        return resolved;
    }

    private static string NormalizeRootPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string normalizedRoot, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(normalizedRoot, FileSystemPathComparison);
    }

    private static string[] BuildIgnoreNavPatternsForPipeline(List<string> userPatterns, bool useDefaults)
    {
        if (!useDefaults)
            return userPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        var defaults = new WebAuditOptions().IgnoreNavFor;
        if (userPatterns.Count == 0)
            return defaults;

        return defaults.Concat(userPatterns)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] BuildIgnoreMediaPatternsForPipeline(List<string> userPatterns, bool useDefaults)
    {
        if (!useDefaults)
            return userPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        var defaults = new WebAuditOptions().IgnoreMediaFor;
        if (userPatterns.Count == 0)
            return defaults;

        return defaults.Concat(userPatterns)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSummaryPathForPipeline(bool summaryEnabled, string? summaryPath)
    {
        if (!summaryEnabled && string.IsNullOrWhiteSpace(summaryPath))
            return null;

        return string.IsNullOrWhiteSpace(summaryPath) ? "audit-summary.json" : summaryPath;
    }

    private static string? ResolveSarifPathForPipeline(bool sarifEnabled, string? sarifPath)
    {
        if (!sarifEnabled && string.IsNullOrWhiteSpace(sarifPath))
            return null;

        return string.IsNullOrWhiteSpace(sarifPath) ? "audit.sarif.json" : sarifPath;
    }

    private static WebAuditNavProfile[] LoadAuditNavProfilesForPipeline(string baseDir, string? navProfilesPath)
    {
        if (string.IsNullOrWhiteSpace(navProfilesPath))
            return Array.Empty<WebAuditNavProfile>();

        var resolvedPath = ResolvePath(baseDir, navProfilesPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            throw new FileNotFoundException($"Nav profile file not found: {navProfilesPath}", resolvedPath ?? navProfilesPath);

        using var stream = File.OpenRead(resolvedPath);
        var profiles = JsonSerializer.Deserialize(stream, WebCliJson.Context.WebAuditNavProfileArray)
                       ?? Array.Empty<WebAuditNavProfile>();
        return profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Match))
            .ToArray();
    }

    private static WebAuditMediaProfile[] LoadAuditMediaProfilesForPipeline(string baseDir, string? mediaProfilesPath)
    {
        if (string.IsNullOrWhiteSpace(mediaProfilesPath))
            return Array.Empty<WebAuditMediaProfile>();

        var resolvedPath = ResolvePath(baseDir, mediaProfilesPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            throw new FileNotFoundException($"Media profile file not found: {mediaProfilesPath}", resolvedPath ?? mediaProfilesPath);

        using var stream = File.OpenRead(resolvedPath);
        var profiles = JsonSerializer.Deserialize(stream, WebCliJson.Context.WebAuditMediaProfileArray)
                       ?? Array.Empty<WebAuditMediaProfile>();
        return profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Match))
            .ToArray();
    }
}
