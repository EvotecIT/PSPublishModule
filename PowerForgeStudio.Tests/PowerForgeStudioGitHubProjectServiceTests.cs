using System.Net;
using System.Net.Http;
using System.Text;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioGitHubProjectServiceTests
{
    [Fact]
    public async Task FetchIssuesAsync_ParsesBodyAndHtmlUrl_AndSkipsPullRequests()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json("""
[
  {
    "number": 17,
    "title": "Issue body",
    "state": "closed",
    "html_url": "https://github.com/EvotecIT/PSPublishModule/issues/17",
    "body": "## Summary\nIssue details",
    "user": { "login": "przemyslaw" },
    "labels": [{ "name": "bug" }],
    "assignees": [{ "login": "team" }],
    "created_at": "2026-03-18T10:00:00Z",
    "closed_at": "2026-03-19T11:15:00Z"
  },
  {
    "number": 18,
    "title": "PR masquerading as issue",
    "state": "open",
    "pull_request": {},
    "created_at": "2026-03-18T10:00:00Z"
  }
]
""")))
        {
            BaseAddress = new Uri("https://api.github.com")
        };

        using var service = new GitHubProjectService(client);
        var issues = await service.FetchIssuesAsync("EvotecIT/PSPublishModule", state: "closed");

        var issue = Assert.Single(issues);
        Assert.Equal(17, issue.Number);
        Assert.Equal("https://github.com/EvotecIT/PSPublishModule/issues/17", issue.HtmlUrl);
        Assert.Equal("## Summary\nIssue details", issue.BodyMarkdown);
        Assert.Equal("Closed", issue.StateDisplay);
        Assert.Equal("bug", issue.LabelDisplay);
        Assert.Equal("team", issue.AssigneeDisplay);
    }

    [Fact]
    public async Task FetchPullRequestsAsync_ParsesBodyAndHtmlUrl()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json("""
[
  {
    "number": 220,
    "title": "Add markdown preview",
    "state": "closed",
    "html_url": "https://github.com/EvotecIT/PSPublishModule/pull/220",
    "body": "Rendered through OfficeIMO",
    "user": { "login": "przemyslaw" },
    "head": { "ref": "feature/powerforge-studio-hub" },
    "base": { "ref": "main" },
    "labels": [{ "name": "enhancement" }],
    "mergeable_state": "clean",
    "additions": 42,
    "deletions": 7,
    "changed_files": 5,
    "created_at": "2026-03-18T10:00:00Z",
    "merged_at": "2026-03-19T11:15:00Z"
  }
]
""")))
        {
            BaseAddress = new Uri("https://api.github.com")
        };

        using var service = new GitHubProjectService(client);
        var prs = await service.FetchPullRequestsAsync("EvotecIT/PSPublishModule", state: "closed");

        var pr = Assert.Single(prs);
        Assert.Equal(220, pr.Number);
        Assert.Equal("https://github.com/EvotecIT/PSPublishModule/pull/220", pr.HtmlUrl);
        Assert.Equal("Rendered through OfficeIMO", pr.BodyMarkdown);
        Assert.Equal("Merged", pr.StateDisplay);
        Assert.Equal("enhancement", pr.LabelDisplay);
        Assert.Equal("Ready to merge", pr.MergeStatusDisplay);
    }

    [Fact]
    public async Task FetchIssueDetailAsync_ReturnsBodyAndDiscussionComments()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (path.Contains("/issues/17/timeline", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""
[
  {
    "id": 301,
    "event": "labeled",
    "actor": { "login": "maintainer" },
    "label": { "name": "bug" },
    "created_at": "2026-03-19T11:30:00Z"
  }
]
""");
            }

            if (path.Contains("/issues/17/comments", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""
[
  {
    "id": 101,
    "html_url": "https://github.com/EvotecIT/PSPublishModule/issues/17#issuecomment-101",
    "body": "First follow-up",
    "user": { "login": "reviewer" },
    "created_at": "2026-03-19T12:00:00Z"
  }
]
""");
            }

            return Json("""
{
  "number": 17,
  "title": "Issue body",
  "state": "closed",
  "html_url": "https://github.com/EvotecIT/PSPublishModule/issues/17",
  "body": "## Summary\nIssue details",
  "user": { "login": "przemyslaw" },
  "labels": [{ "name": "bug" }],
  "assignees": [{ "login": "team" }],
  "created_at": "2026-03-18T10:00:00Z",
  "closed_at": "2026-03-19T11:15:00Z"
}
""");
        }))
        {
            BaseAddress = new Uri("https://api.github.com")
        };

        using var service = new GitHubProjectService(client);
        var detail = await service.FetchIssueDetailAsync("EvotecIT/PSPublishModule", 17);

        Assert.NotNull(detail);
        Assert.Equal(17, detail.Issue.Number);
        var comment = Assert.Single(detail.Comments);
        Assert.Equal(GitHubDiscussionCommentKind.IssueComment, comment.Kind);
        Assert.Equal("reviewer", comment.AuthorLogin);
        Assert.Equal("First follow-up", comment.BodyMarkdown);
        var timeline = Assert.Single(detail.TimelineEvents);
        Assert.Equal("labeled", timeline.EventName);
        Assert.Contains("added label", timeline.Markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchPullRequestDetailAsync_ReturnsIssueAndReviewComments()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (path.Contains("/issues/220/timeline", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""
[
  {
    "id": 401,
    "event": "review_requested",
    "actor": { "login": "maintainer" },
    "requested_reviewer": { "login": "reviewer" },
    "created_at": "2026-03-19T11:00:00Z"
  },
  {
    "id": 402,
    "event": "reviewed",
    "actor": { "login": "reviewer" },
    "state": "approved",
    "body": "Looks good to me.",
    "created_at": "2026-03-19T12:15:00Z"
  },
  {
    "id": 403,
    "event": "merged",
    "actor": { "login": "przemyslaw" },
    "created_at": "2026-03-19T13:00:00Z"
  }
]
""");
            }

            if (path.Contains("/issues/220/comments", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""
[
  {
    "id": 201,
    "html_url": "https://github.com/EvotecIT/PSPublishModule/pull/220#issuecomment-201",
    "body": "Top-level PR comment",
    "user": { "login": "maintainer" },
    "created_at": "2026-03-19T12:00:00Z"
  }
]
""");
            }

            if (path.Contains("/pulls/220/comments", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""
[
  {
    "id": 202,
    "html_url": "https://github.com/EvotecIT/PSPublishModule/pull/220#discussion_r202",
    "body": "Inline review note",
    "path": "PowerForgeStudio.Wpf/MainWindow.xaml",
    "line": 571,
    "diff_hunk": "@@ -560,6 +571,8 @@",
    "pull_request_review_id": 55,
    "user": { "login": "reviewer" },
    "created_at": "2026-03-19T12:30:00Z"
  },
  {
    "id": 203,
    "html_url": "https://github.com/EvotecIT/PSPublishModule/pull/220#discussion_r203",
    "body": "Reply on the inline thread",
    "path": "PowerForgeStudio.Wpf/MainWindow.xaml",
    "in_reply_to_id": 202,
    "pull_request_review_id": 55,
    "user": { "login": "przemyslaw" },
    "created_at": "2026-03-19T12:45:00Z"
  }
]
""");
            }

            return Json("""
{
  "number": 220,
  "title": "Add markdown preview",
  "state": "closed",
  "html_url": "https://github.com/EvotecIT/PSPublishModule/pull/220",
  "body": "Rendered through OfficeIMO",
  "user": { "login": "przemyslaw" },
  "head": { "ref": "feature/powerforge-studio-hub" },
  "base": { "ref": "main" },
  "labels": [{ "name": "enhancement" }],
  "mergeable_state": "clean",
  "additions": 42,
  "deletions": 7,
  "changed_files": 5,
  "created_at": "2026-03-18T10:00:00Z",
  "merged_at": "2026-03-19T11:15:00Z"
}
""");
        }))
        {
            BaseAddress = new Uri("https://api.github.com")
        };

        using var service = new GitHubProjectService(client);
        var detail = await service.FetchPullRequestDetailAsync("EvotecIT/PSPublishModule", 220);

        Assert.NotNull(detail);
        Assert.Equal(220, detail.PullRequest.Number);
        Assert.Equal(3, detail.Comments.Count);
        Assert.Contains(detail.Comments, comment => comment.Kind == GitHubDiscussionCommentKind.IssueComment);
        Assert.Contains(detail.Comments, comment => comment.Kind == GitHubDiscussionCommentKind.PullRequestReviewComment
            && comment.Path == "PowerForgeStudio.Wpf/MainWindow.xaml");
        Assert.Contains(detail.Comments, comment => comment.ParentCommentId == 202);
        Assert.Equal(3, detail.TimelineEvents.Count);
        Assert.Contains(detail.TimelineEvents, timeline => timeline.EventName == "reviewed"
            && timeline.Markdown.Contains("approved", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detail.TimelineEvents, timeline => timeline.EventName == "merged");
    }

    [Fact]
    public void GitHubDiscussionMarkdownBuilder_MergesTimelineAndCommentsChronologically()
    {
        var issue = new GitHubIssue(
            Number: 17,
            Title: "Issue body",
            State: "open",
            AuthorLogin: "author",
            Labels: [],
            Assignees: [],
            CreatedAt: DateTimeOffset.Parse("2026-03-19T10:00:00Z"),
            ClosedAt: null,
            BodyMarkdown: "Issue body");
        var detail = new GitHubIssueDetail(
            issue,
            Comments:
            [
                new GitHubDiscussionComment(
                    101,
                    GitHubDiscussionCommentKind.IssueComment,
                    "reviewer",
                    "Follow-up comment",
                    DateTimeOffset.Parse("2026-03-19T12:00:00Z"))
            ],
            TimelineEvents:
            [
                new GitHubTimelineEvent(
                    201,
                    "labeled",
                    "maintainer",
                    DateTimeOffset.Parse("2026-03-19T11:00:00Z"),
                    "maintainer added label `bug`.")
            ]);

        var markdown = GitHubDiscussionMarkdownBuilder.BuildIssueThread(issue, detail);

        Assert.Contains("## Discussion", markdown);
        Assert.True(markdown.IndexOf("added label", StringComparison.Ordinal) < markdown.IndexOf("Follow-up comment", StringComparison.Ordinal));
    }

    [Fact]
    public void GitHubThreadEntryBuilder_GroupsPullRequestReviewRepliesIntoOneThread()
    {
        var pullRequest = new GitHubPullRequest(
            Number: 220,
            Title: "Add markdown preview",
            State: "open",
            AuthorLogin: "author",
            HeadBranch: "feature",
            BaseBranch: "main",
            ReviewStatus: GitHubPrReviewStatus.Pending,
            MergeStatus: GitHubPrMergeStatus.Clean,
            Labels: [],
            Additions: 1,
            Deletions: 0,
            ChangedFiles: 1,
            CreatedAt: DateTimeOffset.Parse("2026-03-19T10:00:00Z"),
            MergedAt: null,
            BodyMarkdown: "PR body");
        var detail = new GitHubPullRequestDetail(
            pullRequest,
            Comments:
            [
                new GitHubDiscussionComment(
                    202,
                    GitHubDiscussionCommentKind.PullRequestReviewComment,
                    "reviewer",
                    "Inline review note",
                    DateTimeOffset.Parse("2026-03-19T12:30:00Z"),
                    Path: "PowerForgeStudio.Wpf/MainWindow.xaml",
                    PullRequestReviewId: 55,
                    Line: 571,
                    DiffHunk: "@@ -560,6 +571,8 @@"),
                new GitHubDiscussionComment(
                    203,
                    GitHubDiscussionCommentKind.PullRequestReviewComment,
                    "przemyslaw",
                    "Reply on the inline thread",
                    DateTimeOffset.Parse("2026-03-19T12:45:00Z"),
                    Path: "PowerForgeStudio.Wpf/MainWindow.xaml",
                    ParentCommentId: 202,
                    PullRequestReviewId: 55)
            ],
            TimelineEvents: []);

        var entries = GitHubThreadEntryBuilder.BuildPullRequestEntries(pullRequest, detail);

        var reviewThread = Assert.Single(entries, entry => entry.Kind == GitHubThreadEntryKind.ReviewThread);
        Assert.Equal("Review thread", reviewThread.Title);
        Assert.Equal("reviewer", reviewThread.AuthorLogin);
        Assert.Equal("PowerForgeStudio.Wpf/MainWindow.xaml", reviewThread.Path);
        Assert.Contains("Inline review note", reviewThread.Markdown);
        Assert.Contains("Reply from przemyslaw", reviewThread.Markdown);
        Assert.Contains("@@ -560,6 +571,8 @@", reviewThread.Markdown);
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }
}
