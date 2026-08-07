using System.Text.Json;
using YamlDotNet.Serialization;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Theory]
    [InlineData("", "[]")]
    [InlineData(
        "failure",
        "[{\"severity\":\"error\",\"category\":\"transient\",\"code\":\"APPLE_TRANSIENT\",\"retryable\":true}]")]
    public void MonitorIncidentStepLeavesExistingIncidentUntouchedWithoutActionableAppleFailure(
        string doctorOutcome,
        string diagnostics)
    {
        if (!CommandExists("pwsh")) return;

        using var result = RunMonitorIncidentStep(
            doctorOutcome,
            diagnostics,
            "[[{\"number\":42,\"title\":\"Apple release monitor detected a failure\",\"body\":\"<!-- powerforge-apple-monitor-incident:v1 -->\"}]]");

        result.Process.EnsureSuccess();
        Assert.Contains("healthy=false", File.ReadAllText(result.OutputPath), StringComparison.Ordinal);
        Assert.DoesNotContain("issue\t", File.ReadAllText(result.GhLogPath), StringComparison.Ordinal);
    }

    [Fact]
    public void RateLimitFailureClassifiesAsRetryableAndLeavesExistingIncidentUntouched()
    {
        if (!CommandExists("pwsh")) return;

        var diagnostic = Assert.Single(AppleReleaseFailureClassifier.Classify(
            "App Store Connect error RATE_LIMIT_EXCEEDED"));
        Assert.Equal("APPLE_TRANSIENT", diagnostic.Code);
        Assert.True(diagnostic.Retryable);
        var diagnostics = JsonSerializer.Serialize(
            new[] { diagnostic },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var result = RunMonitorIncidentStep(
            "failure",
            diagnostics,
            "[[{\"number\":42,\"title\":\"Apple release monitor detected a failure\",\"body\":\"<!-- powerforge-apple-monitor-incident:v1 -->\"}]]");

        result.Process.EnsureSuccess();
        Assert.Contains("healthy=false", File.ReadAllText(result.OutputPath), StringComparison.Ordinal);
        Assert.DoesNotContain("issue\t", File.ReadAllText(result.GhLogPath), StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorIncidentStepCreatesIssueForNonRetryableAppleFailure()
    {
        if (!CommandExists("pwsh")) return;

        using var result = RunMonitorIncidentStep(
            "failure",
            "[{\"severity\":\"error\",\"category\":\"credential\",\"code\":\"APPLE_TEST_ACTIONABLE\",\"summary\":\"Credentials are invalid.\",\"action\":\"Repair credentials.\",\"retryable\":false}]",
            "[[]]");

        result.Process.EnsureSuccess();
        Assert.Contains("healthy=false", File.ReadAllText(result.OutputPath), StringComparison.Ordinal);
        var ghLog = File.ReadAllText(result.GhLogPath);
        Assert.Contains("issue\tcreate", ghLog, StringComparison.Ordinal);
        Assert.Contains("APPLE_TEST_ACTIONABLE", ghLog, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorIncidentStepClosesExistingIncidentOnlyAfterHealthyDoctorRun()
    {
        if (!CommandExists("pwsh")) return;

        using var result = RunMonitorIncidentStep(
            "success",
            "[{\"severity\":\"warning\",\"category\":\"observability\",\"code\":\"APPLE_TEST_WARNING\",\"retryable\":false}]",
            "[[{\"number\":42,\"title\":\"Apple release monitor detected a failure\",\"body\":\"<!-- powerforge-apple-monitor-incident:v1 -->\"}]]");

        result.Process.EnsureSuccess();
        Assert.Contains("healthy=true", File.ReadAllText(result.OutputPath), StringComparison.Ordinal);
        Assert.Contains("issue\tclose\t42", File.ReadAllText(result.GhLogPath), StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorWorkflowRetriesTransientPreflightAndToolBuildFailures()
    {
        var root = FindRepoRoot();
        var workflow = Read(root, ".github", "workflows", "powerforge-apple-monitor.yml");
        var buildAction = Read(root, ".github", "actions", "build-powerforge", "action.yml");

        Assert.Contains("$maximumAttempts = 3", workflow, StringComparison.Ordinal);
        Assert.Contains("HTTP\\s+(?:429|5\\d\\d)", workflow, StringComparison.Ordinal);
        Assert.Contains("Verifying the merged PowerForge monitor source", workflow, StringComparison.Ordinal);
        Assert.Contains("$maximumAttempts = 2", buildAction, StringComparison.Ordinal);
        Assert.Contains("MSBUILDDISABLENODEREUSE: \"1\"", buildAction, StringComparison.Ordinal);
        Assert.Contains("& pwsh @buildArguments", buildAction, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorPreflightRecoversFromTransientGitHubApiFailure()
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var script = ReadWorkflowStepScript(
            Path.Combine(root, ".github", "workflows", "powerforge-apple-monitor.yml"),
            "Require merged shared monitor source");
        var powerForgeRef = new string('b', 40);
        var command = $$$"""
            $global:GitHubApiCallCount = 0
            function global:gh {
              $global:GitHubApiCallCount++
              if ($global:GitHubApiCallCount -eq 1) {
                $global:LASTEXITCODE = 1
                Write-Output 'gh: Server Error: diff temporarily unavailable due to heavy server load. (HTTP 500)'
                return
              }

              $global:LASTEXITCODE = 0
              if (($args -join ' ') -match '/compare/') {
                Write-Output '{"merge_base_commit":{"sha":"{{{powerForgeRef}}}"}}'
              } else {
                Write-Output '{"default_branch":"main"}'
              }
            }

            {{{script}}}
            Write-Output "github-api-calls=$global:GitHubApiCallCount"
            """;
        var environment = new Dictionary<string, string?>
        {
            ["POWERFORGE_REF"] = powerForgeRef
        };

        var process = RunWithEnvironment("pwsh", root, environment, "-NoProfile", "-Command", command);

        process.EnsureSuccess();
        Assert.Contains("attempt 1 of 3", process.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("github-api-calls=3", process.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceBuildActionRetriesFailedChildBuildAndPublishesRecoveredTool()
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"powerforge-build-retry-{Guid.NewGuid():N}");
        try
        {
            var buildDirectory = Directory.CreateDirectory(Path.Combine(sandbox, "Build"));
            var fakeBuildScript = Path.Combine(buildDirectory.FullName, "Build-Project.ps1");
            File.WriteAllText(
                fakeBuildScript,
                """
                [CmdletBinding()]
                param(
                  [switch] $ToolsOnly,
                  [string[]] $Target,
                  [string[]] $Runtimes,
                  [string[]] $Frameworks,
                  [string[]] $Flavors,
                  [string] $Configuration
                )

                $counterPath = Join-Path $PSScriptRoot 'attempt.txt'
                $attempt = if (Test-Path -LiteralPath $counterPath) {
                  1 + [int] (Get-Content -LiteralPath $counterPath -Raw)
                } else {
                  1
                }
                Set-Content -LiteralPath $counterPath -Value $attempt -NoNewline
                if ($attempt -eq 1) { exit 1 }

                $sourceRoot = Split-Path -Parent $PSScriptRoot
                $toolPath = Join-Path $sourceRoot "Artifacts/PowerForge/$($Runtimes[0])/net10.0/SingleContained/PowerForge"
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $toolPath) | Out-Null
                Set-Content -LiteralPath $toolPath -Value 'recovered-tool' -NoNewline
                exit 0
                """);
            var outputPath = Path.Combine(sandbox, "github-output.txt");
            var script = ReadCompositeActionStepScript(
                Path.Combine(root, ".github", "actions", "build-powerforge", "action.yml"),
                "Build immutable standalone PowerForge");
            var environment = new Dictionary<string, string?>
            {
                ["SOURCE_ROOT"] = sandbox,
                ["TOOL_RUNTIME"] = "osx-arm64",
                ["GITHUB_OUTPUT"] = outputPath
            };

            var process = RunWithEnvironment("pwsh", sandbox, environment, "-NoProfile", "-Command", script);

            process.EnsureSuccess();
            Assert.Equal("2", File.ReadAllText(Path.Combine(buildDirectory.FullName, "attempt.txt")));
            Assert.Contains("tool-path=", File.ReadAllText(outputPath), StringComparison.Ordinal);
            Assert.Contains("retrying in 5 seconds", process.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    private static MonitorIncidentStepResult RunMonitorIncidentStep(
        string doctorOutcome,
        string diagnostics,
        string openIssuesJson)
    {
        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"apple-monitor-incident-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        var outputPath = Path.Combine(sandbox, "github-output.txt");
        var ghLogPath = Path.Combine(sandbox, "gh.log");
        var incidentScript = ReadWorkflowStepScript(
            Path.Combine(root, ".github", "workflows", "powerforge-apple-monitor.yml"),
            "Open or close the proactive Apple incident");
        var command = $$"""
            function global:gh {
              Add-Content -LiteralPath $env:GH_FAKE_LOG -Value ($args -join "`t")
              $global:LASTEXITCODE = 0
              if ($args.Count -gt 0 -and $args[0] -eq 'api') {
                Write-Output $env:GH_FAKE_OPEN_ISSUES
              }
            }

            {{incidentScript}}
            """;
        var environment = new Dictionary<string, string?>
        {
            ["GH_REPO"] = "EvotecIT/Tactra",
            ["ISSUE_TITLE"] = "Apple release monitor detected a failure",
            ["DOCTOR_OUTCOME"] = doctorOutcome,
            ["STRUCTURED_DIAGNOSTICS"] = diagnostics,
            ["RECEIPT_PATH"] = Path.Combine(sandbox, "missing-receipt.json"),
            ["RUN_URL"] = "https://github.com/EvotecIT/Tactra/actions/runs/123",
            ["SOURCE_REF"] = new string('a', 40),
            ["GITHUB_OUTPUT"] = outputPath,
            ["GH_FAKE_LOG"] = ghLogPath,
            ["GH_FAKE_OPEN_ISSUES"] = openIssuesJson
        };
        var process = RunWithEnvironment("pwsh", sandbox, environment, "-NoProfile", "-Command", command);
        return new MonitorIncidentStepResult(process, sandbox, outputPath, ghLogPath);
    }

    private static string ReadWorkflowStepScript(string workflowPath, string stepName)
    {
        var document = new DeserializerBuilder().Build().Deserialize<IDictionary<object, object>>(
            File.ReadAllText(workflowPath));
        var jobs = Assert.IsAssignableFrom<IDictionary<object, object>>(document["jobs"]);
        var doctor = Assert.IsAssignableFrom<IDictionary<object, object>>(jobs["doctor"]);
        var steps = Assert.IsAssignableFrom<IEnumerable<object>>(doctor["steps"]);
        foreach (var stepValue in steps)
        {
            if (stepValue is not IDictionary<object, object> step ||
                !step.TryGetValue("name", out var name) ||
                !string.Equals(name as string, stepName, StringComparison.Ordinal))
            {
                continue;
            }

            return Assert.IsType<string>(step["run"]);
        }

        throw new InvalidOperationException($"Workflow step '{stepName}' was not found in '{workflowPath}'.");
    }

    private static string ReadCompositeActionStepScript(string actionPath, string stepName)
    {
        var document = new DeserializerBuilder().Build().Deserialize<IDictionary<object, object>>(
            File.ReadAllText(actionPath));
        var runs = Assert.IsAssignableFrom<IDictionary<object, object>>(document["runs"]);
        var steps = Assert.IsAssignableFrom<IEnumerable<object>>(runs["steps"]);
        foreach (var stepValue in steps)
        {
            if (stepValue is not IDictionary<object, object> step ||
                !step.TryGetValue("name", out var name) ||
                !string.Equals(name as string, stepName, StringComparison.Ordinal))
            {
                continue;
            }

            return Assert.IsType<string>(step["run"]);
        }

        throw new InvalidOperationException($"Action step '{stepName}' was not found in '{actionPath}'.");
    }

    private sealed record MonitorIncidentStepResult(
        ProcessResult Process,
        string Sandbox,
        string OutputPath,
        string GhLogPath) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(Sandbox))
                Directory.Delete(Sandbox, recursive: true);
        }
    }
}
