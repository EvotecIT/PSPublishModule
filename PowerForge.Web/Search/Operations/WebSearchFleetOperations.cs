using System.Globalization;

namespace PowerForge.Web;

/// <summary>One durable evidence stream used for fleet scheduling and reporting.</summary>
public sealed class WebSearchFleetEvidenceStream
{
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Provider registration identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>Capability represented by this stream.</summary>
    public string Capability { get; set; } = string.Empty;
    /// <summary>Capability-specific collection scope, such as search type or performance target and form factor.</summary>
    public string ScopeKey { get; set; } = string.Empty;
    /// <summary>Configuration identity that produced this evidence stream.</summary>
    public string? ConfigurationHash { get; set; }
    /// <summary>Latest reporting date proven complete for a daily stream.</summary>
    public DateOnly? LatestCompleteDate { get; set; }
    /// <summary>Non-overlapping reporting ranges already proven collected for a daily stream.</summary>
    public WebSearchFleetCompletedRange[] CompletedRanges { get; set; } = Array.Empty<WebSearchFleetCompletedRange>();
    /// <summary>Latest completed collection or import time.</summary>
    public DateTimeOffset? LastCompleteAtUtc { get; set; }
    /// <summary>Latest attempted collection or import time, including partial runs.</summary>
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    /// <summary>Whether the newest available attempt is partial.</summary>
    public bool HasPartialEvidence { get; set; }
    /// <summary>Failure category recorded by the newest partial daily attempt.</summary>
    public string? LatestFailureCategory { get; set; }
    /// <summary>Reporting partition that failed in the newest partial daily attempt.</summary>
    public DateOnly? LatestFailureDate { get; set; }
    /// <summary>Latest durable permanent failure for every affected daily partition.</summary>
    public WebSearchFleetFailurePartition[] PermanentFailures { get; set; } = Array.Empty<WebSearchFleetFailurePartition>();
    /// <summary>Whether completed coverage survives only as a compact retention summary.</summary>
    public bool HasRetainedCoverage { get; set; }
    /// <summary>Number of stored runs contributing to this stream.</summary>
    public int RunCount { get; set; }
}

/// <summary>Inclusive completed reporting-date range.</summary>
public sealed class WebSearchFleetCompletedRange
{
    /// <summary>Inclusive first completed date.</summary>
    public DateOnly FromDate { get; set; }
    /// <summary>Inclusive last completed date.</summary>
    public DateOnly ThroughDate { get; set; }
}

/// <summary>One daily partition that cannot be retried without operator input.</summary>
public sealed class WebSearchFleetFailurePartition
{
    /// <summary>Reporting date rejected by a permanent provider boundary.</summary>
    public DateOnly Date { get; set; }
    /// <summary>Stable non-secret failure category.</summary>
    public string Category { get; set; } = string.Empty;
}

/// <summary>Read-only inventory of durable evidence used by fleet operations.</summary>
public sealed class WebSearchFleetEvidenceSnapshot
{
    /// <summary>Whether the durable store exists.</summary>
    public bool StoreExists { get; set; }
    /// <summary>Database schema version, or zero when the store does not exist.</summary>
    public int DatabaseSchemaVersion { get; set; }
    /// <summary>Evidence streams discovered in the store.</summary>
    public WebSearchFleetEvidenceStream[] Streams { get; set; } = Array.Empty<WebSearchFleetEvidenceStream>();
}

