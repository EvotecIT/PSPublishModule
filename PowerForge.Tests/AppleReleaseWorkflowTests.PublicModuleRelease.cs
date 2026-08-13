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
        var moduleConfig = Read(root, "powerforge.json");
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
        Assert.Contains("status --porcelain --untracked-files=all", script, StringComparison.Ordinal);
        Assert.Contains("[IO.Path]::GetTempPath()", script, StringComparison.Ordinal);
        Assert.Contains("Assert-PowerForgeCommittedReleaseVersion", script, StringComparison.Ordinal);
        Assert.Contains("Set-PowerForgeAuthorizedReleaseVersion", script, StringComparison.Ordinal);
        Assert.Contains("-DisableVersionUpdates:($Operation -eq 'Publish')", script, StringComparison.Ordinal);
        Assert.Contains("Enable-PowerForgeVerifiedGitHubReleaseRecovery", script, StringComparison.Ordinal);
        Assert.Contains("Get-PowerForgeReleasePackageIds", script, StringComparison.Ordinal);
        Assert.Contains("PowerForge.ReleaseProvenance.json", script, StringComparison.Ordinal);
        Assert.Contains("PowerForge.ReleaseProvenance.psd1", script, StringComparison.Ordinal);
        Assert.Contains("moduleName    = [string] $releaseConfig.Module.ModuleName", script, StringComparison.Ordinal);
        Assert.Contains("commit        = $ExpectedCommit", script, StringComparison.Ordinal);
        Assert.Contains("sourceDirty   = $sourceDirty", script, StringComparison.Ordinal);
        Assert.Contains("Get-PowerForgeReleaseSourceState", script, StringComparison.Ordinal);
        Assert.Contains("$moduleProvenanceCreated", script, StringComparison.Ordinal);
        Assert.Contains("$moduleSignedProvenanceCreated", script, StringComparison.Ordinal);
        Assert.Contains("ReceiptPath must stay outside the release checkout", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("Get-PowerForgeReleaseSourceState", StringComparison.Ordinal) <
            script.IndexOf("New-Item -ItemType Directory -Path $receiptDirectory", StringComparison.Ordinal),
            "Release receipts must not be created until the checkout is proven clean.");
        Assert.Contains("\"PowerForge.ReleaseProvenance.json\"", moduleConfig, StringComparison.Ordinal);
        Assert.Contains("\"PowerForge.ReleaseProvenance.psd1\"", moduleConfig, StringComparison.Ordinal);
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
    public void PublicModuleReleaseSourceStateExcludesPriorDefaultReceiptButNotTrackedOrUntrackedInputs()
    {
        var repository = Directory.CreateTempSubdirectory();
        try
        {
            Run("git", repository.FullName, "init").EnsureSuccess();
            Run("git", repository.FullName, "config", "user.email", "powerforge-tests@example.invalid").EnsureSuccess();
            Run("git", repository.FullName, "config", "user.name", "PowerForge Tests").EnsureSuccess();
            Directory.CreateDirectory(Path.Combine(repository.FullName, "Module"));
            File.WriteAllText(Path.Combine(repository.FullName, "tracked.txt"), "tracked");
            Run("git", repository.FullName, "add", "tracked.txt").EnsureSuccess();
            Run("git", repository.FullName, "commit", "-m", "fixture").EnsureSuccess();

            string provenance = Path.Combine(repository.FullName, "Module", "PowerForge.ReleaseProvenance.json");
            File.WriteAllText(provenance, "{}");
            string helper = Path.Combine(FindRepoRoot(), "Build", "Private", "Get-PowerForgeReleaseSourceState.ps1");
            string command = $". '{helper.Replace("'", "''", StringComparison.Ordinal)}'; " +
                             $"Get-PowerForgeReleaseSourceState -RepositoryRoot '{repository.FullName.Replace("'", "''", StringComparison.Ordinal)}' -GeneratedProvenancePath '{provenance.Replace("'", "''", StringComparison.Ordinal)}' | ConvertTo-Json -Compress";
            var clean = Run("pwsh", repository.FullName, "-NoProfile", "-Command", command).EnsureSuccess();
            Assert.Contains("\"SourceDirty\":false", clean.StandardOutput, StringComparison.OrdinalIgnoreCase);

            Directory.CreateDirectory(Path.Combine(repository.FullName, "release-receipts"));
            string defaultReceipt = Path.Combine(repository.FullName, "release-receipts", "powerforge-public-release.json");
            File.WriteAllText(defaultReceipt, "{}");
            var receiptDirty = Run("pwsh", repository.FullName, "-NoProfile", "-Command", command).EnsureSuccess();
            Assert.Contains("\"SourceDirty\":false", receiptDirty.StandardOutput, StringComparison.OrdinalIgnoreCase);

            Run("git", repository.FullName, "add", "release-receipts/powerforge-public-release.json").EnsureSuccess();
            Run("git", repository.FullName, "commit", "-m", "tracked receipt fixture").EnsureSuccess();
            File.WriteAllText(defaultReceipt, "modified tracked receipt");
            var trackedReceiptDirty = Run("pwsh", repository.FullName, "-NoProfile", "-Command", command).EnsureSuccess();
            Assert.Contains("\"SourceDirty\":true", trackedReceiptDirty.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("powerforge-public-release.json", trackedReceiptDirty.StandardOutput, StringComparison.Ordinal);
            File.WriteAllText(defaultReceipt, "{}");

            File.WriteAllText(Path.Combine(repository.FullName, "Module", "untracked-input.ps1"), "'input'");
            var dirty = Run("pwsh", repository.FullName, "-NoProfile", "-Command", command).EnsureSuccess();
            Assert.Contains("\"SourceDirty\":true", dirty.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("untracked-input.ps1", dirty.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            try { repository.Delete(recursive: true); } catch { }
        }
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
                      "$repositoryProbe = { [pscustomobject]@{ permissions = [pscustomobject]@{ push = $true; admin = $false } } }; " +
                      "$releaseProbe = { [pscustomobject]@{ id = 42; tag_name = 'v3.0.81'; draft = $false; prerelease = $false; published_at = '2026-07-29T00:00:00Z' } }; " +
                      $"$tagProbe = {{ '{commit}' }}; " +
                      $"$result = Enable-PowerForgeVerifiedGitHubReleaseRecovery -ReleaseConfig $config -Version '3.0.81' -ExpectedCommit '{commit}' -Token 'test' -GetRepository $repositoryProbe -GetReleaseByTag $releaseProbe -GetTagCommit $tagProbe; " +
                      "if (-not $result.ReuseEnabled -or $result.ReleaseId -ne 42) { throw 'Verified recovery was not enabled.' }; " +
                      "if (-not $config.GitHub.ReuseExistingRelease -or -not $config.GitHub.ReplaceExistingAssets -or -not $config.GitHub.RequireExpectedExistingRelease -or $config.GitHub.ExpectedExistingReleaseId -ne 42 -or -not $config.GitHub.RequirePublishedStableRelease -or -not $config.GitHub.RequirePublishedNuGetAssets -or -not $config.GitHub.RequirePublishedModuleAssets -or $config.GitHub.PublishedModuleSource -ne 'https://www.powershellgallery.com/api/v2') { throw 'Effective recovery binding is incomplete.' }; " +
                      "$fresh = '{\"GitHub\":{\"Publish\":true,\"Owner\":\"EvotecIT\",\"Repository\":\"PSPublishModule\",\"TagTemplate\":\"v{Version}\",\"ReuseExistingRelease\":true,\"ReplaceExistingAssets\":true}}' | ConvertFrom-Json; " +
                      "$missingProbe = { $null }; " +
                      "$emptyRegistry = { [pscustomobject]@{ AnyPublished = $false; PublishedPackageIds = @(); ModulePublished = $false } }; " +
                      $"$freshResult = Enable-PowerForgeVerifiedGitHubReleaseRecovery -ReleaseConfig $fresh -Version '3.0.81' -ExpectedCommit '{commit}' -Token 'test' -PackageIds @('PowerForge') -GetRepository $repositoryProbe -GetReleaseByTag $missingProbe -GetTagCommit $missingProbe -GetRegistryState $emptyRegistry; " +
                      "if ($freshResult.ReuseEnabled -or -not $freshResult.RegistryRecovery -or $fresh.GitHub.ReuseExistingRelease -or $fresh.GitHub.ReplaceExistingAssets -or $fresh.GitHub.RequireExpectedExistingRelease -or $null -ne $fresh.GitHub.ExpectedExistingReleaseId -or $fresh.GitHub.RequirePublishedStableRelease -or -not $fresh.GitHub.RequirePublishedNuGetAssets -or -not $fresh.GitHub.RequirePublishedModuleAssets -or $fresh.GitHub.PublishedModuleSource -ne 'https://www.powershellgallery.com/api/v2' -or -not $fresh.GitHub.RecoverPublishedRegistryAssetsBeforeGitHubRelease -or $fresh.GitHub.PublishedModuleAlreadyExists) { throw 'Fresh release did not enable exact post-publication recovery.' }; " +
                      "$partial = '{\"GitHub\":{\"Publish\":true,\"Owner\":\"EvotecIT\",\"Repository\":\"PSPublishModule\",\"TagTemplate\":\"v{Version}\"}}' | ConvertFrom-Json; " +
                      "$partialRegistry = { [pscustomobject]@{ AnyPublished = $true; PublishedPackageIds = @('PowerForge'); ModulePublished = $true; ProvenanceVerified = $true } }; " +
                      $"$partialResult = Enable-PowerForgeVerifiedGitHubReleaseRecovery -ReleaseConfig $partial -Version '3.0.81' -ExpectedCommit '{commit}' -Token 'test' -PackageIds @('PowerForge','PowerForge.Build') -GetRepository $repositoryProbe -GetReleaseByTag $missingProbe -GetTagCommit $missingProbe -GetRegistryState $partialRegistry; " +
                      "if ($partialResult.ReuseEnabled -or -not $partialResult.RegistryRecovery -or $partial.GitHub.ReuseExistingRelease -or $partial.GitHub.ReplaceExistingAssets -or -not $partial.GitHub.RequirePublishedNuGetAssets -or -not $partial.GitHub.RequirePublishedModuleAssets -or -not $partial.GitHub.RecoverPublishedRegistryAssetsBeforeGitHubRelease -or -not $partial.GitHub.PublishedModuleAlreadyExists) { throw 'Partial registry recovery binding is incomplete.' }";

        Run("pwsh", root, "-NoProfile", "-Command", command).EnsureSuccess();

        var mismatch = command.Replace(
            $"-ExpectedCommit '{commit}'",
            "-ExpectedCommit 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'",
            StringComparison.Ordinal);
        var mismatchResult = Run("pwsh", root, "-NoProfile", "-Command", mismatch);
        Assert.NotEqual(0, mismatchResult.ExitCode);

        var foreignRegistry = command.Replace(
            "ProvenanceVerified = $true",
            "ProvenanceVerified = $false",
            StringComparison.Ordinal);
        var foreignRegistryResult = Run("pwsh", root, "-NoProfile", "-Command", foreignRegistry);
        Assert.NotEqual(0, foreignRegistryResult.ExitCode);
    }

    [Fact]
    public void PublicModuleReleaseResolvesEveryConfiguredPackageId()
    {
        var root = FindRepoRoot();
        var helper = Path.Combine(root, "Build", "Private", "Get-PowerForgeReleasePackageIds.ps1");
        var escapedHelper = helper.Replace("'", "''", StringComparison.Ordinal);
        var escapedConfig = Path.Combine(root, "Build", "release.json").Replace("'", "''", StringComparison.Ordinal);
        var escapedRoot = root.Replace("'", "''", StringComparison.Ordinal);
        var command = $". '{escapedHelper}'; " +
                      $"$config = Get-Content -Raw -LiteralPath '{escapedConfig}' | ConvertFrom-Json; " +
                      $"$ids = @(Get-PowerForgeReleasePackageIds -ReleaseConfig $config -RepositoryRoot '{escapedRoot}'); " +
                      "$expected = @('PowerForge','PowerForge.PowerShell','PowerForge.Build','PowerForge.Blazor','PowerForge.Web','PowerForge.Web.Build'); " +
                      "if (Compare-Object $expected $ids) { throw \"Resolved package IDs differ: $($ids -join ', ')\" }";

        Run("pwsh", root, "-NoProfile", "-Command", command).EnsureSuccess();
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
        var repository = Directory.CreateTempSubdirectory();
        var receiptDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string buildDirectory = Path.Combine(repository.FullName, "Build");
            string privateDirectory = Path.Combine(buildDirectory, "Private");
            Directory.CreateDirectory(privateDirectory);
            File.Copy(
                Path.Combine(root, "Build", "Invoke-PowerForgePublicRelease.ps1"),
                Path.Combine(buildDirectory, "Invoke-PowerForgePublicRelease.ps1"));
            File.Copy(
                Path.Combine(root, "Build", "release.json"),
                Path.Combine(buildDirectory, "release.json"));
            File.Copy(
                Path.Combine(root, "Build", "Private", "Get-PowerForgeReleaseSourceState.ps1"),
                Path.Combine(privateDirectory, "Get-PowerForgeReleaseSourceState.ps1"));
            Run("git", repository.FullName, "init").EnsureSuccess();
            Run("git", repository.FullName, "config", "user.email", "powerforge-tests@example.invalid").EnsureSuccess();
            Run("git", repository.FullName, "config", "user.name", "PowerForge Tests").EnsureSuccess();
            Run("git", repository.FullName, "add", ".").EnsureSuccess();
            Run("git", repository.FullName, "commit", "-m", "fixture").EnsureSuccess();

            var receiptPath = Path.Combine(receiptDirectory.FullName, "failure.json");
            var result = Run(
                "pwsh",
                repository.FullName,
                "-NoProfile",
                "-File",
                Path.Combine(buildDirectory, "Invoke-PowerForgePublicRelease.ps1"),
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
            try { repository.Delete(recursive: true); } catch { }
            try { receiptDirectory.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void PublicModuleReleaseRejectsReceiptPathThatCouldOverwriteTrackedInput()
    {
        var sourceRoot = FindRepoRoot();
        var repository = Directory.CreateTempSubdirectory();
        try
        {
            string buildDirectory = Path.Combine(repository.FullName, "Build");
            Directory.CreateDirectory(buildDirectory);
            string scriptPath = Path.Combine(buildDirectory, "Invoke-PowerForgePublicRelease.ps1");
            string trackedInputPath = Path.Combine(buildDirectory, "release.json");
            File.Copy(Path.Combine(sourceRoot, "Build", "Invoke-PowerForgePublicRelease.ps1"), scriptPath);
            File.WriteAllText(trackedInputPath, "tracked-input");
            Run("git", repository.FullName, "init").EnsureSuccess();
            Run("git", repository.FullName, "config", "user.email", "powerforge-tests@example.invalid").EnsureSuccess();
            Run("git", repository.FullName, "config", "user.name", "PowerForge Tests").EnsureSuccess();
            Run("git", repository.FullName, "add", ".").EnsureSuccess();
            Run("git", repository.FullName, "commit", "-m", "fixture").EnsureSuccess();

            var result = Run(
                "pwsh",
                repository.FullName,
                "-NoProfile",
                "-File",
                scriptPath,
                "-Operation",
                "Plan",
                "-Version",
                "3.0.81",
                "-ExpectedCommit",
                new string('0', 40),
                "-ReceiptPath",
                trackedInputPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal("tracked-input", File.ReadAllText(trackedInputPath));
            Assert.Contains("dedicated release-receipts", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { repository.Delete(recursive: true); } catch { }
        }
    }
}
