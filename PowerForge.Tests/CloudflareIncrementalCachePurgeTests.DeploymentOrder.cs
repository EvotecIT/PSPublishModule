using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class CloudflareIncrementalCachePurgeTests
{
    [Fact]
    public void BaselineOrder_ShouldDetectInterveningDeploymentAcrossRerunAttempts()
    {
        if (!CommandExists("pwsh")) return;
        var root = NewTempDirectory();
        try
        {
            var files = WriteDeploymentOrderFixture(root,
                baselineRunId: 100, baselineAttempt: 1, baselineJobId: 1001,
                currentJobId: 1002,
                deployments:
                [
                    (30, 100, 1002, "2026-08-14T12:00:00Z"),
                    (20, 200, 2001, "2026-08-14T11:00:00Z"),
                    (10, 100, 1001, "2026-08-14T09:00:00Z")
                ]);

            var result = RunBaselineOrder(root, files, 100, 2, 100);
            Assert.Equal("false", result["stale"]);
            Assert.Equal("false", result["use_previous"]);
            Assert.Contains("intervening", result["reason"], StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BaselineOrder_ShouldUseDeploymentHistoryEvenWhenInterveningPolicyNeverRecordedState()
    {
        if (!CommandExists("pwsh")) return;
        var root = NewTempDirectory();
        try
        {
            var files = WriteDeploymentOrderFixture(root,
                100, 1, 1001, 3001,
                [(30, 300, 3001, "2026-08-14T11:00:00Z"), (20, 200, 2001, "2026-08-14T10:00:00Z"), (10, 100, 1001, "2026-08-14T09:00:00Z")]);

            var result = RunBaselineOrder(root, files, 300, 1, 100);
            Assert.Equal("false", result["use_previous"]);
            Assert.Contains("intervening", result["reason"], StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BaselineOrder_ShouldUseExactBaselineAndSkipAStalePolicyJob()
    {
        if (!CommandExists("pwsh")) return;
        var root = NewTempDirectory();
        try
        {
            var files = WriteDeploymentOrderFixture(root,
                100, 1, 1001, 3001,
                [(30, 300, 3001, "2026-08-14T11:00:00Z"), (10, 100, 1001, "2026-08-14T09:00:00Z")]);
            var ordered = RunBaselineOrder(root, files, 300, 1, 100);
            Assert.Equal("true", ordered["use_previous"]);

            AddDeployment(root, 40, 300, 3002, "2026-08-14T12:00:00Z");
            var stale = RunBaselineOrder(root, files, 300, 1, 100);
            Assert.Equal("true", stale["stale"]);
            Assert.Equal("false", stale["use_previous"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BaselineOrder_ShouldFailClosedUntilCurrentDeploymentIsIndexed()
    {
        if (!CommandExists("pwsh")) return;
        var root = NewTempDirectory();
        try
        {
            var files = WriteDeploymentOrderFixture(root,
                100, 1, 1001, 3001,
                [(10, 100, 1001, "2026-08-14T09:00:00Z")]);
            var failure = RunBaselineOrderProcess(root, files, 300, 1, 100);
            Assert.NotEqual(0, failure.ExitCode);
            Assert.Contains("does not yet identify", failure.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Dictionary<string, string> RunBaselineOrder(
        string root,
        DeploymentOrderFixture fixture,
        long deploymentRunId,
        int deploymentRunAttempt,
        long baselineArtifactRunId)
    {
        var result = RunBaselineOrderProcess(root, fixture, deploymentRunId, deploymentRunAttempt, baselineArtifactRunId);
        Assert.True(result.ExitCode == 0, $"Baseline-order validation failed ({result.ExitCode}). stdout: {result.StandardOutput} stderr: {result.StandardError}");

        return File.ReadAllLines(result.OutputPath)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }

    private static (int ExitCode, string StandardOutput, string StandardError, string OutputPath) RunBaselineOrderProcess(
        string root,
        DeploymentOrderFixture fixture,
        long deploymentRunId,
        int deploymentRunAttempt,
        long baselineArtifactRunId)
    {
        var outputPath = Path.Combine(root, $"output-{Guid.NewGuid():N}.txt");
        var wrapperPath = Path.Combine(root, $"deployment-wrapper-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(wrapperPath,
            """
            $ErrorActionPreference = 'Stop'
            function global:Invoke-RestMethod {
                param($Method, $Uri, $Headers)
                if ($Uri -match '/deployments/([0-9]+)/statuses') {
                    $path = Join-Path $env:POWERFORGE_TEST_ROOT "statuses-$($Matches[1]).json"
                } elseif ($Uri -match '/deployments\?') {
                    $path = Join-Path $env:POWERFORGE_TEST_ROOT 'deployments.json'
                } else {
                    throw "Unexpected GitHub test URI: $Uri"
                }
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing GitHub test response: $path" }
                $response = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                Write-Output -NoEnumerate $response
            }
            try {
                & $env:POWERFORGE_TEST_SCRIPT
            } catch {
                [Console]::Error.WriteLine($_.ScriptStackTrace)
                throw
            }
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
        startInfo.Environment["POWERFORGE_CLOUDFLARE_PREVIOUS_MANIFEST"] = fixture.PreviousManifest;
        startInfo.Environment["POWERFORGE_CLOUDFLARE_BASELINE_STATE"] = fixture.BaselineState;
        startInfo.Environment["POWERFORGE_BASELINE_ARTIFACT_RUN_ID"] = baselineArtifactRunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["POWERFORGE_GITHUB_API_URL"] = "https://api.github.test";
        startInfo.Environment["POWERFORGE_GITHUB_REPOSITORY"] = "EvotecIT/Example";
        startInfo.Environment["POWERFORGE_GITHUB_TOKEN"] = "test-token";
        startInfo.Environment["POWERFORGE_DEPLOYMENT_RUN_ID"] = deploymentRunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["POWERFORGE_DEPLOYMENT_RUN_ATTEMPT"] = deploymentRunAttempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["POWERFORGE_DEPLOYMENT_JOB_ID"] = fixture.CurrentJobId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["POWERFORGE_TEST_ROOT"] = root;
        startInfo.Environment["POWERFORGE_TEST_SCRIPT"] = RepoPath(".github", "actions", "powerforge-cloudflare-site-policy", "Resolve-PowerForgeCloudflareBaselineOrder.ps1");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start PowerShell baseline-order validation.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput, standardError, outputPath);
    }

    private static DeploymentOrderFixture WriteDeploymentOrderFixture(
        string root,
        long baselineRunId,
        int baselineAttempt,
        long baselineJobId,
        long currentJobId,
        (long Id, long RunId, long JobId, string DeployedAt)[] deployments)
    {
        var previousManifest = Path.Combine(root, "manifest.json");
        var baselineState = Path.Combine(root, "state.json");
        File.WriteAllText(previousManifest, "{}");
        File.WriteAllText(baselineState, $$"""{ "schemaVersion": 1, "deploymentRunId": "{{baselineRunId}}", "deploymentRunAttempt": {{baselineAttempt}}, "deploymentJobId": "{{baselineJobId}}" }""");
        File.WriteAllText(Path.Combine(root, "deployments.json"), JsonSerializer.Serialize(
            deployments.Select(item => new { id = item.Id, created_at = item.DeployedAt }).ToArray()));
        foreach (var item in deployments)
            WriteDeploymentStatus(root, item.Id, item.RunId, item.JobId, item.DeployedAt);
        return new DeploymentOrderFixture(previousManifest, baselineState, currentJobId);
    }

    private static void AddDeployment(string root, long id, long runId, long jobId, string deployedAt)
    {
        var path = Path.Combine(root, "deployments.json");
        var deployments = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        deployments.Insert(0, new JsonObject { ["id"] = id, ["created_at"] = deployedAt });
        File.WriteAllText(path, deployments.ToJsonString());
        WriteDeploymentStatus(root, id, runId, jobId, deployedAt);
    }

    private static void WriteDeploymentStatus(string root, long id, long runId, long jobId, string deployedAt)
    {
        File.WriteAllText(Path.Combine(root, $"statuses-{id}.json"), JsonSerializer.Serialize(new[]
        {
            new { state = "success", environment_url = "https://example.test/", log_url = $"https://github.test/EvotecIT/Example/actions/runs/{runId}/job/{jobId}", created_at = deployedAt }
        }));
    }

    private sealed record DeploymentOrderFixture(string PreviousManifest, string BaselineState, long CurrentJobId);
}
