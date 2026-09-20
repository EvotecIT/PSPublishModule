using System.Net;
using System.Text;
using System.Text.Json;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class GitHubPullRequestFileTests
{
    private static readonly string Head = new('a', 40), Base = new('b', 40);

    [Theory]
    [InlineData("initial")]
    [InlineData("head")]
    [InlineData("base")]
    public async Task ChangedRevisionNeverReturnsPatches(string change)
    {
        var metadataReads = 0; var fileReads = 0;
        using var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/files")) { fileReads++; return Json("[]"); }
            metadataReads++;
            var head = change == "initial" || change == "head" && metadataReads > 1 ? new string('c', 40) : Head;
            var baseSha = change == "base" && metadataReads > 1 ? new string('c', 40) : Base;
            return Metadata(head, baseSha);
        });
        using var service = new GitHubProjectService(client);
        await Assert.ThrowsAsync<GitHubRevisionChangedException>(() => service.FetchPullRequestFilesAsync("owner/repo", 7, Head));
        Assert.Equal(change == "initial" ? 0 : 1, fileReads);
    }

    [Fact]
    public async Task FilesRetainRenameMissingPatchAndBoundedPreviewAndReportIncompleteCoverage()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/files")
            ? Json(JsonSerializer.Serialize(new object[] {
                new { filename = "new.cs", previous_filename = "old.cs", status = "renamed", additions = 2, deletions = 1, patch = "@@ -1 +1 @@\n-old\n+new" },
                new { filename = "binary.png", status = "modified", additions = 0, deletions = 0 },
                new { filename = "large.cs", status = "modified", additions = 1, deletions = 0, patch = new string('x', 262145) }
            })) : Metadata(Head, Base, 4));
        using var service = new GitHubProjectService(client);
        var result = await service.FetchPullRequestFilesAsync("owner/repo", 7, Head);
        Assert.Equal(Head, result.HeadSha); Assert.Equal(Base, result.BaseSha); Assert.True(result.Files.HasMore);
        Assert.Equal("old.cs", result.Files[0].PreviousPath); Assert.Contains("+new", result.Files[0].Patch);
        Assert.Null(result.Files[1].Patch); Assert.Contains("binary", result.Files[1].PatchNotice);
        Assert.Equal(262144, result.Files[2].Patch!.Length); Assert.True(result.Files[2].PatchTruncated);
    }

    private static HttpResponseMessage Metadata(string head, string baseSha, int total = 0) => Json(JsonSerializer.Serialize(new { head = new { sha = head }, @base = new { sha = baseSha }, changed_files = total }));
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Handler(response)) { BaseAddress = new("https://api.github.com") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