/// <summary>One executable or input-dependent collection unit emitted by the fleet scheduler.</summary>
public sealed class WebSearchFleetWorkItem
{
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Provider registration identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>Provider kind.</summary>
    public string ProviderKind { get; set; } = string.Empty;
    /// <summary>Capability being refreshed.</summary>
    public string Capability { get; set; } = string.Empty;
    /// <summary>Stable action understood by an external runner.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary><c>ready</c> for API work or <c>input-required</c> for export/report imports.</summary>
    public string Readiness { get; set; } = string.Empty;
    /// <summary>Inclusive first reporting date for a daily collection chunk.</summary>
    public DateOnly? FromDate { get; set; }
    /// <summary>Inclusive last reporting date for a daily collection chunk.</summary>
    public DateOnly? ThroughDate { get; set; }
    /// <summary>Time at which periodic evidence became due.</summary>
    public DateTimeOffset? DueAtUtc { get; set; }
    /// <summary>Whether another bounded backfill chunk remains after this work item.</summary>
    public bool HasMoreBackfill { get; set; }
    /// <summary>Permanent provider boundary that requires operator input before retrying.</summary>
    public string? FailureCategory { get; set; }
}

/// <summary>Deterministic collection work due for a fleet at one point in time.</summary>
public sealed class WebSearchFleetSchedulePlan
{
    /// <summary>UTC time used to evaluate due work.</summary>
    public DateTimeOffset AsOfUtc { get; set; }
    /// <summary>Deterministic identity of the effective operations policy.</summary>
    public string OperationsHash { get; set; } = string.Empty;
    /// <summary>Whether the evidence store exists.</summary>
    public bool StoreExists { get; set; }
    /// <summary>Whether provider configuration passed capability checks for this scheduler host.</summary>
    public bool ConfigurationValid { get; set; }
    /// <summary>Due work ordered by site, provider, capability, and reporting range.</summary>
    public WebSearchFleetWorkItem[] WorkItems { get; set; } = Array.Empty<WebSearchFleetWorkItem>();
}

/// <summary>Fleet-wide readiness and evidence state for one configured capability.</summary>
public sealed class WebSearchFleetReportRow
{
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Provider registration identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>Provider kind.</summary>
    public string ProviderKind { get; set; } = string.Empty;
    /// <summary>Capability represented by this row.</summary>
    public string Capability { get; set; } = string.Empty;
    /// <summary>Whether collection is enabled.</summary>
    public bool Enabled { get; set; }
    /// <summary>Whether provider configuration passed semantic validation.</summary>
    public bool ConfigurationReady { get; set; }
    /// <summary>Whether the current executable implements the capability.</summary>
    public bool CollectorAvailable { get; set; }
    /// <summary>Disabled, configuration-error, collector-unavailable, missing, partial, due, or current.</summary>
    public string EvidenceState { get; set; } = string.Empty;
    /// <summary>Latest reporting date proven complete for daily evidence.</summary>
    public DateOnly? LatestCompleteDate { get; set; }
    /// <summary>Latest completed collection time.</summary>
    public DateTimeOffset? LastCompleteAtUtc { get; set; }
    /// <summary>Latest attempted collection time.</summary>
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
}

/// <summary>Fleet-wide provider readiness and evidence report.</summary>
public sealed class WebSearchFleetReport
{
    /// <summary>UTC time used to evaluate freshness.</summary>
    public DateTimeOffset AsOfUtc { get; set; }
    /// <summary>Whether provider configuration passed the capability doctor.</summary>
    public bool ConfigurationValid { get; set; }
    /// <summary>Whether the durable evidence store exists.</summary>
    public bool StoreExists { get; set; }
    /// <summary>Number of configured sites.</summary>
    public int SiteCount { get; set; }
    /// <summary>Number of configured providers.</summary>
    public int ProviderCount { get; set; }
    /// <summary>Whether at least one enabled capability needs attention.</summary>
    public bool NeedsAttention { get; set; }
    /// <summary>Capability rows ordered by fleet identity.</summary>
    public WebSearchFleetReportRow[] Rows { get; set; } = Array.Empty<WebSearchFleetReportRow>();
}

