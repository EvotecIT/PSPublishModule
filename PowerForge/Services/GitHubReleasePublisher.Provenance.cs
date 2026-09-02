using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Threading;

namespace PowerForge;

public sealed partial class GitHubReleasePublisher {
    internal static void ValidateExpectedExistingRelease(
        string tagName,
        bool requireExpectedExistingRelease,
        long? expectedExistingReleaseId,
        long actualExistingReleaseId) {
        if (!requireExpectedExistingRelease) return;
        if (expectedExistingReleaseId.HasValue && expectedExistingReleaseId.Value == actualExistingReleaseId) return;

        throw new InvalidOperationException(
            $"GitHub release for tag '{tagName}' already exists, but release id {actualExistingReleaseId} was not preflight-verified for reuse.");
    }

    private static void ValidatePublishedStableRelease(
        string tagName,
        bool requirePublishedStableRelease,
        GitHubReleaseApiResponse release)
        => ValidatePublishedStableRelease(
            tagName,
            requirePublishedStableRelease,
            release.IsDraft,
            release.IsPreRelease,
            release.PublishedAt);

    internal static void ValidatePublishedStableRelease(
        string tagName,
        bool requirePublishedStableRelease,
        bool isDraft,
        bool isPreRelease,
        string? publishedAt) {
        if (!requirePublishedStableRelease) return;
        if (!isDraft && !isPreRelease && !string.IsNullOrWhiteSpace(publishedAt)) return;

        throw new InvalidOperationException(
            $"GitHub release for tag '{tagName}' is not a published stable release and cannot be reused for asset mutation.");
    }

    private void ValidateReleaseBeforeAssetMutation(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string tagName,
        string? expectedReleaseBodyMarker,
        string? expectedTagCommitSha,
        bool requirePublishedStableRelease,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(expectedReleaseBodyMarker) &&
            string.IsNullOrWhiteSpace(expectedTagCommitSha) &&
            !requirePublishedStableRelease) return;

        var currentRelease = GetReleaseByTag(owner, repo, token, apiBaseUrl, tagName, reusedExistingRelease: true, cancellationToken);
        var currentTagCommitSha = string.IsNullOrWhiteSpace(expectedTagCommitSha)
            ? null
            : GetTagCommitSha(owner, repo, token, apiBaseUrl, tagName, cancellationToken);
        ValidateExpectedReleaseState(
            tagName,
            releaseId,
            currentRelease.Id,
            currentRelease.Body,
            expectedReleaseBodyMarker,
            currentTagCommitSha,
            expectedTagCommitSha);
        ValidatePublishedStableRelease(tagName, requirePublishedStableRelease, currentRelease);
        _logger.Info($"Revalidated GitHub release {releaseId} and tag '{tagName}' immediately before asset mutation.");
    }

    internal static void ValidateExpectedReleaseState(
        string tagName,
        long expectedReleaseId,
        long actualReleaseId,
        string? actualReleaseBody,
        string? expectedReleaseBodyMarker,
        string? actualTagCommitSha,
        string? expectedTagCommitSha) {
        if (expectedReleaseId != actualReleaseId) {
            throw new InvalidOperationException(
                $"GitHub release for tag '{tagName}' changed from release id {expectedReleaseId} to {actualReleaseId} before asset mutation.");
        }

        if (!string.IsNullOrWhiteSpace(expectedReleaseBodyMarker) &&
            (actualReleaseBody?.IndexOf(expectedReleaseBodyMarker, StringComparison.Ordinal) ?? -1) < 0) {
            throw new InvalidOperationException(
                $"GitHub release {actualReleaseId} for tag '{tagName}' no longer contains the preflight-verified body marker.");
        }

        if (!string.IsNullOrWhiteSpace(expectedTagCommitSha) &&
            !string.Equals(actualTagCommitSha, expectedTagCommitSha, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"GitHub tag '{tagName}' changed from expected commit {expectedTagCommitSha} to {actualTagCommitSha ?? "<missing>"} before asset mutation.");
        }
    }

    private static string? GetTagCommitSha(string owner, string repo, string token, string apiBaseUrl, string tagName, CancellationToken cancellationToken) {
        var reference = GetGitObject(token, apiBaseUrl, $"/repos/{owner}/{repo}/git/ref/tags/{Uri.EscapeDataString(tagName)}", cancellationToken);
        var sha = reference.Sha;
        var type = reference.Type;
        for (var depth = 0; string.Equals(type, "tag", StringComparison.OrdinalIgnoreCase) && depth < 10; depth++) {
            var annotated = GetGitObject(token, apiBaseUrl, $"/repos/{owner}/{repo}/git/tags/{sha}", cancellationToken);
            sha = annotated.Sha;
            type = annotated.Type;
        }

        if (string.Equals(type, "tag", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"GitHub tag '{tagName}' exceeded the supported annotated-tag depth.");
        return string.IsNullOrWhiteSpace(sha) ? null : sha;
    }

    private static GitHubGitObjectResponse GetGitObject(string token, string apiBaseUrl, string path, CancellationToken cancellationToken) {
        var uri = BuildApiUri(apiBaseUrl, path);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        using (response) {
            if (!response.IsSuccessStatusCode)
                throw new GitHubApiRequestException(
                    $"GitHub tag provenance check failed ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}",
                    response.StatusCode,
                    GetAssetRetryAfterDelay(response, responseText));
        }

        var parsed = Deserialize<GitHubGitObjectEnvelope>(responseText);
        return parsed.Object ?? throw new InvalidOperationException("GitHub tag provenance response did not contain an object.");
    }

    [DataContract]
    private sealed class GitHubGitObjectEnvelope {
        [DataMember(Name = "object")]
        public GitHubGitObjectResponse? Object { get; set; }
    }

    [DataContract]
    private sealed class GitHubGitObjectResponse {
        [DataMember(Name = "sha")]
        public string? Sha { get; set; }

        [DataMember(Name = "type")]
        public string? Type { get; set; }
    }
}
