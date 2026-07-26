using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class GitHubServerRecoveryValidationSecurityTests
{
    [Fact]
    public void Validator_ShouldAcceptIgnoredUntrackedDeferredRepositorySecretTarget()
    {
        var result = RunDeferredSecretValidator(ignoreTarget: true, trackTarget: false);

        Assert.True(result.ExitCode == 0, result.AllOutput);
    }

    [Fact]
    public void Validator_ShouldRejectNonIgnoredDeferredRepositorySecretTarget()
    {
        var result = RunDeferredSecretValidator(ignoreTarget: false, trackTarget: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must be ignored for rerunnable recovery", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectTrackedDeferredRepositorySecretTarget()
    {
        var result = RunDeferredSecretValidator(ignoreTarget: true, trackTarget: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not be tracked", result.AllOutput, StringComparison.Ordinal);
    }

    private static ValidationResult RunDeferredSecretValidator(bool ignoreTarget, bool trackTarget)
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-deferred-source-security-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "caller");
        var engineRoot = Path.Combine(root, "engine");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(engineRoot);
        try
        {
            if (ignoreTarget)
                File.WriteAllText(Path.Combine(workspace, ".gitignore"), ".secret\n");
            if (trackTarget)
                File.WriteAllText(Path.Combine(workspace, ".secret"), "fixture\n");
            else
                File.WriteAllText(Path.Combine(workspace, "README.md"), "fixture\n");
            InitializeGitRepository(workspace);
            if (trackTarget)
            {
                RunProcess("git", workspace, "add", "-f", ".secret").EnsureSuccess();
                RunProcess("git", workspace, "commit", "-m", "Track secret fixture", "--quiet").EnsureSuccess();
            }
            var callerRef = RunProcess("git", workspace, "rev-parse", "HEAD").StandardOutput.Trim();
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(new
                {
                    repositories = new[]
                    {
                        new
                        {
                            url = "https://github.com/EvotecIT/ExampleSite.git",
                            path = "/srv/caller",
                            @ref = callerRef
                        }
                    },
                    paths = Array.Empty<object>(),
                    secrets = new[]
                    {
                        new
                        {
                            id = "repository-secret",
                            path = "/srv/caller/.secret",
                            restoreAfterRepositories = true
                        }
                    }
                }));
            var wrapperPath = Path.Combine(root, "invoke-validator.ps1");
            File.WriteAllText(wrapperPath, """
                param(
                    [Parameter(Mandatory)][string] $ValidatorPath,
                    [Parameter(Mandatory)][string] $ManifestPath,
                    [Parameter(Mandatory)][string] $Workspace,
                    [Parameter(Mandatory)][string] $EngineRoot,
                    [Parameter(Mandatory)][string] $VisudoPath
                )
                $ErrorActionPreference = 'Stop'
                $env:POWERFORGE_ENGINE_REF = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
                $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 100
                try {
                    & $ValidatorPath `
                        -Manifest $manifest `
                        -Workspace $Workspace `
                        -EngineRoot $EngineRoot `
                        -CallerRepository 'EvotecIT/ExampleSite' `
                        -EngineRepository 'EvotecIT/PSPublishModule' `
                        -CaptureUser 'powerforge-example-backup' `
                        -VisudoPath $VisudoPath
                } catch {
                    [Console]::Error.WriteLine($_.Exception.Message)
                    exit 1
                }
                """);
            var validatorPath = GetRepoPath(
                ".github", "actions", "powerforge-server-recovery-validate", "Assert-PowerForgeServerRecoverySources.ps1");
            var visudoPath = CreateVisudoStub(root, Path.Combine(root, "visudo.log"));
            return RunProcess(
                "pwsh",
                root,
                "-NoLogo", "-NoProfile", "-File", wrapperPath,
                "-ValidatorPath", validatorPath,
                "-ManifestPath", manifestPath,
                "-Workspace", workspace,
                "-EngineRoot", engineRoot,
                "-VisudoPath", visudoPath);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort test cleanup */ }
        }
    }
}