/// <summary>Retention outcome for one durable evidence kind.</summary>
public sealed class WebSearchFleetRetentionKindResult
{
    /// <summary>Search, traffic, or performance.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Exclusive collection-time cutoff.</summary>
    public DateTimeOffset CutoffUtc { get; set; }
    /// <summary>Runs eligible for deletion after preserving each stream's best latest evidence.</summary>
    public int CandidateRunCount { get; set; }
    /// <summary>Observation rows belonging to candidate runs.</summary>
    public int CandidateObservationCount { get; set; }
    /// <summary>Runs actually deleted.</summary>
    public int DeletedRunCount { get; set; }
    /// <summary>Observation rows actually deleted.</summary>
    public int DeletedObservationCount { get; set; }
}

/// <summary>Dry-run or applied fleet retention result.</summary>
public sealed class WebSearchFleetRetentionResult
{
    /// <summary>Whether the durable store exists.</summary>
    public bool StoreExists { get; set; }
    /// <summary>Whether deletion was requested.</summary>
    public bool Applied { get; set; }
    /// <summary>Per-kind retention outcomes.</summary>
    public WebSearchFleetRetentionKindResult[] Kinds { get; set; } = Array.Empty<WebSearchFleetRetentionKindResult>();
}

/// <summary>Builds deterministic schedule and fleet-report contracts without contacting providers.</summary>
public static class WebSearchFleetPlanner
{
    private const string PlanningCredentialPlaceholder = "credential-not-required-by-fleet-planning";
    private static readonly IReadOnlySet<string> PermanentDailyFailureCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        "retention-boundary", "duration-boundary", "row-limit-reached"
    };

    /// <summary>Creates due collection work from configuration and durable evidence.</summary>
    public static WebSearchFleetSchedulePlan CreateSchedule(
        WebSearchProviderConfiguration configuration,
        WebSearchProviderDoctorResult doctor,
        WebSearchFleetEvidenceSnapshot snapshot,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(doctor);
        ArgumentNullException.ThrowIfNull(snapshot);
        var policy = configuration.Operations ?? new WebSearchFleetOperationsConfiguration();
        ValidatePolicy(policy);
        var asOf = asOfUtc.ToUniversalTime();
        var work = new List<WebSearchFleetWorkItem>();

        foreach (var site in (configuration.Sites ?? Array.Empty<WebSearchSiteProviderConfiguration>())
                     .Where(value => value is not null)
                     .OrderBy(value => value.Id, StringComparer.Ordinal))
        foreach (var provider in (site.Providers ?? Array.Empty<WebSearchProviderRegistration>())
                     .Where(value => value?.Enabled == true)
                     .OrderBy(value => value.Id, StringComparer.Ordinal))
        foreach (var capability in (provider.Capabilities ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            var providerState = doctor.Providers.FirstOrDefault(value =>
                value.SiteId.Equals(site.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                value.ProviderId.Equals(provider.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            var actionDoctor = InspectProviderAction(configuration, site, provider);
            var actionReady = providerState?.ConfigurationReady == true && actionDoctor.Success;
            var readiness = !actionReady
                ? "configuration-error"
                : providerState?.AvailableCollectorCapabilities.Contains(capability, StringComparer.OrdinalIgnoreCase) == true
                    ? null
                    : "collector-unavailable";
            var evidence = actionReady
                ? FindStream(snapshot, site, provider, capability, actionDoctor.ConfigurationHash)
                : null;
            if (capability == WebSearchProviderCapabilities.SearchAnalytics)
            {
                AddDailyWork(work, site, provider, capability,
                    string.Equals(provider.Kind, "bing-webmaster-export", StringComparison.OrdinalIgnoreCase) ? "import-bing-export" : "collect-search",
                    readiness ?? (string.Equals(provider.Kind, "bing-webmaster-export", StringComparison.OrdinalIgnoreCase) ? "input-required" : "ready"),
                    evidence, DateOnly.FromDateTime(asOf.UtcDateTime).AddDays(-policy.SearchDataLagDays), policy);
            }
            else if (capability == WebSearchProviderCapabilities.TrafficAnalytics)
            {
                AddDailyWork(work, site, provider, capability, "collect-traffic", readiness ?? "ready", evidence,
                    DateOnly.FromDateTime(asOf.UtcDateTime).AddDays(-policy.TrafficDataLagDays), policy);
            }
            else if (capability == WebSearchProviderCapabilities.PerformanceCrux)
            {
                AddPeriodicWork(work, site, provider, capability, "collect-crux", readiness ?? "ready", evidence,
                    TimeSpan.FromDays(policy.CruxIntervalDays), asOf);
            }
            else if (capability == WebSearchProviderCapabilities.PerformanceLighthouse)
            {
                AddPeriodicWork(work, site, provider, capability, "import-lighthouse", readiness ?? "input-required", evidence,
                    TimeSpan.FromDays(policy.LighthouseIntervalDays), asOf);
            }
            else
            {
                work.Add(new WebSearchFleetWorkItem
                {
                    SiteId = site.Id ?? string.Empty,
                    ProviderId = provider.Id ?? string.Empty,
                    ProviderKind = provider.Kind ?? string.Empty,
                    Capability = capability,
                    Action = "unsupported-capability",
                    Readiness = readiness ?? "collector-unavailable"
                });
            }
        }

        return new WebSearchFleetSchedulePlan
        {
            AsOfUtc = asOf,
            OperationsHash = OperationsHash(policy),
            StoreExists = snapshot.StoreExists,
            ConfigurationValid = doctor.Success,
            WorkItems = work
                .OrderBy(value => value.SiteId, StringComparer.Ordinal)
                .ThenBy(value => value.ProviderId, StringComparer.Ordinal)
                .ThenBy(value => value.Capability, StringComparer.Ordinal)
                .ThenBy(value => value.FromDate)
                .ToArray()
        };
    }

    /// <summary>Combines provider-doctor state with durable evidence freshness.</summary>
    public static WebSearchFleetReport CreateReport(
        WebSearchProviderConfiguration configuration,
        WebSearchProviderDoctorResult doctor,
        WebSearchFleetEvidenceSnapshot snapshot,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(doctor);
        ArgumentNullException.ThrowIfNull(snapshot);
        var schedule = CreateSchedule(configuration, doctor, snapshot, asOfUtc);
        var rows = new List<WebSearchFleetReportRow>();
        foreach (var site in (configuration.Sites ?? Array.Empty<WebSearchSiteProviderConfiguration>())
                     .Where(value => value is not null)
                     .OrderBy(value => value.Id, StringComparer.Ordinal))
        foreach (var provider in (site.Providers ?? Array.Empty<WebSearchProviderRegistration>())
                     .Where(value => value is not null)
                     .OrderBy(value => value.Id, StringComparer.Ordinal))
        foreach (var capability in (provider.Capabilities ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            var providerState = doctor.Providers.FirstOrDefault(value =>
                value.SiteId.Equals(site.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                value.ProviderId.Equals(provider.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            var actionDoctor = InspectProviderAction(configuration, site, provider);
            var actionReady = providerState?.ConfigurationReady == true && actionDoctor.Success;
            var capabilityAvailable = providerState?.AvailableCollectorCapabilities.Contains(capability, StringComparer.OrdinalIgnoreCase) == true;
            var evidence = actionReady
                ? FindStream(snapshot, site, provider, capability, actionDoctor.ConfigurationHash)
                : null;
            var due = schedule.WorkItems.Any(value =>
                value.SiteId.Equals(site.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                value.ProviderId.Equals(provider.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                value.Capability.Equals(capability, StringComparison.OrdinalIgnoreCase));
            var state = !provider.Enabled ? "disabled" :
                !actionReady ? "configuration-error" :
                !capabilityAvailable ? "collector-unavailable" :
                evidence is null ? "missing" :
                evidence.HasPartialEvidence ? "partial" :
                evidence.LastCompleteAtUtc is null && !evidence.HasRetainedCoverage ? "missing" :
                due ? "due" : "current";
            rows.Add(new WebSearchFleetReportRow
            {
                SiteId = site.Id ?? string.Empty,
                ProviderId = provider.Id ?? string.Empty,
                ProviderKind = provider.Kind ?? string.Empty,
                Capability = capability,
                Enabled = provider.Enabled,
                ConfigurationReady = actionReady,
                CollectorAvailable = capabilityAvailable,
                EvidenceState = state,
                LatestCompleteDate = evidence?.LatestCompleteDate,
                LastCompleteAtUtc = evidence?.LastCompleteAtUtc,
                LastAttemptAtUtc = evidence?.LastAttemptAtUtc
            });
        }

        return new WebSearchFleetReport
        {
            AsOfUtc = schedule.AsOfUtc,
            ConfigurationValid = doctor.Success,
            StoreExists = snapshot.StoreExists,
            SiteCount = (configuration.Sites ?? Array.Empty<WebSearchSiteProviderConfiguration>()).Count(value => value is not null),
            ProviderCount = (configuration.Sites ?? Array.Empty<WebSearchSiteProviderConfiguration>())
                .Where(value => value is not null)
                .Sum(value => (value.Providers ?? Array.Empty<WebSearchProviderRegistration>()).Count(provider => provider is not null)),
            NeedsAttention = rows.Any(value => value.Enabled && value.EvidenceState != "current"),
            Rows = rows.ToArray()
        };
    }

    /// <summary>Validates the effective operations policy.</summary>
    public static void ValidatePolicy(WebSearchFleetOperationsConfiguration policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        RequireRange(policy.MaxBackfillDaysPerRun, 1, 366, "maxBackfillDaysPerRun");
        RequireRange(policy.SearchDataLagDays, 0, 30, "searchDataLagDays");
        RequireRange(policy.TrafficDataLagDays, 1, 30, "trafficDataLagDays");
        RequireRange(policy.CruxIntervalDays, 1, 365, "cruxIntervalDays");
        RequireRange(policy.LighthouseIntervalDays, 1, 365, "lighthouseIntervalDays");
        RequireRange(policy.SearchRunRetentionDays, 1, 3650, "searchRunRetentionDays");
        RequireRange(policy.TrafficRunRetentionDays, 1, 3650, "trafficRunRetentionDays");
        RequireRange(policy.PerformanceRunRetentionDays, 1, 3650, "performanceRunRetentionDays");
    }

    private static void AddDailyWork(
        List<WebSearchFleetWorkItem> work,
        WebSearchSiteProviderConfiguration site,
        WebSearchProviderRegistration provider,
        string capability,
        string action,
        string readiness,
        WebSearchFleetEvidenceStream? evidence,
        DateOnly targetThrough,
        WebSearchFleetOperationsConfiguration policy)
    {
        var ranges = evidence?.CompletedRanges ?? Array.Empty<WebSearchFleetCompletedRange>();
        var defaultStart = ranges.Length > 0
            ? ranges.Min(value => value.FromDate)
            : targetThrough.AddDays(1 - policy.MaxBackfillDaysPerRun);
        var requestedStart = policy.BackfillStartDate ?? defaultStart;
        var permanentFailures = (evidence?.PermanentFailures ?? Array.Empty<WebSearchFleetFailurePartition>())
            .Where(value => PermanentDailyFailureCategories.Contains(value.Category))
            .Concat(evidence?.HasPartialEvidence == true &&
                    evidence.LatestFailureDate is DateOnly latestFailureDate &&
                    PermanentDailyFailureCategories.Contains(evidence.LatestFailureCategory ?? string.Empty)
                ? [new WebSearchFleetFailurePartition { Date = latestFailureDate, Category = evidence.LatestFailureCategory! }]
                : Array.Empty<WebSearchFleetFailurePartition>())
            .Where(value => value.Date >= requestedStart && value.Date <= targetThrough)
            .GroupBy(value => value.Date)
            .Select(group => group.First())
            .OrderBy(value => value.Date)
            .ToArray();
        var schedulingRanges = ranges;
        var start = FindFirstMissing(requestedStart, targetThrough, schedulingRanges);
        while (readiness == "ready" && start is not null)
        {
            var failure = permanentFailures.FirstOrDefault(value => value.Date == start.Value);
            if (failure is null)
                break;
            work.Add(new WebSearchFleetWorkItem
            {
                SiteId = site.Id ?? string.Empty,
                ProviderId = provider.Id ?? string.Empty,
                ProviderKind = provider.Kind ?? string.Empty,
                Capability = capability,
                Action = action,
                Readiness = "input-required",
                FromDate = failure.Date,
                ThroughDate = failure.Date,
                FailureCategory = failure.Category
            });
            schedulingRanges = schedulingRanges.Append(
                    new WebSearchFleetCompletedRange { FromDate = failure.Date, ThroughDate = failure.Date })
                .OrderBy(value => value.FromDate)
                .ThenBy(value => value.ThroughDate)
                .ToArray();
            start = FindFirstMissing(failure.Date.AddDays(1), targetThrough, schedulingRanges);
        }
        if (start is null)
            return;
        var maximumDays = string.Equals(provider.Kind, GoogleSearchConsoleCollector.ProviderKind, StringComparison.OrdinalIgnoreCase)
            ? Math.Min(policy.MaxBackfillDaysPerRun, GoogleSearchConsoleCollector.MaximumCollectionDateCount)
            : policy.MaxBackfillDaysPerRun;
        var through = start.Value.AddDays(maximumDays - 1);
        if (through > targetThrough)
            through = targetThrough;
        var nextCoveredRange = schedulingRanges
            .Where(value => value.FromDate > start.Value && value.FromDate <= through)
            .Concat(permanentFailures
                .Where(value => value.Date > start.Value && value.Date <= through)
                .Select(value => new WebSearchFleetCompletedRange
                {
                    FromDate = value.Date,
                    ThroughDate = value.Date
                }))
            .OrderBy(value => value.FromDate)
            .ThenBy(value => value.ThroughDate)
            .FirstOrDefault();
        if (nextCoveredRange is not null)
            through = nextCoveredRange.FromDate.AddDays(-1);
        var failureCategory = readiness == "ready"
            ? null
            : permanentFailures.FirstOrDefault(value => value.Date == start)?.Category;
        work.Add(new WebSearchFleetWorkItem
        {
            SiteId = site.Id ?? string.Empty,
            ProviderId = provider.Id ?? string.Empty,
            ProviderKind = provider.Kind ?? string.Empty,
            Capability = capability,
            Action = action,
            Readiness = readiness,
            FromDate = start,
            ThroughDate = through,
            HasMoreBackfill = through < targetThrough && FindFirstMissing(through.AddDays(1), targetThrough, schedulingRanges).HasValue,
            FailureCategory = failureCategory
        });
    }

    private static void AddPeriodicWork(
        List<WebSearchFleetWorkItem> work,
        WebSearchSiteProviderConfiguration site,
        WebSearchProviderRegistration provider,
        string capability,
        string action,
        string readiness,
        WebSearchFleetEvidenceStream? evidence,
        TimeSpan interval,
        DateTimeOffset asOfUtc)
    {
        var dueAt = evidence?.LastCompleteAtUtc?.Add(interval) ?? asOfUtc;
        if (dueAt > asOfUtc)
            return;
        work.Add(new WebSearchFleetWorkItem
        {
            SiteId = site.Id ?? string.Empty,
            ProviderId = provider.Id ?? string.Empty,
            ProviderKind = provider.Kind ?? string.Empty,
            Capability = capability,
            Action = action,
            Readiness = readiness,
            DueAtUtc = dueAt
        });
    }

    private static WebSearchFleetEvidenceStream? FindStream(
        WebSearchFleetEvidenceSnapshot snapshot,
        WebSearchSiteProviderConfiguration site,
        WebSearchProviderRegistration provider,
        string capability,
        string? configurationHash)
    {
        var matches = snapshot.Streams.Where(value =>
            value.SiteId.Equals(site.Id, StringComparison.OrdinalIgnoreCase) &&
            value.ProviderId.Equals(provider.Id, StringComparison.OrdinalIgnoreCase) &&
            value.Capability.Equals(capability, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value.ConfigurationHash, configurationHash, StringComparison.Ordinal));
        var expectedScope = capability switch
        {
            WebSearchProviderCapabilities.SearchAnalytics => "web",
            WebSearchProviderCapabilities.TrafficAnalytics => "traffic",
            WebSearchProviderCapabilities.PerformanceCrux => string.Join("\u001f", "field", "origin",
                CanonicalizeSiteOrigin(site.BaseUrl), "all"),
            _ => null
        };
        return expectedScope is null
            ? matches.OrderByDescending(value => value.LastCompleteAtUtc).ThenByDescending(value => value.LastAttemptAtUtc).FirstOrDefault()
            : matches.FirstOrDefault(value => value.ScopeKey.Equals(expectedScope, StringComparison.Ordinal));
    }

    private static WebSearchProviderDoctorResult InspectProviderAction(
        WebSearchProviderConfiguration configuration,
        WebSearchSiteProviderConfiguration site,
        WebSearchProviderRegistration provider) =>
        WebSearchProviderDoctor.InspectProviderAction(
            configuration,
            site,
            provider,
            WebSearchCollectorCatalog.AvailableCapabilities,
            _ => PlanningCredentialPlaceholder);

    private static string CanonicalizeSiteOrigin(string siteBaseUrl)
    {
        var site = new Uri(WebPerformanceObservationNormalizer.CanonicalizeTarget(siteBaseUrl, "url"), UriKind.Absolute);
        return WebPerformanceObservationNormalizer.CanonicalizeTarget(site.GetLeftPart(UriPartial.Authority) + "/", "origin");
    }

    private static DateOnly? FindFirstMissing(
        DateOnly start,
        DateOnly through,
        IEnumerable<WebSearchFleetCompletedRange> ranges)
    {
        if (start > through)
            return null;
        var candidate = start;
        foreach (var range in ranges.OrderBy(value => value.FromDate).ThenBy(value => value.ThroughDate))
        {
            if (range.ThroughDate < candidate)
                continue;
            if (range.FromDate > candidate)
                return candidate;
            candidate = range.ThroughDate.AddDays(1);
            if (candidate > through)
                return null;
        }
        return candidate <= through ? candidate : null;
    }

    private static string OperationsHash(WebSearchFleetOperationsConfiguration policy) => "sha256:" + WebSearchIdentityHasher.Compute(
        policy.BackfillStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        policy.MaxBackfillDaysPerRun.ToString(CultureInfo.InvariantCulture),
        policy.SearchDataLagDays.ToString(CultureInfo.InvariantCulture),
        policy.TrafficDataLagDays.ToString(CultureInfo.InvariantCulture),
        policy.CruxIntervalDays.ToString(CultureInfo.InvariantCulture),
        policy.LighthouseIntervalDays.ToString(CultureInfo.InvariantCulture),
        policy.SearchRunRetentionDays.ToString(CultureInfo.InvariantCulture),
        policy.TrafficRunRetentionDays.ToString(CultureInfo.InvariantCulture),
        policy.PerformanceRunRetentionDays.ToString(CultureInfo.InvariantCulture));

    private static void RequireRange(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, value, $"Fleet operations {name} must be between {minimum} and {maximum}.");
    }
}
