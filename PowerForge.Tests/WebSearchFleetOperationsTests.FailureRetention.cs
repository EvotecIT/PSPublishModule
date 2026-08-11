using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebSearchFleetOperationsTests
{
    [Fact]
    public void Planner_DoesNotBlockAnEarlierGapForALaterPermanentFailure()
    {
        var configuration = CreateConfiguration();
        configuration.Sites[0].Providers = [configuration.Sites[0].Providers.Single(value => value.Id == "cloudflare")];
        configuration.Operations!.BackfillStartDate = new DateOnly(2026, 1, 1);
        var doctor = Doctor(configuration);
        var snapshot = new WebSearchFleetEvidenceSnapshot
        {
            StoreExists = true,
            DatabaseSchemaVersion = 7,
            Streams =
            [
                Stream(
                    "cloudflare",
                    WebSearchProviderCapabilities.TrafficAnalytics,
                    null,
                    AsOf.AddMinutes(-1),
                    partial: true,
                    configurationHash: ProviderConfigurationHash(configuration, "cloudflare"),
                    failureCategory: "row-limit-reached",
                    failureDate: new DateOnly(2026, 7, 1))
            ]
        };

        var work = Assert.Single(WebSearchFleetPlanner.CreateSchedule(configuration, doctor, snapshot, AsOf).WorkItems);

        Assert.Equal(new DateOnly(2026, 1, 1), work.FromDate);
        Assert.Equal("ready", work.Readiness);
        Assert.Null(work.FailureCategory);
    }

    [Fact]
    public async Task Retention_IncludesEmptyLegacyPartialRuns()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            foreach (var (runId, collectedAt) in new[]
                     {
                         ("legacy-empty-old", AsOf.AddDays(-60)),
                         ("legacy-empty-current", AsOf.AddDays(-1))
                     })
            {
                await store.ImportAsync(WebSearchObservationNormalizer.Normalize(new WebSearchObservationBatch
                {
                    SchemaVersion = 1,
                    RunId = runId,
                    Provider = "gsc",
                    SiteId = "officeimo",
                    CollectedAtUtc = collectedAt,
                    SourceKind = "fixture",
                    Status = "partial",
                    Observations = Array.Empty<WebSearchObservation>()
                }));
            }
            var policy = new WebSearchFleetOperationsConfiguration
            {
                SearchRunRetentionDays = 30,
                TrafficRunRetentionDays = 30,
                PerformanceRunRetentionDays = 30
            };

            var result = await store.ApplyFleetRetentionAsync(policy, AsOf, apply: true);

            var search = Assert.Single(result.Kinds, value => value.Kind == "search");
            Assert.Equal(1, search.CandidateRunCount);
            Assert.Equal(1, search.DeletedRunCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
