using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebPerformanceObservationTests
{
    [Fact]
    public void Normalizer_RejectsP75OutsideTheCrossingHistogramBin()
    {
        var batch = CreateFieldBatch();
        batch.Observations[0].Value = 5000;

        var exception = Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));

        Assert.Contains("p75 value is inconsistent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalizer_RejectsFieldPeriodsEndingAfterCollection()
    {
        var batch = CreateFieldBatch();
        batch.CollectedAtUtc = new DateTimeOffset(2026, 8, 7, 23, 59, 59, TimeSpan.Zero);

        var exception = Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));

        Assert.Contains("cannot end after", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CruxCollector_StampsTheBatchAfterTheRequestCompletes()
    {
        var startedAt = CollectionTime.AddMinutes(-5);
        var clock = new MutableTimeProvider(startedAt);
        var handler = new ScriptedHandler(_ =>
        {
            clock.UtcNow = CollectionTime;
            return JsonResponse(CruxResponse());
        });
        using var client = new HttpClient(handler);

        var result = await new CruxCollector(client, new FakeApiKeyProvider(), clock).CollectAsync(CruxOptions());

        Assert.True(result.Success);
        Assert.Equal(CollectionTime, result.Batch.CollectedAtUtc);
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; } = value;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
