using System.Diagnostics;

namespace PowerForge.Tests;

public sealed partial class CloudflareIncrementalCachePurgeTests
{
    [Fact]
    public void RepositoryArtifactLookup_ShouldSelectLatestNonExpiredArtifactAcrossBranches()
    {
        if (!CommandExists("pwsh"))
            return;

        var root = NewTempDirectory();
        try
        {
            const string artifactName = "powerforge-cloudflare-manifest-v2-42-site";
            var responsePath = Path.Combine(root, "response.json");
            File.WriteAllText(responsePath,
                $$"""
                {
                  "total_count": 4,
                  "artifacts": [
                    {
                      "id": 4,
                      "name": "different-artifact",
                      "expired": false,
                      "created_at": "2026-08-14T12:00:00Z",
                      "workflow_run": { "id": 404, "head_branch": "main" }
                    },
                    {
                      "id": 3,
                      "name": "{{artifactName}}",
                      "expired": true,
                      "created_at": "2026-08-14T11:00:00Z",
                      "workflow_run": { "id": 303, "head_branch": "main" }
                    },
                    {
                      "id": 1,
                      "name": "{{artifactName}}",
                      "expired": false,
                      "created_at": "2026-08-14T09:00:00Z",
                      "workflow_run": { "id": 101, "head_branch": "feature/cache" }
                    },
                    {
                      "id": 2,
                      "name": "{{artifactName}}",
                      "expired": false,
                      "created_at": "2026-08-14T10:00:00Z",
                      "workflow_run": { "id": 202, "head_branch": "main" }
                    }
                  ]
                }
                """);

            var result = RunRepositoryArtifactLookup(root, responsePath, artifactName);

            Assert.Equal("true", result["found"]);
            Assert.Equal("202", result["run_id"]);
            Assert.Equal("2", result["artifact_id"]);
            Assert.Equal("2026-08-14T10:00:00.0000000+00:00", result["created_at"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Dictionary<string, string> RunRepositoryArtifactLookup(
        string root,
        string responsePath,
        string artifactName)
    {
        var outputPath = Path.Combine(root, $"artifact-output-{Guid.NewGuid():N}.txt");
        var wrapperPath = Path.Combine(root, $"artifact-wrapper-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(wrapperPath,
            """
            $ErrorActionPreference = 'Stop'
            $global:PowerForgeTestArtifactResponse = Get-Content -LiteralPath $env:POWERFORGE_TEST_RESPONSE -Raw | ConvertFrom-Json
            function global:Invoke-RestMethod {
                param($Method, $Uri, $Headers)
                return $global:PowerForgeTestArtifactResponse
            }
            & $env:POWERFORGE_TEST_SCRIPT
            """);

        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(wrapperPath);
        startInfo.Environment["GITHUB_OUTPUT"] = outputPath;
        startInfo.Environment["POWERFORGE_ARTIFACT_NAME"] = artifactName;
        startInfo.Environment["POWERFORGE_GITHUB_API_URL"] = "https://api.github.test";
        startInfo.Environment["POWERFORGE_GITHUB_REPOSITORY"] = "EvotecIT/Example";
        startInfo.Environment["POWERFORGE_GITHUB_TOKEN"] = "test-token";
        startInfo.Environment["POWERFORGE_TEST_RESPONSE"] = responsePath;
        startInfo.Environment["POWERFORGE_TEST_SCRIPT"] = RepoPath(".github", "actions", "powerforge-cloudflare-site-policy", "Resolve-PowerForgeRepositoryArtifact.ps1");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start repository-artifact validation.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"Repository-artifact validation failed ({process.ExitCode}). stdout: {standardOutput} stderr: {standardError}");

        return File.ReadAllLines(outputPath)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }
}
