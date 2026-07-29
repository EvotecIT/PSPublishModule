namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void PublicModuleReleaseBindsSigningAndPublicationToExactMain()
    {
        var root = FindRepoRoot();
        var workflow = Read(root, ".github", "workflows", "pspublishmodule-public-release.yml");
        var script = Read(root, "Build", "Invoke-PowerForgePublicRelease.ps1");
        var releaseConfig = Read(root, "Build", "release.json");
        var releaseSchema = Read(root, "Schemas", "powerforge.release.schema.json");

        Assert.Contains("runs-on: [self-hosted, windows, runner-github-runner-w]", workflow, StringComparison.Ordinal);
        Assert.Contains("$mainCommit -ine $env:EXPECTED_COMMIT", workflow, StringComparison.Ordinal);
        Assert.Contains("Unable to refresh origin/main", workflow, StringComparison.Ordinal);
        Assert.Contains("permission -notin @('admin', 'maintain', 'write')", workflow, StringComparison.Ordinal);
        Assert.Contains("publish:<version>:<expected_commit>", workflow, StringComparison.Ordinal);
        Assert.Contains("$expectedConfirmation = \"publish:$Version`:$ExpectedCommit\"", script, StringComparison.Ordinal);
        Assert.Contains("-or -not $certificate.HasPrivateKey", script, StringComparison.Ordinal);
        Assert.Contains("$certificate.NotAfter -le [DateTime]::UtcNow.AddDays(7)", script, StringComparison.Ordinal);
        Assert.Contains("The release checkout must start clean", script, StringComparison.Ordinal);
        Assert.Contains("Assert-PowerForgeCommittedReleaseVersion", script, StringComparison.Ordinal);
        Assert.Contains("Set-PowerForgeAuthorizedReleaseVersion", script, StringComparison.Ordinal);
        Assert.Contains("-DisableVersionUpdates:($Operation -eq 'Publish')", script, StringComparison.Ordinal);
        Assert.Contains("Enable-PowerForgeVerifiedGitHubReleaseRecovery", script, StringComparison.Ordinal);
        Assert.Contains(". .\\Build\\Private\\Assert-PowerForgeCommittedReleaseVersion.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("$releaseConfig.GitHub | Add-Member -NotePropertyName Commitish", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedTagCommitSha = gitHub.Commitish", Read(root, "PowerForge", "Services", "PowerForgeReleaseService.cs"), StringComparison.Ordinal);
        Assert.Contains("Status         = 'Failed'", script, StringComparison.Ordinal);
        Assert.Contains("$release.isDraft -eq $true", workflow, StringComparison.Ordinal);
        Assert.Contains("$release.isPrerelease -eq $true", workflow, StringComparison.Ordinal);
        Assert.Contains("[string]::IsNullOrWhiteSpace([string] $release.publishedAt)", workflow, StringComparison.Ordinal);
        Assert.Contains("$tagCommit -ine $env:EXPECTED_COMMIT", workflow, StringComparison.Ordinal);
        Assert.Contains("Unable to push release branch", workflow, StringComparison.Ordinal);
        Assert.Contains("$remoteCommit -ine $preparedCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr list --repo $env:REPOSITORY --state all", workflow, StringComparison.Ordinal);
        Assert.Contains("git merge-base --is-ancestor $env:EXPECTED_COMMIT $remoteCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("git diff --quiet \"origin/$branch\" --", workflow, StringComparison.Ordinal);
        Assert.Contains("git branch -D $branch", workflow, StringComparison.Ordinal);
        Assert.Contains("url=$($existingPullRequest.url)", workflow, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgePublicRelease.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr merge", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request:", workflow, StringComparison.Ordinal);
        Assert.Contains("\"PlanOutputPath\": \"../Artefacts/ProjectBuild/project.build.plan.json\"", releaseConfig, StringComparison.Ordinal);
        Assert.Contains("\"Commitish\"", releaseSchema, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicModuleReleasePinsEffectiveConfigurationToAuthorizedVersion()
    {
        var root = FindRepoRoot();
        var helper = Path.Combine(root, "Build", "Private", "Set-PowerForgeAuthorizedReleaseVersion.ps1");
        var escapedHelper = helper.Replace("'", "''", StringComparison.Ordinal);
        var command = $". '{escapedHelper}'; " +
                      "$config = '{\"Module\":{\"ModuleVersion\":\"3.0.X\"},\"Packages\":{\"UpdateVersions\":true,\"VersionTracks\":{\"Main\":{\"ExpectedVersion\":\"3.0.X\"},\"Tools\":{\"ExpectedVersion\":\"4.0.X\"}}}}' | ConvertFrom-Json; " +
                      "$result = Set-PowerForgeAuthorizedReleaseVersion -ReleaseConfig $config -Version '3.0.81' -DisableVersionUpdates; " +
                      "if ($result.Module.ModuleVersion -ne '3.0.81') { throw 'Module version was not pinned.' }; " +
                      "if (@($result.Packages.VersionTracks.PSObject.Properties | Where-Object { $_.Value.ExpectedVersion -ne '3.0.81' }).Count -ne 0) { throw 'A package track was not pinned.' }; " +
                      "if ($result.Packages.UpdateVersions -ne $false) { throw 'Version mutation remained enabled.' }";

        Run("pwsh", root, "-NoProfile", "-Command", command).EnsureSuccess();
    }

    [Fact]
    public void PublicModuleReleaseEnablesOnlyExactVerifiedGitHubRecovery()
    {
        var root = FindRepoRoot();
        var helper = Path.Combine(root, "Build", "Private", "Enable-PowerForgeVerifiedGitHubReleaseRecovery.ps1");
        var escapedHelper = helper.Replace("'", "''", StringComparison.Ordinal);
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var command = $". '{escapedHelper}'; " +
                      "$config = '{\"GitHub\":{\"Publish\":true,\"Owner\":\"EvotecIT\",\"Repository\":\"PSPublishModule\",\"TagTemplate\":\"v{Version}\",\"ReuseExistingRelease\":false,\"ReplaceExistingAssets\":false}}' | ConvertFrom-Json; " +
                      "$releaseProbe = { [pscustomobject]@{ id = 42; tag_name = 'v3.0.81'; draft = $false; prerelease = $false; published_at = '2026-07-29T00:00:00Z' } }; " +
                      $"$tagProbe = {{ '{commit}' }}; " +
                      $"$result = Enable-PowerForgeVerifiedGitHubReleaseRecovery -ReleaseConfig $config -Version '3.0.81' -ExpectedCommit '{commit}' -Token 'test' -GetReleaseByTag $releaseProbe -GetTagCommit $tagProbe; " +
                      "if (-not $result.ReuseEnabled -or $result.ReleaseId -ne 42) { throw 'Verified recovery was not enabled.' }; " +
                      "if (-not $config.GitHub.ReuseExistingRelease -or -not $config.GitHub.ReplaceExistingAssets -or -not $config.GitHub.RequireExpectedExistingRelease -or $config.GitHub.ExpectedExistingReleaseId -ne 42 -or -not $config.GitHub.RequirePublishedStableRelease -or -not $config.GitHub.RequirePublishedNuGetAssets -or -not $config.GitHub.RequirePublishedModuleAssets -or $config.GitHub.PublishedModuleSource -ne 'https://www.powershellgallery.com/api/v2') { throw 'Effective recovery binding is incomplete.' }; " +
                      "$fresh = '{\"GitHub\":{\"Publish\":true,\"Owner\":\"EvotecIT\",\"Repository\":\"PSPublishModule\",\"TagTemplate\":\"v{Version}\",\"ReuseExistingRelease\":true,\"ReplaceExistingAssets\":true}}' | ConvertFrom-Json; " +
                      "$missingProbe = { $null }; " +
                      $"$freshResult = Enable-PowerForgeVerifiedGitHubReleaseRecovery -ReleaseConfig $fresh -Version '3.0.81' -ExpectedCommit '{commit}' -Token 'test' -GetReleaseByTag $missingProbe -GetTagCommit $missingProbe; " +
                      "if ($freshResult.ReuseEnabled -or $fresh.GitHub.ReuseExistingRelease -or $fresh.GitHub.ReplaceExistingAssets -or $fresh.GitHub.RequireExpectedExistingRelease -or $null -ne $fresh.GitHub.ExpectedExistingReleaseId -or $fresh.GitHub.RequirePublishedStableRelease -or $fresh.GitHub.RequirePublishedNuGetAssets -or $fresh.GitHub.RequirePublishedModuleAssets -or $null -ne $fresh.GitHub.PublishedModuleSource) { throw 'Fresh release did not remain non-reuse.' }";

        Run("pwsh", root, "-NoProfile", "-Command", command).EnsureSuccess();

        var mismatch = command.Replace(
            $"-ExpectedCommit '{commit}'",
            "-ExpectedCommit 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'",
            StringComparison.Ordinal);
        var mismatchResult = Run("pwsh", root, "-NoProfile", "-Command", mismatch);
        Assert.NotEqual(0, mismatchResult.ExitCode);
    }

    [Fact]
    public void PublicModuleReleaseRejectsPartiallyCommittedPackageVersions()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "PowerForge"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "PowerForge.Cli"));
            File.WriteAllText(Path.Combine(root.FullName, "Module", "PSPublishModule.psd1"), "@{ ModuleVersion = '3.0.81' }");
            File.WriteAllText(Path.Combine(root.FullName, "PowerForge", "PowerForge.csproj"),
                "<Project><PropertyGroup><VersionPrefix>3.0.81</VersionPrefix></PropertyGroup></Project>");
            var cliProject = Path.Combine(root.FullName, "PowerForge.Cli", "PowerForge.Cli.csproj");
            File.WriteAllText(cliProject,
                "<Project><PropertyGroup><VersionPrefix>3.0.81</VersionPrefix></PropertyGroup></Project>");

            var helper = Path.Combine(FindRepoRoot(), "Build", "Private", "Assert-PowerForgeCommittedReleaseVersion.ps1");
            var escapedHelper = helper.Replace("'", "''", StringComparison.Ordinal);
            var escapedRoot = root.FullName.Replace("'", "''", StringComparison.Ordinal);
            var command = $". '{escapedHelper}'; " +
                          "$config = '{\"Packages\":{\"VersionTracks\":{\"Train\":{\"AnchorProject\":\"PowerForge\",\"Projects\":[\"PowerForge.Cli\"]}}}}' | ConvertFrom-Json; " +
                          $"Assert-PowerForgeCommittedReleaseVersion -RepositoryRoot '{escapedRoot}' -Version '3.0.81' -ReleaseConfig $config";
            Run("pwsh", root.FullName, "-NoProfile", "-Command", command).EnsureSuccess();

            File.WriteAllText(cliProject,
                "<Project><PropertyGroup><VersionPrefix>3.0.80</VersionPrefix></PropertyGroup></Project>");
            var failure = Run("pwsh", root.FullName, "-NoProfile", "-Command", command);

            Assert.NotEqual(0, failure.ExitCode);
            Assert.Contains("Publish requires committed project version '3.0.81'", failure.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void PublicModuleReleaseWritesReceiptForPreflightFailure()
    {
        var root = FindRepoRoot();
        var receiptDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var receiptPath = Path.Combine(receiptDirectory.FullName, "failure.json");
            var result = Run(
                "pwsh",
                root,
                "-NoProfile",
                "-File",
                Path.Combine(root, "Build", "Invoke-PowerForgePublicRelease.ps1"),
                "-Operation",
                "Plan",
                "-Version",
                "3.0.81",
                "-ExpectedCommit",
                new string('0', 40),
                "-ReceiptPath",
                receiptPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(receiptPath));
            var receipt = File.ReadAllText(receiptPath);
            Assert.Contains("\"Status\": \"Failed\"", receipt, StringComparison.Ordinal);
            Assert.Contains("\"Stage\": \"Preflight\"", receipt, StringComparison.Ordinal);
            Assert.Contains("\"Operation\": \"Plan\"", receipt, StringComparison.Ordinal);
        }
        finally
        {
            receiptDirectory.Delete(recursive: true);
        }
    }
}
