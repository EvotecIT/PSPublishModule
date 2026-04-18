using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Web;

public static partial class WebLinkService
{
    /// <summary>Imports Pretty Links-style CSV exports into PowerForge shortlink JSON.</summary>
    public static WebLinkShortlinkImportResult ImportPrettyLinks(WebLinkShortlinkImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SourcePath))
            throw new ArgumentException("SourcePath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var sourcePath = Path.GetFullPath(options.SourcePath);
        var outputPath = Path.GetFullPath(options.OutputPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Pretty Links CSV not found: {sourcePath}", sourcePath);

        var warnings = new List<string>();
        var sourceOriginPath = string.IsNullOrWhiteSpace(options.SourceOriginPath)
            ? options.SourcePath
            : options.SourceOriginPath;
        var imported = ReadPrettyLinksCsv(sourcePath, sourceOriginPath, options, warnings).ToList();
        var existing = options.MergeWithExisting && File.Exists(outputPath)
            ? ReadExistingShortlinks(outputPath)
            : new List<LinkShortlinkRule>();

        var existingCount = existing.Count;
        var merged = MergeShortlinks(existing, imported, options.ReplaceExisting, options.ShortHost, out var skippedCount);
        WriteShortlinkJson(outputPath, merged);

        return new WebLinkShortlinkImportResult
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            ExistingCount = existingCount,
            ImportedCount = imported.Count,
            WrittenCount = merged.Count,
            SkippedDuplicateCount = skippedCount,
            WarningCount = warnings.Count,
            Warnings = warnings.ToArray()
        };
    }

    private static IEnumerable<LinkShortlinkRule> ReadPrettyLinksCsv(
        string sourcePath,
        string sourceOriginPath,
        WebLinkShortlinkImportOptions options,
        List<string> warnings)
    {
        var lines = File.ReadAllLines(sourcePath);
        if (lines.Length <= 1)
            yield break;

        var header = SplitCsvLine(lines[0]);
        var slugIndex = FindHeader(header, "slug", "link_slug", "link slug", "pretty_slug", "pretty slug", "short_slug", "short slug");
        var prettyUrlIndex = FindHeader(header, "pretty_link", "pretty link", "pretty_url", "pretty url", "short_url", "short url", "shortlink", "short_link", "short link", "path");
        var targetIndex = FindHeader(header, "target_url", "target url", "target", "destination", "destination_url", "destination url", "redirect_url", "redirect url", "url");
        var titleIndex = FindHeader(header, "title", "name", "link_name", "link name");
        var descriptionIndex = FindHeader(header, "description", "desc");
        var clicksIndex = FindHeader(header, "clicks", "click_count", "click count", "hits", "visits");
        var statusIndex = FindHeader(header, "redirect_type", "redirect type", "status", "status_code", "status code");
        var createdIndex = FindHeader(header, "created_at", "created at", "created", "createdAt");
        var updatedIndex = FindHeader(header, "updated_at", "updated at", "updated", "updatedAt", "last_updated_at", "last updated at", "lastUpdatedAt");
        var groupIndex = FindHeader(header, "group", "group_name", "group name", "category", "categories", "link_categories", "link categories");
        var tagsIndex = FindHeader(header, "tags", "tag", "link_tags", "link tags", "keywords");
        var idIndex = FindHeader(header, "id", "link_id", "link id", "link_cpt_id", "link cpt id");

        if (targetIndex < 0)
        {
            warnings.Add("Pretty Links import skipped: CSV does not contain a target URL column.");
            yield break;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var parts = SplitCsvLine(lines[i]);
            var target = ReadPart(parts, targetIndex);
            if (string.IsNullOrWhiteSpace(target))
            {
                warnings.Add($"Row {i + 1}: skipped because target URL is empty.");
                continue;
            }

            var rawSlug = ReadPart(parts, slugIndex);
            if (string.IsNullOrWhiteSpace(rawSlug))
                rawSlug = ReadPart(parts, prettyUrlIndex);
            if (string.IsNullOrWhiteSpace(rawSlug))
                rawSlug = SlugifyShortlink(ReadPart(parts, titleIndex));

            var parsed = ParseImportedShortlinkPath(rawSlug, options.PathPrefix);
            if (string.IsNullOrWhiteSpace(parsed.Slug))
            {
                warnings.Add($"Row {i + 1}: skipped because slug is empty.");
                continue;
            }

            yield return new LinkShortlinkRule
            {
                Slug = parsed.Slug,
                Host = string.IsNullOrWhiteSpace(options.Host) ? null : options.Host.Trim(),
                PathPrefix = parsed.PathPrefix,
                TargetUrl = target.Trim(),
                Status = ParseRedirectStatus(ReadPart(parts, statusIndex), options.Status),
                Title = NullIfWhiteSpace(ReadPart(parts, titleIndex)),
                Description = NullIfWhiteSpace(ReadPart(parts, descriptionIndex)),
                Tags = BuildImportedTags(ReadPart(parts, groupIndex), ReadPart(parts, tagsIndex), options.Tags),
                Owner = NullIfWhiteSpace(options.Owner),
                Source = "imported-pretty-links",
                Notes = BuildImportNote(ReadPart(parts, idIndex)),
                ImportedHits = ParsePositiveInt(ReadPart(parts, clicksIndex)),
                AllowExternal = options.AllowExternal,
                Enabled = true,
                CreatedAt = NullIfWhiteSpace(ReadPart(parts, createdIndex)),
                UpdatedAt = NullIfWhiteSpace(ReadPart(parts, updatedIndex)),
                OriginPath = sourceOriginPath,
                OriginLine = i + 1
            };
        }
    }

    private static List<LinkShortlinkRule> ReadExistingShortlinks(string path)
    {
        var shortlinks = new List<LinkShortlinkRule>();
        var usedSources = new List<string>();
        var missingSources = new List<string>();
        LoadShortlinkJson(path, shortlinks, usedSources, missingSources);
        return shortlinks;
    }

    private static List<LinkShortlinkRule> MergeShortlinks(
        List<LinkShortlinkRule> existing,
        List<LinkShortlinkRule> imported,
        bool replaceExisting,
        string? shortHost,
        out int skippedCount)
    {
        skippedCount = 0;
        var merged = new List<LinkShortlinkRule>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var shortlink in existing.Where(static item => item is not null))
        {
            index[BuildShortlinkImportKey(shortlink, shortHost)] = merged.Count;
            merged.Add(shortlink);
        }

        foreach (var shortlink in imported.Where(static item => item is not null))
        {
            var key = BuildShortlinkImportKey(shortlink, shortHost);
            if (index.TryGetValue(key, out var existingIndex))
            {
                if (replaceExisting)
                    merged[existingIndex] = shortlink;
                else
                    skippedCount++;
                continue;
            }

            index[key] = merged.Count;
            merged.Add(shortlink);
        }

        return merged
            .OrderBy(static item => item.Host ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.PathPrefix ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteShortlinkJson(string outputPath, IReadOnlyList<LinkShortlinkRule> shortlinks)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new { shortlinks };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, ShortlinkImportJsonOptions), Utf8NoBom);
    }

    private static (string Slug, string? PathPrefix) ParseImportedShortlinkPath(string value, string? configuredPathPrefix)
    {
        var path = value.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            path = uri.AbsolutePath;
        }

        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
            path = path[..queryIndex];
        var hashIndex = path.IndexOf('#');
        if (hashIndex >= 0)
            path = path[..hashIndex];

        path = path.Trim().Trim('/');
        var configuredPrefix = string.IsNullOrWhiteSpace(configuredPathPrefix)
            ? null
            : "/" + configuredPathPrefix.Trim().Trim('/');
        var configuredPrefixSegment = configuredPrefix?.Trim('/');
        if (!string.IsNullOrWhiteSpace(configuredPrefixSegment) &&
            (string.Equals(path, configuredPrefixSegment, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(configuredPrefixSegment + "/", StringComparison.OrdinalIgnoreCase)))
        {
            path = path[configuredPrefixSegment.Length..].Trim('/');
        }

        var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return (string.Empty, configuredPrefix);

        var slug = SlugifyShortlink(parts[^1]);
        if (!string.IsNullOrWhiteSpace(configuredPrefix))
            return (slug, configuredPrefix);

        var prefix = parts.Length > 1
            ? "/" + string.Join("/", parts.Take(parts.Length - 1).Select(SlugifyShortlink).Where(static part => !string.IsNullOrWhiteSpace(part)))
            : null;
        return (slug, string.IsNullOrWhiteSpace(prefix) ? null : prefix);
    }

    private static string[] BuildImportedTags(string rowGroups, string rowTags, string[] optionTags)
    {
        var tags = new List<string>();
        tags.AddRange(optionTags ?? Array.Empty<string>());
        if (!string.IsNullOrWhiteSpace(rowGroups))
        {
            tags.AddRange(rowGroups.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanImportedTag)
                .Where(static tag => !string.IsNullOrWhiteSpace(tag)));
        }
        if (!string.IsNullOrWhiteSpace(rowTags))
        {
            tags.AddRange(rowTags.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanImportedTag)
                .Where(static tag => !string.IsNullOrWhiteSpace(tag)));
        }

        return tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CleanImportedTag(string value)
        => value.Trim().Trim('"', '\'').Trim();

    private static string? BuildImportNote(string id)
        => string.IsNullOrWhiteSpace(id) ? null : "Pretty Links id: " + id.Trim();

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParsePositiveInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;

    private static int ParseRedirectStatus(string value, int defaultStatus)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed is 301 or 302 or 307 or 308
            ? parsed
            : defaultStatus;

    private static string BuildShortlinkImportKey(LinkShortlinkRule shortlink, string? shortHost)
        => string.Join("|",
            NormalizeRedirectGraphHost(shortlink.Host),
            NormalizeShortlinkImportPrefix(shortlink.PathPrefix, shortlink.Host, shortHost),
            shortlink.Slug ?? string.Empty);

    private static string NormalizeShortlinkImportPrefix(string? pathPrefix, string? host, string? shortHost)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return !string.IsNullOrWhiteSpace(host) &&
                   !string.IsNullOrWhiteSpace(shortHost) &&
                   host.Trim().Equals(shortHost.Trim(), StringComparison.OrdinalIgnoreCase)
                ? "/"
                : "/go";
        }

        var trimmed = pathPrefix.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            trimmed = "/" + trimmed.TrimStart('/');
        trimmed = trimmed.TrimEnd('/');
        return string.IsNullOrWhiteSpace(trimmed) ? "/" : trimmed;
    }

    private static string SlugifyShortlink(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = new List<char>();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                result.Add(ch);
            }
            else if (ch is '-' or '_' or '.')
            {
                result.Add(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                result.Add('-');
            }
        }

        var slug = new string(result.ToArray()).Trim('-', '.', '_');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug;
    }

    private static readonly JsonSerializerOptions ShortlinkImportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

