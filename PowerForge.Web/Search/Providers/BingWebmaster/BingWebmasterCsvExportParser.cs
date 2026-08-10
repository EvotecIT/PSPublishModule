using System.Globalization;
using System.Text;

namespace PowerForge.Web;

/// <summary>Options that supply fleet identity and coverage for a Bing Search Performance CSV export.</summary>
public sealed class BingWebmasterCsvExportOptions
{
    /// <summary>Stable provider registration identifier written to the observation batch.</summary>
    public string ProviderId { get; set; } = "bing-webmaster";

    /// <summary>Stable fleet site identifier written to the observation batch.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Owning fleet site boundary used to validate exported page rows.</summary>
    public string SiteBaseUrl { get; set; } = string.Empty;

    /// <summary>Optional verified Bing property identity used to authorize query-only rows.</summary>
    public string? PropertySiteUrl { get; set; }

    /// <summary>Inclusive first reporting date represented by the export.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Inclusive last reporting date represented by the export.</summary>
    public DateOnly ThroughDate { get; set; }

    /// <summary>Search surface attached to normalized observations.</summary>
    public string SearchType { get; set; } = "web";

    /// <summary>Time at which the export was obtained.</summary>
    public DateTimeOffset CollectedAtUtc { get; set; }

    /// <summary>Optional deterministic provider configuration identity.</summary>
    public string? ConfigurationHash { get; set; }

    /// <summary>Optional non-secret reference to the retained export.</summary>
    public string? EvidenceReference { get; set; }
}

/// <summary>Converts dated Bing Search Performance CSV exports into provider-neutral observations.</summary>
public static class BingWebmasterCsvExportParser
{
    /// <summary>Fleet provider kind used for export-only registrations.</summary>
    public const string ProviderKind = "bing-webmaster-export";

    /// <summary>Capabilities implemented by the export parser.</summary>
    public static readonly IReadOnlySet<string> AvailableCapabilities =
        new HashSet<string>([WebSearchProviderCapabilities.SearchAnalytics], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DateHeaders = ["date", "day"];
    private static readonly string[] PageHeaders = ["page", "url", "top page", "served page"];
    private static readonly string[] QueryHeaders = ["query", "keyword", "keywords", "search query"];
    private static readonly string[] ClickHeaders = ["clicks", "click"];
    private static readonly string[] ImpressionHeaders = ["impressions", "impression"];
    private static readonly string[] PositionHeaders = ["average position", "avg position", "position"];
    private static readonly string[] ClickThroughRateHeaders = ["average ctr", "avg ctr", "ctr", "click through rate"];

    /// <summary>Parses an exported CSV document without inventing dates or dimensions absent from the file.</summary>
    public static WebSearchObservationBatch Parse(string csv, BingWebmasterCsvExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ValidateOptions(options);

        var rows = ParseRows(csv);
        if (rows.Count == 0)
            throw new FormatException("Bing Webmaster CSV export is empty.");
        var header = rows[0].Select(NormalizeHeader).ToArray();
        if (header.Any(string.IsNullOrEmpty))
            throw new FormatException("Bing Webmaster CSV contains a blank header.");
        var duplicateHeader = header.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicateHeader is not null)
            throw new FormatException($"Bing Webmaster CSV contains duplicate header '{duplicateHeader.Key}'.");

        var dateIndex = FindRequiredHeader(header, DateHeaders, "date");
        var clickIndex = FindRequiredHeader(header, ClickHeaders, "clicks");
        var impressionIndex = FindRequiredHeader(header, ImpressionHeaders, "impressions");
        var pageIndex = FindOptionalHeader(header, PageHeaders);
        var queryIndex = FindOptionalHeader(header, QueryHeaders);
        if (pageIndex < 0 && queryIndex < 0)
        {
            throw new FormatException(
                "Bing Webmaster CSV must contain a page or query column; aggregate-only exports cannot be represented as daily search observations.");
        }
        var positionIndex = FindOptionalHeader(header, PositionHeaders);
        var clickThroughRateIndex = FindOptionalHeader(header, ClickThroughRateHeaders);

        var observations = new List<WebSearchObservation>();
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;
            if (row.Length != header.Length)
                throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} has {row.Length} fields; expected {header.Length}.");

