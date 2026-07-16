using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;

namespace PowerForge;

public sealed partial class GitHubReleasePublisher {
    private void ValidateReleaseBeforeAssetMutation(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string tagName,
        string? expectedReleaseBodyMarker,
        string? expectedTagCommitSha) {
        if (string.IsNullOrWhiteSpace(expectedReleaseBodyMarker) && string.IsNullOrWhiteSpace(expectedTagCommitSha)) return;

        var currentRelease = GetReleaseByTag(owner, repo, token, apiBaseUrl, tagName, reusedExistingRelease: true);
        var currentTagCommitSha = string.IsNullOrWhiteSpace(expectedTagCommitSha)
            ? null
            : GetTagCommitSha(owner, repo, token, apiBaseUrl, tagName);
        ValidateExpectedReleaseState(
            tagName,
            releaseId,
            currentRelease.Id,
            currentRelease.Body,
            expectedReleaseBodyMarker,
            currentTagCommitSha,
            expectedTagCommitSha);
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

    private static string? GetTagCommitSha(string owner, string repo, string token, string apiBaseUrl, string tagName) {
        var reference = GetGitObject(token, apiBaseUrl, $"/repos/{owner}/{repo}/git/ref/tags/{Uri.EscapeDataString(tagName)}");
        var sha = reference.Sha;
        var type = reference.Type;
        for (var depth = 0; string.Equals(type, "tag", StringComparison.OrdinalIgnoreCase) && depth < 10; depth++) {
            var annotated = GetGitObject(token, apiBaseUrl, $"/repos/{owner}/{repo}/git/tags/{sha}");
            sha = annotated.Sha;
            type = annotated.Type;
        }

        if (string.Equals(type, "tag", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"GitHub tag '{tagName}' exceeded the supported annotated-tag depth.");
        return string.IsNullOrWhiteSpace(sha) ? null : sha;
    }

    private static GitHubGitObjectResponse GetGitObject(string token, string apiBaseUrl, string path) {
        var uri = BuildApiUri(apiBaseUrl, path);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = SharedClient.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        using (response) {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"GitHub tag provenance check failed ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
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