/// <summary>Options for importing Pretty Links CSV exports into PowerForge shortlink JSON.</summary>
public sealed class WebLinkShortlinkImportOptions
{
    /// <summary>Source Pretty Links CSV path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Optional source path label persisted in imported shortlink metadata.</summary>
    public string? SourceOriginPath { get; set; }
    /// <summary>Output shortlinks JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Optional host for imported shortlinks.</summary>
    public string? Host { get; set; }
    /// <summary>Configured shortlink host used to resolve implicit root prefixes.</summary>
    public string? ShortHost { get; set; }
    /// <summary>Optional path prefix for imported shortlinks.</summary>
    public string? PathPrefix { get; set; }
    /// <summary>Default owner assigned to imported shortlinks.</summary>
    public string? Owner { get; set; }
    /// <summary>Tags assigned to every imported shortlink.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
    /// <summary>HTTP status for imported shortlinks.</summary>
    public int Status { get; set; } = 302;
    /// <summary>Allow absolute external targets.</summary>
    public bool AllowExternal { get; set; } = true;
    /// <summary>Merge with existing output shortlinks instead of replacing the file.</summary>
    public bool MergeWithExisting { get; set; } = true;
    /// <summary>Replace existing shortlinks with the same host/prefix/slug key.</summary>
    public bool ReplaceExisting { get; set; }
}

/// <summary>Result from a Pretty Links shortlink import.</summary>
public sealed class WebLinkShortlinkImportResult
{
    /// <summary>Resolved source CSV path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Resolved output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Existing shortlinks loaded from the output file.</summary>
    public int ExistingCount { get; set; }
    /// <summary>Shortlinks imported from the source CSV.</summary>
    public int ImportedCount { get; set; }
    /// <summary>Total shortlinks written to the output file.</summary>
    public int WrittenCount { get; set; }
    /// <summary>Imported rows skipped because an existing shortlink had the same key.</summary>
    public int SkippedDuplicateCount { get; set; }
    /// <summary>Warning count.</summary>
    public int WarningCount { get; set; }
    /// <summary>Import warnings.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}
