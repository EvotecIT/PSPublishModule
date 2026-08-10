using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebCloudflareAnalyticsCollectorTests
{
    [Fact]
    public async Task TrafficEvidence_PrefersNewerCompleteZeroOverOlderCompleteRows()
    {
        var root = CreateTrafficStorageRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "traffic.db"));
            var older = CreateTrafficBatch();
            older.RunId = "older-complete-rows";
            var newerZero = CreateTrafficBatch();
            newerZero.RunId = "newer-complete-zero";
            newerZero.CollectedAtUtc = older.CollectedAtUtc.AddMinutes(1);
            newerZero.Observations = Array.Empty<WebTrafficObservation>();
            newerZero.ZeroDataConfirmed = true;
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(older));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(newerZero));

            var evidence = await store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = "officeimo",
                Provider = "cloudflare",
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 1)
            });

            Assert.Empty(evidence.Observations);
            var run = Assert.Single(evidence.SelectedRuns);
            Assert.Equal("newer-complete-zero", run.RunId);
            Assert.True(evidence.HasExplicitZeroEvidence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrafficEvidence_DetectsInternalGapsForOneSidedDateBounds(bool fromOnly)
    {
        var root = CreateTrafficStorageRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "traffic.db"));
            var first = CreateTrafficBatch();
            first.RunId = "day-one";
            var third = CreateTrafficBatch();
            third.RunId = "day-three";
            third.CollectedAtUtc = first.CollectedAtUtc.AddMinutes(1);
            third.CollectionCoverage.FromDate = new DateOnly(2026, 8, 3);
            third.CollectionCoverage.ThroughDate = new DateOnly(2026, 8, 3);
            third.CollectionCoverage.CompletedDates = [new DateOnly(2026, 8, 3)];
            third.Observations[0].Date = new DateOnly(2026, 8, 3);
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(first));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(third));
            var query = new WebTrafficObservationQuery { SiteId = "officeimo", Provider = "cloudflare" };
            if (fromOnly)
                query.FromDate = new DateOnly(2026, 8, 1);
            else
                query.ThroughDate = new DateOnly(2026, 8, 3);

            var evidence = await store.QueryTrafficEvidenceAsync(query);

            Assert.True(evidence.HasCoverageGaps);
            Assert.Equal([new DateOnly(2026, 8, 2)], evidence.MissingDates);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTrafficStorageRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-traffic-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
