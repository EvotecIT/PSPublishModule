using System.Globalization;
using System.Text;

namespace PowerForge.Web;

public static partial class WebLinkService
{
    /// <summary>Generates Apache/link-service compatible CSV redirects for legacy WordPress AMP aliases.</summary>
    public static WebLegacyAmpRedirectResult GenerateLegacyAmpRedirects(WebLegacyAmpRedirectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SourceCsvPath))
            throw new ArgumentException("SourceCsvPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputCsvPath))
            throw new ArgumentException("OutputCsvPath is required.", nameof(options));

        var sourcePath = Path.GetFullPath(options.SourceCsvPath);
        var outputPath = Path.GetFullPath(options.OutputCsvPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Legacy redirect source CSV not found.", sourcePath);

        var scheme = string.IsNullOrWhiteSpace(options.DefaultScheme) ? "https" : options.DefaultScheme.Trim().ToLowerInvariant();
        var defaultLanguage = NormalizeLegacyAmpLanguage(options.DefaultLanguage, nameof(options.DefaultLanguage));
        var languageHosts = BuildLegacyAmpLanguageHosts(options, defaultLanguage);
        var lines = File.ReadAllLines(sourcePath);
        var generated = new List<LegacyAmpRedirectRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedCount = 0;

        if (lines.Length > 1)
        {
            var header = SplitCsvLine(lines[0]);
            var legacyIndex = FindHeader(header, "legacy_url", "source", "from", "redirect_from", "redirect from");
            var targetIndex = FindHeader(header, "target_url", "target", "to", "redirect_to", "redirect to");
            var statusIndex = FindHeader(header, "status", "redirect_type", "redirect type", "status_code", "status code");
            var languageIndex = FindHeader(header, "language", "lang");

            if (legacyIndex >= 0 && targetIndex >= 0)
            {
                for (var i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        skippedCount++;
                        continue;
                    }

                    var parts = SplitCsvLine(lines[i]);
                    if (parts.Length <= legacyIndex || parts.Length <= targetIndex)
                    {
                        skippedCount++;
                        continue;
                    }

                    var legacyUrl = parts[legacyIndex].Trim();
                    var targetUrl = parts[targetIndex].Trim();
                    if (string.IsNullOrWhiteSpace(legacyUrl) || string.IsNullOrWhiteSpace(targetUrl))
                    {
                        skippedCount++;
                        continue;
                    }

                    var legacyPath = ResolveLegacyPathForAmp(legacyUrl);
                    if (string.IsNullOrWhiteSpace(legacyPath))
                    {
                        skippedCount++;
                        continue;
                    }

                    var ampPath = BuildAmpAliasPath(legacyPath);
                    if (string.IsNullOrWhiteSpace(ampPath))
                    {
                        skippedCount++;
                        continue;
                    }

                    var language = ReadPart(parts, languageIndex);
                    var host = ResolveLegacyAmpHost(legacyUrl, language, languageHosts, defaultLanguage);
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        skippedCount++;
                        continue;
                    }

                    var target = ResolveLegacyAmpTargetUrl(targetUrl, host, scheme, languageHosts);
                    var status = 301;
                    if (statusIndex >= 0 && statusIndex < parts.Length && int.TryParse(parts[statusIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStatus))
                        status = parsedStatus;
                    if (status is < 300 or >= 400)
                    {
                        skippedCount++;
                        continue;
                    }

                    var ampLegacyUrl = $"{scheme}://{host}{ampPath}";
                    var key = $"{ampLegacyUrl}|{target}|{status}";
                    if (!seen.Add(key))
                    {
                        skippedCount++;
                        continue;
                    }

                    generated.Add(new LegacyAmpRedirectRow(
                        ampLegacyUrl,
                        target,
                        status,
                        "generated-legacy-amp",
                        "Generated AMP continuity alias from the imported legacy WordPress redirect map."));
                }
            }
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        WriteLegacyAmpRedirectCsv(outputPath, generated);

        return new WebLegacyAmpRedirectResult
        {
            SourceCsvPath = sourcePath,
            OutputCsvPath = outputPath,
            SourceRowCount = Math.Max(0, lines.Length - 1),
            GeneratedCount = generated.Count,
            SkippedCount = skippedCount
        };
    }

    private static string NormalizeRequiredHost(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required for relative legacy AMP redirect rows.", parameterName);

        var host = value.Trim().TrimEnd('/').ToLowerInvariant();
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
                throw new ArgumentException($"{parameterName} must be a valid host name or absolute http(s) URL.", parameterName);
            if ((!string.IsNullOrWhiteSpace(uri.AbsolutePath) && uri.AbsolutePath != "/") ||
                !string.IsNullOrWhiteSpace(uri.Query) ||
                !string.IsNullOrWhiteSpace(uri.Fragment))
            {
                throw new ArgumentException($"{parameterName} must be a host name without a path.", parameterName);
            }

            host = uri.Host.ToLowerInvariant();
        }

        if (host.Contains('/', StringComparison.Ordinal) ||
            host.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException($"{parameterName} must be a host name without a path.", parameterName);
        }

        return host;
    }

    private static IReadOnlyDictionary<string, string> BuildLegacyAmpLanguageHosts(
        WebLegacyAmpRedirectOptions options,
        string defaultLanguage)
    {
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.LanguageHosts is not null)
        {
            foreach (var pair in options.LanguageHosts)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                var language = NormalizeLegacyAmpLanguage(pair.Key, $"LanguageHosts[{pair.Key}]");
                hosts[language] = NormalizeRequiredHost(pair.Value, $"LanguageHosts[{language}]");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultEnglishHost) && !hosts.ContainsKey("en"))
            hosts["en"] = NormalizeRequiredHost(options.DefaultEnglishHost, nameof(options.DefaultEnglishHost));
        if (!string.IsNullOrWhiteSpace(options.DefaultPolishHost) && !hosts.ContainsKey("pl"))
            hosts["pl"] = NormalizeRequiredHost(options.DefaultPolishHost, nameof(options.DefaultPolishHost));

        if (!hosts.ContainsKey(defaultLanguage))
        {
            throw new ArgumentException(
                $"LanguageHosts must include the default language '{defaultLanguage}' for relative legacy AMP redirect rows.",
                nameof(options));
        }

        return hosts;
    }

    private static string NormalizeLegacyAmpLanguage(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);

        var language = value.Trim().Trim('/').ToLowerInvariant();
        if (language.Length == 0 ||
            language.Contains('/', StringComparison.Ordinal) ||
            language.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException($"{parameterName} must be a language code such as 'en' or 'pl'.", parameterName);
        }

        return language;
    }

    private static string? ResolveLegacyPathForAmp(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.IsNullOrWhiteSpace(uri.Query))
                return null;
            return uri.AbsolutePath;
        }

        if (trimmed.Contains('?', StringComparison.Ordinal))
            return null;
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed.TrimStart('/');
    }

    private static string? BuildAmpAliasPath(string path)
    {
        var normalized = NormalizeCanonicalLegacyPath(path);
        if (normalized.Equals("/", StringComparison.Ordinal) ||
            normalized.Equals("/amp/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/amp/", StringComparison.OrdinalIgnoreCase))
            return null;

        return normalized.TrimEnd('/') + "/amp/";
    }

    private static string ResolveLegacyAmpHost(
        string legacyUrl,
        string language,
        IReadOnlyDictionary<string, string> languageHosts,
        string defaultLanguage)
    {
        if (Uri.TryCreate(legacyUrl.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return uri.Host.ToLowerInvariant();

        var languageKey = string.IsNullOrWhiteSpace(language)
            ? defaultLanguage
            : NormalizeLegacyAmpLanguage(language, "language");
        if (languageHosts.TryGetValue(languageKey, out var host))
            return host;
        if (languageHosts.TryGetValue(defaultLanguage, out var defaultHost))
            return defaultHost;

        throw new InvalidOperationException($"Legacy AMP language host map is missing the default language '{defaultLanguage}'.");
    }

    private static string ResolveLegacyAmpTargetUrl(
        string targetUrl,
        string host,
        string scheme,
        IReadOnlyDictionary<string, string> languageHosts)
    {
        var trimmed = targetUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return trimmed;

        var path = NormalizeCanonicalLegacyPath(trimmed);
        // Legacy AMP redirects only strip the language prefix for the host that owns that language root.
        foreach (var pair in languageHosts)
        {
            if (!host.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                continue;

            var prefix = "/" + pair.Key.Trim('/') + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                path = "/" + path[prefix.Length..];
            break;
        }

        return $"{scheme}://{host}{path}";
    }

    private static string NormalizeCanonicalLegacyPath(string path)
    {
        var normalized = path.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized.TrimStart('/');
        if (normalized.Length > 1)
            normalized = normalized.TrimEnd('/');
        // Legacy WordPress AMP aliases and redirect targets were slash-canonical.
        return normalized + "/";
    }

    private static void WriteLegacyAmpRedirectCsv(string outputPath, IEnumerable<LegacyAmpRedirectRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("\"legacy_url\",\"target_url\",\"status\",\"match_kind\",\"notes\"");
        foreach (var row in rows
                     .OrderBy(static row => row.LegacyUrl, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static row => row.TargetUrl, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(Csv(row.LegacyUrl)).Append(',')
                .Append(Csv(row.TargetUrl)).Append(',')
                .Append(Csv(row.Status.ToString(CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(row.MatchKind)).Append(',')
                .Append(Csv(row.Notes)).AppendLine();
        }

        File.WriteAllText(outputPath, builder.ToString(), Utf8NoBom);
    }

    private static string Csv(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed record LegacyAmpRedirectRow(
        string LegacyUrl,
        string TargetUrl,
        int Status,
        string MatchKind,
        string Notes);
}
