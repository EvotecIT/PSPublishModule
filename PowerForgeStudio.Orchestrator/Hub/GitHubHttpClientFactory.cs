using PowerForge;
using System.Net.Http.Headers;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>
/// Shared GitHub API HTTP client setup. Reusable by GitHubProjectService,
/// GitHubInboxService, and any future GitHub-interacting service.
/// Token resolution chain: env vars → gh CLI → token file.
/// </summary>
public static class GitHubHttpClientFactory
{
    private static readonly Lazy<string?> Token = new(ResolveTokenCore);

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

    public static bool HasToken => !string.IsNullOrWhiteSpace(ResolveToken());

    internal static string? ResolveToken() => Token.Value;

    private static string? ResolveTokenCore()
    {
        // 1. Environment variables (explicit configuration)
        var token = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        token = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        // 2. gh CLI auth token (most users have this from `gh auth login`)
        token = TryGetGhCliToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        // 3. Known token file locations
        token = TryReadTokenFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "powerforge", "github-token"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        return null;
    }

    private static string? TryGetGhCliToken()
        => TryGetGhCliTokenAsync(new ProcessRunner()).GetAwaiter().GetResult();

    internal static async Task<string?> TryGetGhCliTokenAsync(IProcessRunner runner)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe"), "gh" }
            : new[] { "gh" };
        foreach (var candidate in candidates)
        {
            var request = new ProcessRunRequest(candidate, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ["auth", "token", "--hostname", "github.com"], TimeSpan.FromSeconds(5)) { MaxCapturedOutputCharacters = 8192 };
            try
            {
                var result = await runner.RunAsync(request).ConfigureAwait(false);
                if (result.Succeeded && !result.StandardOutputLimitExceeded && !string.IsNullOrWhiteSpace(result.StdOut))
                    return result.StdOut.Trim();
            }
            catch { /* Authentication discovery is optional; never surface captured credential output. */ }
        }
        return null;
    }

    private static string? TryReadTokenFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var reader = new StreamReader(path);
                var buffer = new char[8193];
                var count = reader.ReadBlock(buffer, 0, buffer.Length);
                if (count > 8192) return null;
                var content = new string(buffer, 0, count).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content;
                }
            }
        }
        catch
        {
            // File not readable
        }

        return null;
    }
}
