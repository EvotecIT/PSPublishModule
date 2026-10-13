using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Theory]
    [InlineData("other-version")]
    [InlineData(null)]
    public async Task ReviewWithdrawal_does_not_cancel_from_decoded_item_identity_without_authoritative_binding(string? relationshipVersion)
    {
        var changed = WithdrawalInventory();
        var itemId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("anything|anything|version-2"));
        changed[4] = WithdrawalResponse(new
        {
            data = new[] { new { id = itemId, type = "reviewSubmissionItems", attributes = new { state = "READY_FOR_REVIEW" },
                relationships = new { appStoreVersion = new { data = relationshipVersion is null ? null : new { type = "appStoreVersions", id = relationshipVersion } } } } }
        });
        if (relationshipVersion is not null) changed[5] = WithdrawalRelatedVersion(id: relationshipVersion);
        var handler = new SequenceHandler(changed.Concat(changed).Concat(new[]
        {
            WithdrawalSubmission("CANCELING", single: true), WithdrawalSubmission("COMPLETE", single: true),
            WithdrawalVersion("DEVELOPER_REJECTED"), WithdrawalSelectedBuild()
        }).ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewSubmissionService(client);
        var error = await Record.ExceptionAsync(async () =>
        {
            var plan = await service.PlanWithdrawalAsync(WithdrawalRequest());
            await service.WithdrawAsync(plan, confirmWithdrawal: true);
        });

        Assert.DoesNotContain(handler.Methods, method => method == new HttpMethod("PATCH"));
        Assert.IsType<InvalidOperationException>(error);
    }

    [Theory]
    [InlineData("WAITING_FOR_REVIEW", "WAITING_FOR_REVIEW", true)]
    [InlineData("PENDING_DEVELOPER_RELEASE", "COMPLETE", true)]
    [InlineData("IN_REVIEW", "IN_REVIEW", false)]
    public async Task ReviewWithdrawal_revalidates_exact_single_item_and_reads_back_cancellation(
        string versionState, string submissionState, bool observed)
    {
        var responses = WithdrawalInventory(versionState, submissionState);
        var handler = new SequenceHandler(responses.Concat(responses).Concat(new[]
        {
            WithdrawalSubmission("CANCELING", single: true),
            WithdrawalSubmission(observed ? "COMPLETE" : "CANCELING", single: true),
            WithdrawalVersion(observed ? "DEVELOPER_REJECTED" : versionState), WithdrawalSelectedBuild()
        }).ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewSubmissionService(client);
        var plan = await service.PlanWithdrawalAsync(WithdrawalRequest());
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));

        var result = await service.WithdrawAsync(plan, confirmWithdrawal: true);

        Assert.True(result.CancellationRequested);
        Assert.Equal(observed, result.WithdrawalObserved);
        Assert.Equal("build-28", result.Plan.BuildId);
        Assert.Equal("version-2", result.Plan.AppStoreVersionId);
        Assert.Equal("item-1", result.Plan.ReviewSubmissionItemId);
        Assert.Equal("CANCELING", result.ResponseSubmissionState);
        Assert.Equal(observed ? "DEVELOPER_REJECTED" : versionState, result.ObservedVersionState);
        Assert.Single(handler.Methods, method => method == new HttpMethod("PATCH"));
        using var body = JsonDocument.Parse(handler.RequestBodies[12]);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("submission-1", data.GetProperty("id").GetString());
        Assert.True(data.GetProperty("attributes").GetProperty("canceled").GetBoolean());
        Assert.Equal("reviewSubmissions", data.GetProperty("type").GetString());
        Assert.DoesNotContain(handler.Methods, method => method == HttpMethod.Post || method == HttpMethod.Delete);
    }

    [Theory]
    [InlineData("different-build")]
    [InlineData("different-version-item")]
    [InlineData("mixed-items")]
    [InlineData("released-version")]
    [InlineData("canceling-submission")]
    [InlineData("wrong-platform")]
    [InlineData("removed-item")]
    public async Task ReviewWithdrawal_refuses_other_targets_and_ineligible_states_before_mutation(string mismatch)
    {
        var responses = WithdrawalInventory();
        if (mismatch == "different-build") responses[2] = WithdrawalSelectedBuild("other-build");
        if (mismatch == "different-version-item")
        {
            responses[4] = WithdrawalItems("other-version");
            responses[5] = WithdrawalRelatedVersion(id: "other-version");
        }
        if (mismatch == "mixed-items") responses[4] = WithdrawalItems(additionalItem: true);
        if (mismatch == "released-version") responses[0] = WithdrawalVersion("READY_FOR_DISTRIBUTION");
        if (mismatch == "canceling-submission") responses[3] = WithdrawalSubmission("CANCELING");
        if (mismatch == "wrong-platform") responses[0] = WithdrawalVersion("WAITING_FOR_REVIEW", "IOS");
        if (mismatch == "removed-item") responses[4] = WithdrawalItems(itemState: "REMOVED");
        var handler = new SequenceHandler(responses);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AppStoreConnectReviewSubmissionService(client).PlanWithdrawalAsync(WithdrawalRequest()));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task ReviewWithdrawal_counts_other_items_on_later_pages()
    {
        var responses = WithdrawalInventory();
        responses[4] = WithdrawalResponse(new
        {
            data = new[] { WithdrawalItem("version-2") },
            links = new { next = "https://api.appstoreconnect.apple.com/v1/reviewSubmissions/submission-1/items?cursor=next" }
        });
        var handler = new SequenceHandler(responses.Take(5).Concat(new[] { WithdrawalResponse(new
        {
            data = new[] { new { id = "event-item", type = "reviewSubmissionItems", attributes = new { state = "WAITING_FOR_REVIEW" } } }
        }) }).ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AppStoreConnectReviewSubmissionService(client).PlanWithdrawalAsync(WithdrawalRequest()));
        Assert.Equal(6, handler.Methods.Count);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task ReviewWithdrawal_requires_confirmation_and_fresh_unchanged_plan()
    {
        var changed = WithdrawalInventory("IN_REVIEW", "IN_REVIEW");
        var handler = new SequenceHandler(WithdrawalInventory().Concat(changed).ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewSubmissionService(client);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.WithdrawAsync(new()));
        Assert.Empty(handler.Methods);
        var plan = await service.PlanWithdrawalAsync(WithdrawalRequest());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.WithdrawAsync(plan, confirmWithdrawal: true));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Theory]
    [InlineData("response-identity")]
    [InlineData("readback-identity")]
    [InlineData("mutation-error")]
    [InlineData("readback-version")]
    [InlineData("readback-build")]
    public async Task ReviewWithdrawal_does_not_retry_or_claim_withdrawal_after_uncertain_mutation(string failure)
    {
        var response = failure == "mutation-error"
            ? new SequenceResponse(HttpStatusCode.Conflict, "{\"errors\":[{\"detail\":\"State changed\"}]}")
            : WithdrawalSubmission("CANCELING", single: true, id: failure == "response-identity" ? "other-submission" : "submission-1");
        var handler = new SequenceHandler(WithdrawalInventory().Concat(WithdrawalInventory()).Concat(new[]
        {
            response, WithdrawalSubmission("COMPLETE", single: true, id: failure == "readback-identity" ? "other-submission" : "submission-1"),
            failure == "readback-version" ? WithdrawalVersion("DEVELOPER_REJECTED", "IOS") : WithdrawalVersion("DEVELOPER_REJECTED"),
            WithdrawalSelectedBuild(failure == "readback-build" ? "other-build" : "build-28")
        }).ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewSubmissionService(client);
        var plan = await service.PlanWithdrawalAsync(WithdrawalRequest());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.WithdrawAsync(plan, confirmWithdrawal: true));
        Assert.Contains("Reconcile", error.Message);
        Assert.NotNull(error.InnerException);
        Assert.Single(handler.Methods, method => method == new HttpMethod("PATCH"));
    }

    [Fact]
    public async Task ReviewWithdrawal_resolves_alternate_relationship_through_authoritative_version_resource()
    {
        var responses = WithdrawalInventory();
        responses[4] = WithdrawalItems("alternate-relationship-id");
        var handler = new SequenceHandler(responses);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var plan = await new AppStoreConnectReviewSubmissionService(client).PlanWithdrawalAsync(WithdrawalRequest());
        Assert.Equal("version-2", plan.AppStoreVersionId);
        Assert.Equal("/v1/appStoreVersions/alternate-relationship-id", handler.RequestUris[5].AbsolutePath);
        Assert.Contains("app%2Cbuild", handler.RequestUris[5].Query);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Theory]
    [InlineData("app")]
    [InlineData("build")]
    [InlineData("platform")]
    [InlineData("missing")]
    public async Task ReviewWithdrawal_refuses_unbound_or_conflicting_authoritative_version_resource(string mismatch)
    {
        var responses = WithdrawalInventory();
        responses[5] = mismatch == "missing" ? new SequenceResponse(HttpStatusCode.NotFound, "{\"errors\":[]}")
            : WithdrawalRelatedVersion(appId: mismatch == "app" ? "other-app" : "app-1",
                buildId: mismatch == "build" ? "other-build" : "build-28", platform: mismatch == "platform" ? "IOS" : "MAC_OS");
        var handler = new SequenceHandler(responses);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AppStoreConnectReviewSubmissionService(client).PlanWithdrawalAsync(WithdrawalRequest()));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task ReviewWithdrawal_caller_plan_edits_during_awaits_do_not_redirect_cancellation_or_readback()
    {
        var responses = WithdrawalInventory();
        var handler = new SequenceHandler(responses.Concat(responses).Concat(new[]
        {
            WithdrawalSubmission("CANCELING", single: true), WithdrawalSubmission("COMPLETE", single: true),
            WithdrawalVersion("DEVELOPER_REJECTED"), WithdrawalSelectedBuild()
        }).ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewSubmissionService(client);
        var plan = await service.PlanWithdrawalAsync(WithdrawalRequest());
        handler.OnRequest = _ =>
        {
            plan.AppId = "other-app"; plan.VersionString = "9.9"; plan.BuildNumber = "99";
            plan.Platform = ApplePlatform.iOS; plan.AppStoreVersionId = "other-version"; plan.BuildId = "other-build";
            plan.ReviewSubmissionId = "other-submission"; plan.ReviewSubmissionItemId = "other-item";
            plan.AppStoreState = "READY_FOR_DISTRIBUTION"; plan.AppVersionState = "READY_FOR_DISTRIBUTION";
            plan.SubmissionState = "COMPLETE"; plan.ItemState = "REMOVED";
        };
        var result = await service.WithdrawAsync(plan, confirmWithdrawal: true);
        Assert.True(result.WithdrawalObserved);
        Assert.Equal("app-1", result.Plan.AppId);
        Assert.Equal("version-2", result.Plan.AppStoreVersionId);
        Assert.Equal("build-28", result.Plan.BuildId);
        Assert.Equal("submission-1", result.Plan.ReviewSubmissionId);
        Assert.Equal("/v1/reviewSubmissions/submission-1", handler.RequestUris[12].AbsolutePath);
        Assert.DoesNotContain(handler.RequestUris, uri => uri.ToString().Contains("other-", StringComparison.Ordinal));
    }

    private static AppStoreConnectReviewWithdrawalRequest WithdrawalRequest() => new()
    {
        AppId = "app-1", VersionString = "2.2", BuildNumber = "28", Platform = ApplePlatform.macOS,
        ReviewSubmissionId = "submission-1"
    };

    private static SequenceResponse[] WithdrawalInventory(string versionState = "WAITING_FOR_REVIEW", string submissionState = "WAITING_FOR_REVIEW")
        => new[] { WithdrawalVersion(versionState), WithdrawalBuild(), WithdrawalSelectedBuild(), WithdrawalSubmission(submissionState), WithdrawalItems(), WithdrawalRelatedVersion(state: versionState) };

    private static SequenceResponse WithdrawalRelatedVersion(string id = "version-2", string appId = "app-1",
        string buildId = "build-28", string platform = "MAC_OS", string state = "WAITING_FOR_REVIEW")
        => WithdrawalResponse(new { data = new { id, type = "appStoreVersions", attributes = new { versionString = "2.2", appStoreState = state, platform },
            relationships = new { app = new { data = new { type = "apps", id = appId } }, build = new { data = new { type = "builds", id = buildId } } } } });

    private static SequenceResponse WithdrawalVersion(string state, string platform = "MAC_OS")
        => WithdrawalResponse(new { data = new[] { new { id = "version-2", type = "appStoreVersions", attributes = new { versionString = "2.2", appStoreState = state, platform } } } });

    private static SequenceResponse WithdrawalBuild() => WithdrawalResponse(new
    {
        data = new[] { new { id = "build-28", type = "builds", attributes = new { version = "28", processingState = "VALID" },
            relationships = new { preReleaseVersion = new { data = new { type = "preReleaseVersions", id = "train-1" } } } } },
        included = new[] { new { id = "train-1", type = "preReleaseVersions", attributes = new { version = "2.2", platform = "MAC_OS" } } }
    });

    private static SequenceResponse WithdrawalSelectedBuild(string id = "build-28")
        => WithdrawalResponse(new { data = new { type = "builds", id } });

    private static SequenceResponse WithdrawalSubmission(string state, bool single = false, string id = "submission-1")
    {
        var value = new { type = "reviewSubmissions", id, attributes = new { state, platform = "MAC_OS" } };
        return single ? WithdrawalResponse(new { data = value }) : WithdrawalResponse(new { data = new[] { value } });
    }

    private static object WithdrawalItem(string versionId, string state = "READY_FOR_REVIEW") => new
    {
        id = "item-1", type = "reviewSubmissionItems", attributes = new { state },
        relationships = new { appStoreVersion = new { data = new { type = "appStoreVersions", id = versionId } } }
    };

    private static SequenceResponse WithdrawalItems(string versionId = "version-2", bool additionalItem = false, string itemState = "READY_FOR_REVIEW")
        => WithdrawalResponse(new { data = additionalItem
            ? new[] { WithdrawalItem(versionId, itemState), new { id = "event-item", type = "reviewSubmissionItems" } }
            : new[] { WithdrawalItem(versionId, itemState) } });

    private static SequenceResponse WithdrawalResponse(object value) => new(HttpStatusCode.OK, JsonSerializer.Serialize(value));
}
