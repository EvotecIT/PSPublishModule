using System.Net;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebPerformanceObservationTests
{
    [Fact]
    public async Task CruxCollector_AllowsOriginEvidenceForAPathScopedFleetSite()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(CruxResponse()));
        using var client = new HttpClient(handler);
        var options = CruxOptions();
        options.SiteBaseUrl = "https://officeimo.com/docs/";

        var result = await new CruxCollector(client, new FakeApiKeyProvider()).CollectAsync(options);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("https://officeimo.com/", result.Batch.TargetUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication-failed")]
    [InlineData(HttpStatusCode.Forbidden, "authentication-failed")]
    [InlineData(HttpStatusCode.BadRequest, "request-rejected")]
    [InlineData(HttpStatusCode.RequestTimeout, "provider-unavailable")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limited")]
    [InlineData(HttpStatusCode.InternalServerError, "provider-unavailable")]
    public async Task CruxCollector_ClassifiesActionableHttpFailures(HttpStatusCode statusCode, string expectedCode)
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(statusCode));
        using var client = new HttpClient(handler);

        var result = await new CruxCollector(client, new FakeApiKeyProvider()).CollectAsync(CruxOptions());

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public void Normalizer_RejectsAnExplicitlyNullObservationArray()
    {
        var batch = CreateFieldBatch();
        batch.Observations = null!;

        var exception = Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));

        Assert.Contains("must be an array", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalizer_RejectsAnExplicitlyNullHistogram()
    {
        var batch = CreateFieldBatch();
        batch.Observations[0].Histogram = null!;

        var exception = Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));

        Assert.Contains("histogram must be an array", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CruxCollector_SendsTheCanonicalTargetAcceptedByOwnershipValidation()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(CruxResponse()));
        using var client = new HttpClient(handler);
        var options = CruxOptions();
        options.TargetUrl = " HTTPS://OFFICEIMO.COM:443/ ";

        var result = await new CruxCollector(client, new FakeApiKeyProvider()).CollectAsync(options);

        Assert.True(result.Success, result.ErrorMessage);
        using var body = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("https://officeimo.com/", body.RootElement.GetProperty("origin").GetString());
        Assert.Equal("https://officeimo.com/", result.Batch.TargetUrl);
    }

    [Fact]
    public void CruxCli_CanonicalizesOriginBeforeConstructingProviderOptions()
    {
        var target = WebCliCommandHandlers.NormalizeCruxCommandTarget("origin", " HTTPS://OFFICEIMO.COM:443/docs/ ");

        Assert.Equal("https://officeimo.com/", target);
    }
}
