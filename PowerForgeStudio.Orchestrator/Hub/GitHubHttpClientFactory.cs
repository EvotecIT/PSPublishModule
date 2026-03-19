using System.Net.Http.Headers;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>
/// Shared GitHub API HTTP client setup. Reusable by GitHubProjectService,
/// GitHubInboxService, and any future GitHub-interacting service.
/// </summary>
public static class GitHubHttpClientFactory
{
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com"),
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForgeStudio/0.1");

        var token = ResolveToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return httpClient;
    }

    public static string? ResolveToken()
    {
        var token = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token)) return token;

        token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token)) return token;

        token = Environment.GetEnvironmentVariable("GH_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
