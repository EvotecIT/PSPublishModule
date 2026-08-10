using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebSearchFleetOperationsTests
{
    [Fact]
    public void Planner_EmitsNonReadyWorkForRecognizedCapabilitiesWithoutAScheduler()
    {
        var configuration = CreateConfiguration();
        var provider = configuration.Sites[0].Providers.Single(value => value.Id == "gsc");
        configuration.Sites[0].Providers = [provider];
        provider.Capabilities = [WebSearchProviderCapabilities.SearchSitemaps];
        var doctor = Doctor(configuration);

        var item = Assert.Single(WebSearchFleetPlanner.CreateSchedule(
            configuration,
            doctor,
            new WebSearchFleetEvidenceSnapshot { StoreExists = true, DatabaseSchemaVersion = 7 },
            AsOf).WorkItems);

        Assert.Equal(WebSearchProviderCapabilities.SearchSitemaps, item.Capability);
        Assert.Equal("unsupported-capability", item.Action);
        Assert.Equal("collector-unavailable", item.Readiness);
    }

    [Fact]
    public void Planner_KeepsHealthyProviderEvidenceWhenAnUnrelatedRegistrationChanges()
    {
        var configuration = CreateConfiguration();
        configuration.Sites[0].Providers = [configuration.Sites[0].Providers.Single(value => value.Id == "gsc")];
        configuration.Operations!.BackfillStartDate = null;
        var hash = ProviderConfigurationHash(configuration, "gsc");
        var evidence = Stream(
            "gsc",
            WebSearchProviderCapabilities.SearchAnalytics,
            new DateOnly(2026, 8, 7),
            AsOf.AddMinutes(-1),
            configurationHash: hash);
        configuration.Sites =
        [
            .. configuration.Sites,
            new WebSearchSiteProviderConfiguration
            {
                Id = "broken",
                BaseUrl = "not-a-url",
                Providers =
                [
                    Provider("broken", "unknown", "unknown")
                ]
            }
        ];
        var doctor = Doctor(configuration);

        var report = WebSearchFleetPlanner.CreateReport(
            configuration,
            doctor,
            new WebSearchFleetEvidenceSnapshot { StoreExists = true, DatabaseSchemaVersion = 7, Streams = [evidence] },
            AsOf);

        Assert.False(doctor.Success);
        Assert.Equal(hash, ProviderConfigurationHash(configuration, "gsc"));
        Assert.Equal("current", Row(report, "gsc").EvidenceState);
    }

    [Fact]
    public void Planner_IgnoresFutureCoverageWhenDerivingDefaultBackfillStart()
    {
        var configuration = CreateConfiguration();
        configuration.Sites[0].Providers = [configuration.Sites[0].Providers.Single(value => value.Id == "gsc")];
        configuration.Operations!.BackfillStartDate = null;
        var doctor = Doctor(configuration);
        var targetThrough = new DateOnly(2026, 8, 7);
        var evidence = Stream(
            "gsc",
            WebSearchProviderCapabilities.SearchAnalytics,
            targetThrough.AddDays(10),
            AsOf.AddMinutes(-1),
            configurationHash: ProviderConfigurationHash(configuration, "gsc"));
        evidence.CompletedRanges =
        [
            new WebSearchFleetCompletedRange
            {
                FromDate = targetThrough.AddDays(10),
                ThroughDate = targetThrough.AddDays(10)
            }
        ];

        var snapshot = new WebSearchFleetEvidenceSnapshot
        {
            StoreExists = true,
            DatabaseSchemaVersion = 7,
            Streams = [evidence]
        };
        var work = Assert.Single(WebSearchFleetPlanner.CreateSchedule(
            configuration,
            doctor,
            snapshot,
            AsOf).WorkItems);

        Assert.Equal(targetThrough.AddDays(1 - configuration.Operations.MaxBackfillDaysPerRun), work.FromDate);
        Assert.Equal(targetThrough, work.ThroughDate);
        Assert.Equal("ready", work.Readiness);
        Assert.Equal("due", Assert.Single(WebSearchFleetPlanner.CreateReport(
            configuration, doctor, snapshot, AsOf).Rows).EvidenceState);
    }

    [Fact]
    public void Planner_EmitsExecutableHealthyWorkDespiteAnUnrelatedStructuralError()
    {
        var configuration = CreateConfiguration();
        configuration.Sites[0].Providers = [configuration.Sites[0].Providers.Single(value => value.Id == "gsc")];
        configuration.Operations!.BackfillStartDate = new DateOnly(2026, 1, 1);
        configuration.Operations.MaxBackfillDaysPerRun = 31;
        configuration.Sites =
        [
            .. configuration.Sites,
            new WebSearchSiteProviderConfiguration
            {
                Id = "broken",
                BaseUrl = "not-a-url",
                Providers = [Provider("broken", "unknown", "unknown")]
            }
        ];
        var doctor = Doctor(configuration);

        var item = Assert.Single(WebSearchFleetPlanner.CreateSchedule(
            configuration,
            doctor,
            new WebSearchFleetEvidenceSnapshot { StoreExists = true, DatabaseSchemaVersion = 7 },
            AsOf).WorkItems, value => value.ProviderId == "gsc");

        Assert.False(doctor.Success);
        Assert.Equal("ready", item.Readiness);
        Assert.Equal(new DateOnly(2026, 1, 1), item.FromDate);
        Assert.Equal(new DateOnly(2026, 1, 7), item.ThroughDate);
        Assert.True(item.HasMoreBackfill);
    }

    [Fact]
    public void Planner_ReturnsConfigurationErrorsWhenADailyProviderKindIsNull()
    {
        var configuration = CreateConfiguration();
        var provider = configuration.Sites[0].Providers.Single(value => value.Id == "gsc");
        configuration.Sites[0].Providers = [provider];
        provider.Kind = null!;
        var doctor = Doctor(configuration);

        var plan = WebSearchFleetPlanner.CreateSchedule(
            configuration,
            doctor,
            new WebSearchFleetEvidenceSnapshot { StoreExists = true, DatabaseSchemaVersion = 7 },
            AsOf);
        var report = WebSearchFleetPlanner.CreateReport(
            configuration,
            doctor,
            new WebSearchFleetEvidenceSnapshot { StoreExists = true, DatabaseSchemaVersion = 7 },
            AsOf);

        Assert.False(doctor.Success);
        Assert.Equal("configuration-error", Assert.Single(plan.WorkItems).Readiness);
        Assert.Equal("configuration-error", Assert.Single(report.Rows).EvidenceState);
    }
}
