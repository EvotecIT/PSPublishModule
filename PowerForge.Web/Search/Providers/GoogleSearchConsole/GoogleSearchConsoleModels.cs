using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Options for collecting finalized Search Analytics observations from one Google Search Console property.</summary>
public sealed class GoogleSearchConsoleCollectionOptions
{
    /// <summary>Stable provider registration identifier written to the observation batch.</summary>
    public string ProviderId { get; set; } = "google-search-console";

    /// <summary>Stable fleet site identifier written to the observation batch.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Canonical fleet site boundary used to reject analytics rows owned by another site.</summary>
    public string SiteBaseUrl { get; set; } = string.Empty;

    /// <summary>Search Console property, such as <c>sc-domain:example.com</c> or an HTTP(S) URL-prefix property.</summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>Inclusive first reporting date.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Inclusive last reporting date.</summary>
    public DateOnly ThroughDate { get; set; }

    /// <summary>Google search surface. The default is <c>web</c>.</summary>
    public string SearchType { get; set; } = "web";

    /// <summary>Maximum rows requested per API page. Google currently accepts 1 through 25,000.</summary>
    public int RowLimit { get; set; } = GoogleSearchConsoleCollector.MaximumRowLimit;

    /// <summary>Optional deterministic provider configuration identity.</summary>
    public string? ConfigurationHash { get; set; }

    /// <summary>Optional non-secret reference to separately retained raw evidence.</summary>
    public string? EvidenceReference { get; set; }
}

/// <summary>Authenticated access state for a configured Google Search Console property.</summary>
public sealed class GoogleSearchConsolePropertyProbeResult
{
    /// <summary>Whether the credential can see the configured property.</summary>
    public bool Success { get; set; }

    /// <summary>Configured Search Console property.</summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>Permission level returned by Search Console when the property is visible.</summary>
    public string? PermissionLevel { get; set; }

    /// <summary>Capabilities implemented by this collector.</summary>
    public string[] AvailableCapabilities { get; set; } = Array.Empty<string>();

    /// <summary>Stable failure category when the probe does not succeed.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Credential-safe failure summary when the probe does not succeed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Result of one Google Search Console collection run.</summary>
public sealed class GoogleSearchConsoleCollectionResult
{
    /// <summary>Whether property probing and every requested Search Analytics page succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Authenticated property probe.</summary>
    public GoogleSearchConsolePropertyProbeResult Probe { get; set; } = new();

    /// <summary>Complete or partial provider-neutral observation batch.</summary>
    public WebSearchObservationBatch Batch { get; set; } = new();

    /// <summary>Number of reporting dates whose paging completed.</summary>
    public int CompletedDateCount { get; set; }

    /// <summary>Number of Search Analytics page requests attempted.</summary>
    public int RequestCount { get; set; }

    /// <summary>Stable failure category for a partial run.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Credential-safe failure summary for a partial run.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Provides short-lived OAuth access tokens to the Google Search Console collector.</summary>
public interface IGoogleSearchConsoleAccessTokenProvider
{
    /// <summary>Gets a bearer token for a Google API request.</summary>
    /// <param name="requestUri">Google API request URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Non-empty OAuth bearer token.</returns>
    Task<string> GetAccessTokenAsync(Uri requestUri, CancellationToken cancellationToken = default);
}

internal sealed class GoogleSearchConsoleSitesResponse
{
    [JsonPropertyName("siteEntry")]
    public GoogleSearchConsoleSiteEntry[] SiteEntries { get; set; } = Array.Empty<GoogleSearchConsoleSiteEntry>();
}

internal sealed class GoogleSearchConsoleSiteEntry
{
    [JsonPropertyName("siteUrl")]
    public string SiteUrl { get; set; } = string.Empty;

    [JsonPropertyName("permissionLevel")]
    public string PermissionLevel { get; set; } = string.Empty;
}

internal sealed class GoogleSearchConsoleQueryRequest
{
    [JsonPropertyName("startDate")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("endDate")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("dimensions")]
    public string[] Dimensions { get; set; } = Array.Empty<string>();

    [JsonPropertyName("type")]
    public string Type { get; set; } = "web";

    [JsonPropertyName("dataState")]
    public string DataState { get; set; } = "final";

    [JsonPropertyName("rowLimit")]
    public int RowLimit { get; set; }

    [JsonPropertyName("startRow")]
    public int StartRow { get; set; }

    [JsonPropertyName("dimensionFilterGroups")]
    public GoogleSearchConsoleDimensionFilterGroup[] DimensionFilterGroups { get; set; } = Array.Empty<GoogleSearchConsoleDimensionFilterGroup>();
}

internal sealed class GoogleSearchConsoleDimensionFilterGroup
{
    [JsonPropertyName("groupType")]
    public string GroupType { get; set; } = "and";

    [JsonPropertyName("filters")]
    public GoogleSearchConsoleDimensionFilter[] Filters { get; set; } = Array.Empty<GoogleSearchConsoleDimensionFilter>();
}

internal sealed class GoogleSearchConsoleDimensionFilter
{
    [JsonPropertyName("dimension")]
    public string Dimension { get; set; } = "page";

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "includingRegex";

    [JsonPropertyName("expression")]
    public string Expression { get; set; } = string.Empty;
}

internal sealed class GoogleSearchConsoleQueryResponse
{
    [JsonPropertyName("rows")]
    public GoogleSearchConsoleQueryRow[] Rows { get; set; } = Array.Empty<GoogleSearchConsoleQueryRow>();

    [JsonPropertyName("metadata")]
    public GoogleSearchConsoleQueryMetadata? Metadata { get; set; }
}

internal sealed class GoogleSearchConsoleQueryMetadata
{
    [JsonPropertyName("first_incomplete_date")]
    public string? FirstIncompleteDate { get; set; }
}

internal sealed class GoogleSearchConsoleQueryRow
{
    [JsonPropertyName("keys")]
    public string[] Keys { get; set; } = Array.Empty<string>();

    [JsonPropertyName("clicks")]
    public double? Clicks { get; set; }

    [JsonPropertyName("impressions")]
    public double? Impressions { get; set; }

    [JsonPropertyName("ctr")]
    public double? ClickThroughRate { get; set; }

    [JsonPropertyName("position")]
    public double? Position { get; set; }
}

internal sealed class GoogleSearchConsoleErrorResponse
{
    [JsonPropertyName("error")]
    public GoogleSearchConsoleErrorBody? Error { get; set; }
}

internal sealed class GoogleSearchConsoleErrorBody
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
