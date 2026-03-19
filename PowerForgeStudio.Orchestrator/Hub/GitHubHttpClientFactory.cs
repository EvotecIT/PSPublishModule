using System.Diagnostics;
using System.Net.Http.Headers;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>
/// Shared GitHub API HTTP client setup. Reusable by GitHubProjectService,
/// GitHubInboxService, and any future GitHub-interacting service.
/// Token resolution chain: env vars → gh CLI → token file.
/// </summary>
public static class GitHubHttpClientFactory
{
    private static string? _cachedToken;
    private static bool _tokenResolved;

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

    internal static string? ResolveToken()
    {
        // Return cached result after first resolution
        if (_tokenResolved)
        {
            return _cachedToken;
        }

        _tokenResolved = true;

        // 1. Environment variables (explicit configuration)
        var token = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            _cachedToken = token;
            return token;
        }

        token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            _cachedToken = token;
            return token;
        }

        token = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            _cachedToken = token;
            return token;
        }

        // 2. gh CLI auth token (most users have this from `gh auth login`)
        token = TryGetGhCliToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            _cachedToken = token;
            return token;
        }

        // 3. Known token file locations
        token = TryReadTokenFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "powerforge", "github-token"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            _cachedToken = token;
            return token;
        }

        _cachedToken = null;
        return null;
    }

    private static string? TryGetGhCliToken()
    {
        // Try known install locations + PATH
        var candidates = new[]
        {
            @"C:\Program Files\GitHub CLI\gh.exe",
            @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            "gh"
        };

        foreach (var ghPath in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(ghPath)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("auth");
                psi.ArgumentList.Add("token");

                using var process = Process.Start(psi);
                if (process is null) continue;

                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output;
                }
            }
            catch
            {
                // This path didn't work, try next
            }
        }

        return null;
    }

    private static string? TryReadTokenFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var content = File.ReadAllText(path).Trim();
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
