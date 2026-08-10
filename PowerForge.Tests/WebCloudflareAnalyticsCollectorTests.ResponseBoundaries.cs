using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebCloudflareAnalyticsCollectorTests
{
    [Fact]
    public async Task Collect_ScalesSampledRequestCountsBeforePersistence()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/", 100, 25, 5000, 2.5)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectAsync(CreateOptions());

        Assert.True(result.Success);
        Assert.Equal(250, Assert.Single(result.Batch.Observations).Requests);
    }

    [Fact]
    public async Task Collect_RejectsNullTrafficGroupElements()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse((object?)null!),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Empty(result.Batch.Observations);
    }

    [Theory]
    [InlineData("evil.com@officeimo.com")]
    [InlineData("officeimo.com:443")]
    [InlineData("officeimo.com/path")]
    public async Task Probe_RejectsZoneNamesContainingUriComponents(string zoneName)
    {
        var handler = new ScriptedHandler((_, index) => index == 0
            ? ZoneResponse(zoneName)
            : throw new InvalidOperationException("Capability probing must not run for malformed zone names."));
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).ProbeAsync(ZoneId, "https://officeimo.com/");

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Single(handler.Requests);
    }
}