            var date = ParseDate(row[dateIndex], rowIndex);
            if (date < options.FromDate || date > options.ThroughDate)
                throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} date is outside the declared export range.");
            var page = GetOptional(row, pageIndex);
            var query = GetOptional(row, queryIndex);
            if (page is null && query is null)
                throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} requires a page or query value.");
            if (page is not null && !BingWebmasterCollector.PageBelongsToSite(page, options.SiteBaseUrl))
                throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} page is outside the owning fleet site boundary.");
            if (page is null && !PropertyMatchesSite(options.PropertySiteUrl, options.SiteBaseUrl))
            {
                throw new FormatException(
                    $"Bing Webmaster CSV row {rowIndex + 1} is query-only and cannot prove that it belongs to the owning fleet site.");
            }

            var clicks = ParseCount(row[clickIndex], rowIndex, "clicks");
            var impressions = ParseCount(row[impressionIndex], rowIndex, "impressions");
            if (clicks > impressions)
                throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} has more clicks than impressions.");
            var clickThroughRate = ParseOptionalRate(GetOptional(row, clickThroughRateIndex), rowIndex);
            if (impressions == 0 && clickThroughRate is not null and not 0d)
                throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} has nonzero CTR with zero impressions.");
            var parsedPosition = ParseOptionalNonNegativeDouble(GetOptional(row, positionIndex), rowIndex, "average position");

            observations.Add(new WebSearchObservation
            {
                Date = date,
                Page = page,
                Query = query,
                SearchType = options.SearchType,
                Clicks = clicks,
                Impressions = impressions,
                ClickThroughRate = clickThroughRate,
                AveragePosition = impressions == 0 ? null : parsedPosition,
                EvidenceReference = options.EvidenceReference
            });
        }

        if (observations.Count == 0)
        {
            throw new FormatException(
                "Bing Webmaster CSV contains no data rows; an empty export cannot prove zero provider data for caller-supplied dates.");
        }
        var batch = new WebSearchObservationBatch
        {
            SchemaVersion = WebSearchObservationBatch.CurrentSchemaVersion,
            Provider = options.ProviderId,
            SiteId = options.SiteId,
            CollectedAtUtc = options.CollectedAtUtc,
            SourceKind = "csv-import",
            Status = "complete",
            ConfigurationHash = options.ConfigurationHash,
            EvidenceReference = options.EvidenceReference,
            ZeroDataConfirmed = false,
            CollectionCoverage = new WebSearchObservationCollectionCoverage
            {
                Mode = "snapshot",
                FromDate = options.FromDate,
                ThroughDate = options.ThroughDate,
                SearchType = options.SearchType,
                CompletedDates = observations.Select(observation => observation.Date)
                    .Distinct()
                    .OrderBy(date => date)
                    .ToArray()
            },
            Observations = observations.ToArray()
        };
        return WebSearchObservationNormalizer.Normalize(batch);
    }

    private static IReadOnlyList<string[]> ParseRows(string csv)
    {
        if (csv.Length > 0 && csv[0] == '\uFEFF')
            csv = csv[1..];
        var delimiter = DetectDelimiter(csv);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var quoteClosed = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        quoteClosed = true;
                    }
                }
                else
                {
                    field.Append(character);
                }
                continue;
            }

            if (quoteClosed && character != delimiter && character is not '\r' and not '\n')
                throw new FormatException("Bing Webmaster CSV contains characters after a closing quote.");
            if (character == '"')
            {
                if (field.Length != 0)
                    throw new FormatException("Bing Webmaster CSV contains an unexpected quote.");
                quoted = true;
                continue;
            }
            if (character == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                quoteClosed = false;
                continue;
            }
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                    index++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
                quoteClosed = false;
                continue;
            }
            field.Append(character);
        }

        if (quoted)
            throw new FormatException("Bing Webmaster CSV contains an unterminated quoted field.");
        if (field.Length > 0 || row.Count > 0 || quoteClosed)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }

    private static char DetectDelimiter(string csv)
    {
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var quoted = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (character == '"')
            {
                if (quoted && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    index++;
                    continue;
                }
                quoted = !quoted;
                continue;
            }
            if (!quoted && character is '\r' or '\n')
                break;
            if (!quoted && counts.ContainsKey(character))
                counts[character]++;
        }
        var selected = counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key == ',' ? 0 : 1).First();
        if (selected.Value == 0)
            throw new FormatException("Bing Webmaster CSV header does not contain a supported delimiter.");
        return selected.Key;
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal);
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized;
    }

    private static int FindRequiredHeader(string[] header, string[] aliases, string label)
    {
        var index = FindOptionalHeader(header, aliases);
        return index >= 0 ? index : throw new FormatException($"Bing Webmaster CSV requires a {label} column.");
    }

    private static int FindOptionalHeader(string[] header, string[] aliases)
    {
        var indexes = header
            .Select((value, index) => (value, index))
            .Where(item => aliases.Contains(item.value, StringComparer.Ordinal))
            .Select(item => item.index)
            .ToArray();
        if (indexes.Length > 1)
            throw new FormatException("Bing Webmaster CSV contains multiple columns for the same semantic field.");
        return indexes.Length == 0 ? -1 : indexes[0];
    }

    private static DateOnly ParseDate(string value, int rowIndex)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy" };
        if (!DateOnly.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} has an invalid date.");
        return date;
    }

    private static long ParseCount(string value, int rowIndex, string label)
    {
        var normalized = value.Trim();
        if (!HasValidInvariantThousandsGrouping(normalized) ||
            !long.TryParse(normalized, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < 0)
            throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} has invalid {label}.");
        return parsed;
    }

    private static bool HasValidInvariantThousandsGrouping(string value)
    {
        var digits = value;
        if (digits.Length > 0 && digits[0] is '+' or '-')
            digits = digits[1..];
        if (digits.Length == 0)
            return false;
        var groups = digits.Split(',');
        if (groups.Any(group => group.Length == 0 || group.Any(character => character is < '0' or > '9')))
            return false;
        return groups.Length == 1 ||
               (groups[0].Length is >= 1 and <= 3 && groups.Skip(1).All(group => group.Length == 3));
    }

    private static double? ParseOptionalRate(string? value, int rowIndex)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        var percentage = trimmed.EndsWith('%');
        if (percentage)
            trimmed = trimmed[..^1].Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
            throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} has invalid CTR.");
        var rate = percentage ? parsed / 100d : parsed;
        if (rate is < 0d or > 1d)
            throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} has CTR outside zero to one.");
        return rate;
    }

    private static double? ParseOptionalNonNegativeDouble(string? value, int rowIndex, string label)
    {
        if (value is null)
            return null;
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed) || parsed < 0d)
        {
            throw new FormatException($"Bing Webmaster CSV row {rowIndex + 1} has invalid {label}.");
        }
        return parsed;
    }

    private static string? GetOptional(string[] row, int index) =>
        index < 0 || string.IsNullOrWhiteSpace(row[index]) ? null : row[index].Trim();

    private static void ValidateOptions(BingWebmasterCsvExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProviderId) || string.IsNullOrWhiteSpace(options.SiteId))
            throw new ArgumentException("Bing Webmaster CSV import requires provider and site identifiers.", nameof(options));
        if (!BingWebmasterCollector.TryNormalizeSiteUrl(options.SiteBaseUrl, out _))
            throw new ArgumentException("Bing Webmaster CSV import requires a valid owning site base URL.", nameof(options));
        if (options.FromDate == default || options.ThroughDate == default || options.FromDate > options.ThroughDate)
            throw new ArgumentException("Bing Webmaster CSV import date range is invalid.", nameof(options));
        if (!string.Equals(options.SearchType, "web", StringComparison.Ordinal))
            throw new ArgumentException("Bing Webmaster CSV import supports only the 'web' search type.", nameof(options));
        if (options.CollectedAtUtc == default)
            throw new ArgumentException("Bing Webmaster CSV import requires an offset-aware collection time.", nameof(options));
    }

    private static bool PropertyMatchesSite(string? propertySiteUrl, string siteBaseUrl) =>
        BingWebmasterCollector.TryNormalizeSiteUrl(propertySiteUrl, out var normalizedProperty) &&
        BingWebmasterCollector.TryNormalizeSiteUrl(siteBaseUrl, out var normalizedSite) &&
        string.Equals(normalizedProperty, normalizedSite, StringComparison.Ordinal);
}
