using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebPerformanceObservationTests
{
    [Theory]
    [InlineData(0.749)]
    [InlineData(0.751)]
    public void Normalizer_AllowsP75AtExactProviderDensityRoundingToleranceEndpoints(double firstBinDensity)
    {
        var batch = CreateFieldBatch();
        batch.Observations[0].Value = 2600;
        batch.Observations[0].Histogram =
        [
            new WebPerformanceHistogramBin { Start = 0, End = 2500, Density = firstBinDensity },
            new WebPerformanceHistogramBin { Start = 2500, End = 4000, Density = 1d - firstBinDensity },
            new WebPerformanceHistogramBin { Start = 4000, Density = 0 }
        ];

        var normalized = WebPerformanceObservationNormalizer.Normalize(batch);

        Assert.Equal(2600, Assert.Single(normalized.Observations).Value);
    }

    [Fact]
    public void Normalizer_AllowsP75InAdjacentBinWithinProviderDensityRoundingTolerance()
    {
        var batch = CreateFieldBatch();
        batch.Observations[0].Value = 2300;
        batch.Observations[0].Histogram =
        [
            new WebPerformanceHistogramBin { Start = 0, End = 2500, Density = 0.7499 },
            new WebPerformanceHistogramBin { Start = 2500, End = 4000, Density = 0.2001 },
            new WebPerformanceHistogramBin { Start = 4000, Density = 0.05 }
        ];

        var normalized = WebPerformanceObservationNormalizer.Normalize(batch);

        Assert.Equal(2300, normalized.Observations[0].Value);
    }

    [Fact]
    public void Normalizer_RejectsP75OutsideTheCrossingBinBeyondDensityRoundingTolerance()
    {
        var batch = CreateFieldBatch();
        batch.Observations[0].Value = 2300;
        batch.Observations[0].Histogram =
        [
            new WebPerformanceHistogramBin { Start = 0, End = 2500, Density = 0.748 },
            new WebPerformanceHistogramBin { Start = 2500, End = 4000, Density = 0.202 },
            new WebPerformanceHistogramBin { Start = 4000, Density = 0.05 }
        ];

        var exception = Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));

        Assert.Contains("inconsistent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalizer_AllowsP75InFollowingBinWithinProviderDensityRoundingTolerance()
    {
        var batch = CreateFieldBatch();
        batch.Observations[0].Value = 2700;
        batch.Observations[0].Histogram =
        [
            new WebPerformanceHistogramBin { Start = 0, End = 2500, Density = 0.7501 },
            new WebPerformanceHistogramBin { Start = 2500, End = 4000, Density = 0.1999 },
            new WebPerformanceHistogramBin { Start = 4000, Density = 0.05 }
        ];

        var normalized = WebPerformanceObservationNormalizer.Normalize(batch);

        Assert.Equal(2700, normalized.Observations[0].Value);
    }


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
