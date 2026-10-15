using System.Net;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task ReviewNotes_UpdatesOnlyNotesAndVerifiesReadbackWithoutLeakingExistingPrivateData()
    {
        var handler = new ReviewNotesHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewNotesService(client);
        var spec = ReviewNotesSpec();
        var plan = await service.PlanAsync(spec);
        var result = await service.ApplyAsync(spec, plan, true);
        Assert.True(result.IsConverged);
        Assert.Equal(spec.Notes, handler.Notes);
        using var patch = JsonDocument.Parse(Assert.Single(handler.Patches));
        var attrs = patch.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Equal("notes", Assert.Single(attrs.EnumerateObject()).Name);
        Assert.Equal(spec.Notes, attrs.GetProperty("notes").GetString());
        var serialized = JsonSerializer.Serialize(new { plan, result });
        Assert.DoesNotContain("Existing private review instructions", serialized);
        Assert.DoesNotContain("private-contact@example.test", serialized);
        Assert.DoesNotContain(spec.Notes!, serialized);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReviewNotes_RejectsChangedDesiredOrRemoteNotesBeforeMutation(bool desiredChanges)
    {
        var handler = new ReviewNotesHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewNotesService(client);
        var spec = ReviewNotesSpec();
        var plan = await service.PlanAsync(spec);
        if (desiredChanges) spec.Notes = "Unreviewed replacement";
        else handler.Notes = "Someone edited the remote instructions";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(spec, plan, true));
        Assert.Empty(handler.Patches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewNotes_ObservesAmbiguousAppliedPatchWithoutRepeatingIt(bool timeout)
    {
        var handler = new ReviewNotesHandler { FailAfterPatch = true, TimeoutAfterPatch = timeout };
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewNotesService(client);
        var spec = ReviewNotesSpec();
        var result = await service.ApplyAsync(spec, await service.PlanAsync(spec), true);
        Assert.True(result.IsConverged);
        Assert.Single(handler.Patches);
    }

    [Fact]
    public async Task ReviewNotes_PreservesCallerCancellationWithoutFurtherObservation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new ReviewNotesHandler { CancelAfterPatch = cancellation.Cancel };
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewNotesService(client);
        var spec = ReviewNotesSpec();
        var plan = await service.PlanAsync(spec, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(spec, plan, true, cancellation.Token));
        Assert.Single(handler.Patches);
        Assert.Equal(2, handler.VersionReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewNotes_ReportsUnconfirmedOutcomeWhenPostPatchObservationFails(bool ambiguousPatch)
    {
        var handler = new ReviewNotesHandler { FailAfterPatch = ambiguousPatch, FailReadsAfterPatch = true };
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewNotesService(client);
        var spec = ReviewNotesSpec();
        var plan = await service.PlanAsync(spec);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(spec, plan, true));
        Assert.Contains("outcome is unconfirmed", error.Message);
        Assert.DoesNotContain("private-contact@example.test", error.ToString());
        Assert.Single(handler.Patches);
        Assert.Equal(spec.Notes, handler.Notes);
    }

    [Fact]
    public void ReviewNotes_ValidatesTheUtf8ByteLimit()
    {
        var spec = ReviewNotesSpec();
        spec.Notes = new string('界', 1333);
        AppStoreConnectReviewNotesService.ValidateSpec(spec);
        spec.Notes += "界";
        Assert.Throws<ArgumentException>(() => AppStoreConnectReviewNotesService.ValidateSpec(spec));
    }

    [Fact]
    public async Task ReviewNotes_ExplicitEmptyReplacementClearsNotesAndConvergesOnNullReadback()
    {
        var handler = new ReviewNotesHandler { ClearAsNull = true };
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewNotesService(client);
        var spec = ReviewNotesSpec();
        spec.Notes = string.Empty;
        var result = await service.ApplyAsync(spec, await service.PlanAsync(spec), true);
        Assert.True(result.IsConverged);
        Assert.Null(handler.Notes);
        using var patch = JsonDocument.Parse(Assert.Single(handler.Patches));
        var attrs = patch.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Equal("notes", Assert.Single(attrs.EnumerateObject()).Name);
        Assert.Equal(string.Empty, attrs.GetProperty("notes").GetString());
    }

    [Theory]
    [InlineData("{\"Target\":{\"AppId\":\"app-1\",\"VersionString\":\"2.2\"}}")]
    [InlineData("{\"Target\":{\"AppId\":\"app-1\",\"VersionString\":\"2.2\"},\"Notes\":null}")]
    public void ReviewNotes_MissingOrNullReplacementCannotClearNotesAccidentally(string json)
    {
        var spec = JsonSerializer.Deserialize<AppStoreConnectReviewNotesSpec>(json)!;
        Assert.Throws<ArgumentException>(() => AppStoreConnectReviewNotesService.ValidateSpec(spec));
    }

    [Fact]
    public async Task ReviewNotes_RequiresConfirmationAndSuppressesProviderPrivateResponse()
    {
        var handler = new ReviewNotesHandler { FailReads = true };
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewNotesService(client);
        var spec = ReviewNotesSpec();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlanAsync(spec));
        Assert.DoesNotContain("private-contact@example.test", failure.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(spec, new(), false));
        Assert.Empty(handler.Patches);
    }

    private static AppStoreConnectReviewNotesSpec ReviewNotesSpec() => new()
    {
        Target = new() { AppId = "app-1", VersionString = "2.2", Platform = ApplePlatform.iOS },
        Notes = "Voice conversation for questions about the configured home."
    };

    private sealed class ReviewNotesHandler : HttpMessageHandler
    {
        public string? Notes { get; set; } = "Existing private review instructions";
        public bool ClearAsNull { get; set; }
        public bool FailAfterPatch { get; set; }
        public bool TimeoutAfterPatch { get; set; }
        public Action? CancelAfterPatch { get; set; }
        public int VersionReads { get; private set; }
        public bool FailReads { get; set; }
        public bool FailReadsAfterPatch { get; set; }
        public List<string> Patches { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Patch)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Patches.Add(body);
                using var parsed = JsonDocument.Parse(body);
                Notes = parsed.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("notes").GetString()!;
                if (ClearAsNull && Notes.Length == 0) Notes = null;
                if (CancelAfterPatch is not null)
                {
                    CancelAfterPatch();
                    throw new TaskCanceledException("The caller canceled the request.");
                }
                if (TimeoutAfterPatch) throw new TaskCanceledException("The provider response timed out.");
                if (FailAfterPatch) return Response(HttpStatusCode.InternalServerError, "private-contact@example.test");
            }
            else if (FailReads || (FailReadsAfterPatch && Patches.Count > 0))
                return Response(HttpStatusCode.Forbidden, "private-contact@example.test");
            if (request.RequestUri!.AbsolutePath.EndsWith("/appStoreVersions"))
            {
                VersionReads++;
                return Response(HttpStatusCode.OK, """{"data":[{"type":"appStoreVersions","id":"version-1","attributes":{"versionString":"2.2","platform":"IOS","appStoreState":"REJECTED"}}]}""");
            }
            return Response(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = new {
                type = "appStoreReviewDetails", id = "review-1", attributes = new {
                    notes = Notes, contactEmail = "private-contact@example.test", demoAccountRequired = false
                }
            }}));
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
