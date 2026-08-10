using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Options for importing one standard Lighthouse JSON report as laboratory evidence.</summary>
public sealed class LighthouseReportImportOptions
{
    /// <summary>Provider registration identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Configured fleet site base URL used for ownership validation.</summary>
    public string SiteBaseUrl { get; set; } = string.Empty;
    /// <summary>Optional non-secret configuration fingerprint.</summary>
    public string? ConfigurationHash { get; set; }
    /// <summary>Optional reference to the retained raw Lighthouse report.</summary>
    public string? EvidenceReference { get; set; }
}

/// <summary>Imports the stable performance fields of a Lighthouse JSON report without embedding a browser runner.</summary>
public static class LighthouseReportImporter
{
    /// <summary>Provider kind registered in the fleet capability catalog.</summary>
    public const string ProviderKind = "lighthouse";

    /// <summary>Imports and normalizes a Lighthouse JSON report.</summary>
    public static WebPerformanceObservationBatch Import(string json, LighthouseReportImportOptions options)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Lighthouse report JSON is required.", nameof(json));
        ArgumentNullException.ThrowIfNull(options);

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 128
        });
        var root = document.RootElement;
        WebPerformanceJsonBoundary.ValidateNoDuplicateObjectMembers(root, "Lighthouse report");
        var finalUrl = RequiredString(root, "finalUrl");
        if (!WebPerformanceObservationNormalizer.TargetBelongsToSite(finalUrl, options.SiteBaseUrl))
            throw new ArgumentException("Lighthouse finalUrl does not belong to the configured fleet site.", nameof(json));
        var fetchTimeValue = RequiredString(root, "fetchTime");
        if (!HasExplicitOffset(fetchTimeValue) || !DateTimeOffset.TryParse(fetchTimeValue, null, System.Globalization.DateTimeStyles.RoundtripKind, out var fetchTime))
            throw new ArgumentException("Lighthouse fetchTime must be ISO-8601 with an explicit UTC offset.", nameof(json));
        var version = RequiredString(root, "lighthouseVersion");
        var formFactor = root.TryGetProperty("configSettings", out var settings) &&
                         settings.TryGetProperty("formFactor", out var formFactorElement) &&
                         formFactorElement.ValueKind == JsonValueKind.String
            ? formFactorElement.GetString()?.Trim().ToLowerInvariant()
            : null;
        formFactor = formFactor switch
        {
            "mobile" => "phone",
            "desktop" => "desktop",
            _ => throw new ArgumentException("Lighthouse report requires configSettings.formFactor of mobile or desktop.", nameof(json))
        };

        var categories = RequiredObject(root, "categories");
        var performance = RequiredObject(categories, "performance");
        var score = RequiredFiniteNumber(performance, "score");
        var audits = RequiredObject(root, "audits");
        var observations = new List<WebPerformanceObservation>
        {
            Metric("performance-score", score, "score"),
            AuditMetric(audits, "first-contentful-paint", "milliseconds"),
            AuditMetric(audits, "largest-contentful-paint", "milliseconds"),
            AuditMetric(audits, "cumulative-layout-shift", "unitless"),
            AuditMetric(audits, "total-blocking-time", "milliseconds"),
            AuditMetric(audits, "speed-index", "milliseconds")
        };

        return WebPerformanceObservationNormalizer.Normalize(new WebPerformanceObservationBatch
        {
            Provider = options.ProviderId,
            SiteId = options.SiteId,
            CollectedAtUtc = fetchTime,
            SourceKind = "lighthouse-json",
            Status = "complete",
            MeasurementKind = "lab",
            TargetKind = "url",
            TargetUrl = finalUrl,
            FormFactor = formFactor,
            ToolVersion = version,
            ConfigurationHash = options.ConfigurationHash,
            EvidenceReference = options.EvidenceReference,
            Observations = observations.ToArray()
        });
    }

    private static WebPerformanceObservation AuditMetric(JsonElement audits, string name, string unit)
    {
        var audit = RequiredObject(audits, name);
        return Metric(name, RequiredFiniteNumber(audit, "numericValue"), unit);
    }

    private static WebPerformanceObservation Metric(string name, double value, string unit) => new()
    {
        Metric = name,
        Value = value,
        Unit = unit
    };

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Lighthouse report requires object '{name}'.", nameof(parent));
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"Lighthouse report requires string '{name}'.", nameof(parent));
        return value.GetString()!;
    }

    private static double RequiredFiniteNumber(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new ArgumentException($"Lighthouse report requires finite numeric '{name}'.", nameof(parent));
        return number;
    }

    private static bool HasExplicitOffset(string value) =>
        value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
        value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':';
}
