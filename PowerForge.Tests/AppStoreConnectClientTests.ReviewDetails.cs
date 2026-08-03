using System.Net;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task ReviewDetailsCreateAndUpdateUseOfficialResourceShapeWithoutNotes()
    {
        var response = """{ "data": { "type": "appStoreReviewDetails", "id": "review-1", "attributes": { "contactFirstName": "Ada", "contactLastName": "Lovelace", "contactPhone": "+48123456789", "contactEmail": "review@example.test", "demoAccountRequired": false } } }""";
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.Created, response),
            new SequenceResponse(HttpStatusCode.OK, response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var details = CompleteReviewDetails();

        _ = await client.CreateReviewDetailsAsync("version-1", details);
        _ = await client.UpdateReviewDetailsAsync("review-1", details);

        Assert.Equal([HttpMethod.Post, HttpMethod.Patch], handler.Methods);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreReviewDetails", handler.RequestUris[0].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreReviewDetails/review-1", handler.RequestUris[1].ToString());
        using var create = JsonDocument.Parse(handler.RequestBodies[0]);
        var createData = create.RootElement.GetProperty("data");
        Assert.Equal("version-1", createData.GetProperty("relationships").GetProperty("appStoreVersion").GetProperty("data").GetProperty("id").GetString());
        Assert.False(createData.GetProperty("attributes").TryGetProperty("notes", out _));
        using var update = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.False(update.RootElement.GetProperty("data").GetProperty("attributes").TryGetProperty("notes", out _));
    }

    [Fact]
    public async Task ReviewDetailsCopyPlanAndReceiptNeverSerializeContactValues()
    {
        var handler = new ReviewDetailsCopyHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewDetailsCopyService(client);
        var spec = ReviewDetailsCopySpec();

        var plan = await service.PlanAsync(spec);
        var result = await service.ApplyAsync(spec, plan, confirmApply: true);

        Assert.False(plan.IsConverged);
        Assert.False(plan.TargetExists);
        Assert.True(result.Success);
        Assert.True(result.CreatedVersion);
        Assert.True(result.Created);
        Assert.True(result.FinalPlan.IsConverged);
        Assert.Equal(2, handler.MutationBodies.Count);
        var serialized = JsonSerializer.Serialize(new { plan, result });
        foreach (var sensitive in ReviewDetailsCopyHandler.SensitiveValues)
            Assert.DoesNotContain(sensitive, serialized, StringComparison.Ordinal);
        Assert.All(handler.MutationBodies, body => Assert.DoesNotContain("notes", body, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReviewDetailsApplyRejectsChangedSourceBeforeMutation()
    {
        var handler = new ReviewDetailsCopyHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewDetailsCopyService(client);
        var spec = ReviewDetailsCopySpec();
        var reviewed = await service.PlanAsync(spec);
        handler.ContactEmail = "changed@example.test";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(spec, reviewed, confirmApply: true));

        Assert.Contains("changed after review", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.MutationBodies);
        Assert.DoesNotContain(handler.ContactEmail, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewDetailsApplyReceiptsPartialDraftCreationWithoutLeakingContact()
    {
        var handler = new ReviewDetailsCopyHandler { FailDetailsMutation = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewDetailsCopyService(client);
        var spec = ReviewDetailsCopySpec();
        var reviewed = await service.PlanAsync(spec);

        var result = await service.ApplyAsync(spec, reviewed, confirmApply: true);

        Assert.False(result.Success);
        Assert.True(result.CreatedVersion);
        Assert.True(result.FinalPlan.TargetVersionExists);
        Assert.False(result.FinalPlan.TargetExists);
        Assert.Equal("APPLE_REVIEW_DETAILS_APPLY_FAILED", result.ErrorCode);
        var serialized = JsonSerializer.Serialize(result);
        foreach (var sensitive in ReviewDetailsCopyHandler.SensitiveValues)
            Assert.DoesNotContain(sensitive, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewDetailsApplyUpdatesExistingTargetAndConverges()
    {
        var handler = new ReviewDetailsCopyHandler
        {
            TargetVersionExists = true,
            TargetExists = true,
            TargetContactEmail = "old@example.test"
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewDetailsCopyService(client);
        var spec = ReviewDetailsCopySpec();
        var reviewed = await service.PlanAsync(spec);

        var result = await service.ApplyAsync(spec, reviewed, confirmApply: true);

        Assert.True(result.Success);
        Assert.False(result.CreatedVersion);
        Assert.False(result.Created);
        Assert.True(result.Updated);
        Assert.True(result.FinalPlan.IsConverged);
        Assert.Single(handler.MutationBodies);
    }

    [Fact]
    public async Task ReviewDetailsApplyTreatsAmbiguousResponseAsSuccessOnlyAfterFreshConvergence()
    {
        var handler = new ReviewDetailsCopyHandler { ApplyThenFailDetailsMutation = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewDetailsCopyService(client);
        var spec = ReviewDetailsCopySpec();
        var reviewed = await service.PlanAsync(spec);

        var result = await service.ApplyAsync(spec, reviewed, confirmApply: true);

        Assert.True(result.Success);
        Assert.True(result.CreatedVersion);
        Assert.True(result.Created);
        Assert.True(result.FinalPlan.IsConverged);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ReviewDetailsApplyReportsOnlySafeProviderClassification()
    {
        var handler = new ReviewDetailsCopyHandler { FailVersionMutation = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewDetailsCopyService(client);
        var spec = ReviewDetailsCopySpec();
        var reviewed = await service.PlanAsync(spec);

        var result = await service.ApplyAsync(spec, reviewed, confirmApply: true);

        Assert.False(result.Success);
        Assert.Equal("create-target-version", result.FailureOperation);
        Assert.Equal(409, result.ProviderStatusCode);
        Assert.Equal(["ENTITY_ERROR.ATTRIBUTE.INVALID"], result.ProviderErrorCodes);
        Assert.Equal(["/data/attributes/versionString"], result.ProviderErrorPointers);
        var serialized = JsonSerializer.Serialize(result);
        foreach (var sensitive in ReviewDetailsCopyHandler.SensitiveValues)
            Assert.DoesNotContain(sensitive, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("provider secret detail", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewDetailsApplyRejectsSecretBearingProviderClassification()
    {
        const string secret = "DemoPassword123";
        var handler = new ReviewDetailsCopyHandler
        {
            VersionFailureBody = $$"""{ "errors": [{ "status": "409", "code": "{{secret}}", "source": { "pointer": "/data/attributes/demoAccountPassword/{{secret}}" } }] }"""
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewDetailsCopyService(client);
        var spec = ReviewDetailsCopySpec();
        var reviewed = await service.PlanAsync(spec);

        var result = await service.ApplyAsync(spec, reviewed, confirmApply: true);

        Assert.False(result.Success);
        Assert.Equal(409, result.ProviderStatusCode);
        Assert.Empty(result.ProviderErrorCodes);
        Assert.Empty(result.ProviderErrorPointers);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private static AppStoreConnectReviewDetailsInfo CompleteReviewDetails()
        => new()
        {
            ContactFirstName = "Ada",
            ContactLastName = "Lovelace",
            ContactPhone = "+48123456789",
            ContactEmail = "review@example.test",
            DemoAccountRequired = false
        };

    private static AppStoreConnectReviewDetailsCopySpec ReviewDetailsCopySpec()
        => new()
        {
            CreateTargetVersion = true,
            Source = new AppStoreConnectReviewDetailsVersionRef
            {
                AppId = "source-app",
                VersionString = "1.4.0",
                Platform = ApplePlatform.iOS
            },
            Target = new AppStoreConnectReviewDetailsVersionRef
            {
                AppId = "target-app",
                VersionString = "0.1.0",
                Platform = ApplePlatform.macOS
            }
        };

    private sealed class ReviewDetailsCopyHandler : HttpMessageHandler
    {
        public static readonly string[] SensitiveValues = ["Ada", "Lovelace", "+48123456789", "review@example.test"];

        public string ContactEmail { get; set; } = SensitiveValues[3];

        public string TargetContactEmail { get; set; } = "old@example.test";

        public bool TargetExists { get; set; }

        public bool TargetVersionExists { get; set; }

        public bool FailDetailsMutation { get; set; }

        public bool ApplyThenFailDetailsMutation { get; set; }

        public bool FailVersionMutation { get; set; }

        public string? VersionFailureBody { get; set; }

        public List<string> MutationBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/source-app/appStoreVersions", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, Version("source-version", "1.4.0", "IOS"));
            if (path.EndsWith("/target-app/appStoreVersions", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, TargetVersionExists
                    ? Version("target-version", "0.1.0", "MAC_OS")
                    : """{ "data": [] }""");
            if (request.Method == HttpMethod.Post && path.EndsWith("/appStoreVersions", StringComparison.Ordinal))
            {
                MutationBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (FailVersionMutation || VersionFailureBody is not null)
                {
                    return Json(HttpStatusCode.Conflict,
                        VersionFailureBody ?? """{ "errors": [{ "status": "409", "code": "ENTITY_ERROR.ATTRIBUTE.INVALID", "detail": "provider secret detail review@example.test", "source": { "pointer": "/data/attributes/versionString" } }] }""");
                }
                TargetVersionExists = true;
                return Json(HttpStatusCode.Created,
                    """{ "data": { "type": "appStoreVersions", "id": "target-version", "attributes": { "versionString": "0.1.0", "platform": "MAC_OS", "appStoreState": "PREPARE_FOR_SUBMISSION" } } }""");
            }
            if (path.EndsWith("/source-version/appStoreReviewDetail", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, Review("source-review", ContactEmail));
            if (path.EndsWith("/target-version/appStoreReviewDetail", StringComparison.Ordinal))
                return TargetExists
                    ? Json(HttpStatusCode.OK, Review("target-review", TargetContactEmail))
                    : Json(HttpStatusCode.NotFound, """{ "errors": [{ "status": "404" }] }""");
            if (request.Method == HttpMethod.Post && path.EndsWith("/appStoreReviewDetails", StringComparison.Ordinal))
            {
                MutationBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (FailDetailsMutation)
                    return Json(HttpStatusCode.InternalServerError, Review("rejected", ContactEmail));
                TargetExists = true;
                TargetContactEmail = ContactEmail;
                if (ApplyThenFailDetailsMutation)
                    return Json(HttpStatusCode.InternalServerError, """{ "errors": [{ "status": "500" }] }""");
                return Json(HttpStatusCode.Created, Review("target-review", ContactEmail));
            }
            if (request.Method == HttpMethod.Patch && path.EndsWith("/appStoreReviewDetails/target-review", StringComparison.Ordinal))
            {
                MutationBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                TargetContactEmail = ContactEmail;
                return Json(HttpStatusCode.OK, Review("target-review", ContactEmail));
            }
            return Json(HttpStatusCode.NotFound, """{ "errors": [{ "status": "404" }] }""");
        }

        private static string Version(string id, string version, string platform)
            => $$"""{ "data": [{ "type": "appStoreVersions", "id": "{{id}}", "attributes": { "versionString": "{{version}}", "platform": "{{platform}}", "appStoreState": "PREPARE_FOR_SUBMISSION" } }] }""";

        private static string Review(string id, string email)
            => $$"""{ "data": { "type": "appStoreReviewDetails", "id": "{{id}}", "attributes": { "contactFirstName": "Ada", "contactLastName": "Lovelace", "contactPhone": "+48123456789", "contactEmail": "{{email}}", "demoAccountRequired": false, "notes": "source-specific notes must not copy" } } }""";

        private static HttpResponseMessage Json(HttpStatusCode status, string content)
            => new(status) { Content = new StringContent(content) };
    }
}
