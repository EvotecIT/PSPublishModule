using DBAClientX;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebPerformanceObservationTests
{
    [Fact]
    public async Task Store_RanksPerformanceRevisionsBeforeDeserializingHistoricalManifests()
    {
        var root = CreatePerformanceStorageRoot();
        try
        {
            var path = Path.Combine(root, "performance.db");
            var store = new SqliteWebSearchObservationStore(path);
            var complete = CreateFieldBatch();
            complete.RunId = "complete-run";
            var partial = CreateFieldBatch();
            partial.RunId = "newer-partial-run";
            partial.CollectedAtUtc = complete.CollectedAtUtc.AddMinutes(1);
            partial.Status = "partial";
            await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(complete));
            await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(partial));
            await using var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(
                path,
                "UPDATE performance_observation_runs SET normalized_manifest_json = '{not-json' WHERE run_id = 'newer-partial-run';");

            var evidence = await store.QueryPerformanceEvidenceAsync(new WebPerformanceObservationQuery { SiteId = "officeimo" });

            var selected = Assert.Single(evidence.EvidenceSets);
            Assert.Equal("complete-run", selected.Run.RunId);
            Assert.False(evidence.HasPartialEvidence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Store_OrdersPerformanceEvidenceByCompleteStableIdentity()
    {
        var root = CreatePerformanceStorageRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "performance.db"));
            var zetaUrl = CreateFieldBatch();
            zetaUrl.Provider = "zeta";
            zetaUrl.RunId = "zeta-url";
            zetaUrl.TargetKind = "url";
            var alphaUrl = CreateFieldBatch();
            alphaUrl.Provider = "alpha";
            alphaUrl.RunId = "alpha-url";
            alphaUrl.TargetKind = "url";
            var alphaOrigin = CreateFieldBatch();
            alphaOrigin.Provider = "alpha";
            alphaOrigin.RunId = "alpha-origin";
            alphaOrigin.TargetKind = "origin";
            alphaOrigin.CollectedAtUtc = alphaOrigin.CollectedAtUtc.AddMinutes(1);
            await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(zetaUrl));
            await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(alphaUrl));
            await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(alphaOrigin));

            var evidence = await store.QueryPerformanceEvidenceAsync(new WebPerformanceObservationQuery { SiteId = "officeimo" });

            Assert.Collection(
                evidence.EvidenceSets,
                value => Assert.Equal(("alpha", "origin", "alpha-origin"),
                    (value.Run.Provider, value.Run.TargetKind, value.Run.RunId)),
                value => Assert.Equal(("alpha", "url", "alpha-url"),
                    (value.Run.Provider, value.Run.TargetKind, value.Run.RunId)),
                value => Assert.Equal(("zeta", "url", "zeta-url"),
                    (value.Run.Provider, value.Run.TargetKind, value.Run.RunId)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreatePerformanceStorageRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-performance-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
