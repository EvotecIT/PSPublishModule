namespace PowerForge.Tests;

public sealed class AppleReleaseWorkflowTests
{
    [Fact]
    public void SetupActionRequiresExactVersionAndChecksum()
    {
        var root = FindRepoRoot();
        var action = Read(root, ".github", "actions", "setup-powerforge", "action.yml");
        var script = Read(root, ".github", "actions", "setup-powerforge", "Install-PinnedPowerForge.ps1");
        var schema = Read(root, "Schemas", "powerforge.tool.schema.json");

        Assert.Contains("manifest-path", action, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("checksum mismatch", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("^\\d+\\.\\d+\\.\\d+$", script, StringComparison.Ordinal);
        Assert.Contains("sha256", schema, StringComparison.Ordinal);
        Assert.Contains("releaseTag", schema, StringComparison.Ordinal);
        Assert.Contains("$manifest.releaseTag", script, StringComparison.Ordinal);
        Assert.Contains("$releaseTag = \"v$version\"", script, StringComparison.Ordinal);
        Assert.Contains("releases/download/$releaseTag/$assetName", script, StringComparison.Ordinal);
        Assert.Contains("unsupported characters", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latest", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MANIFEST_PATH: ${{ inputs.manifest-path }}", action, StringComparison.Ordinal);
        Assert.DoesNotContain("-ManifestPath '${{", action, StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneToolWrapperPinsAnExplicitReleaseVersion()
    {
        var root = FindRepoRoot();
        var wrapper = Read(root, "Build", "Build-PowerForge.ps1");
        var project = Read(root, "Build", "Build-Project.ps1");
        var cli = Read(root, "PowerForge.Cli", "Program.Command.Release.cs");

        Assert.Contains("[string] $Version", wrapper, StringComparison.Ordinal);
        Assert.Contains("$buildProjectParams.ReleaseVersion = $Version", wrapper, StringComparison.Ordinal);
        Assert.Contains("[string] $ReleaseVersion", project, StringComparison.Ordinal);
        Assert.Contains("--release-version", cli, StringComparison.Ordinal);
        Assert.Contains("request.ReleaseVersion", cli, StringComparison.Ordinal);
    }

    [Fact]
    public void AppleActionKeepsPlansAndConfirmedMutationExplicit()
    {
        var root = FindRepoRoot();
        var action = Read(root, ".github", "actions", "apple-release", "action.yml");
        var script = Read(root, ".github", "actions", "apple-release", "Invoke-PowerForgeAppleRelease.ps1");

        Assert.Contains("plan-only", action, StringComparison.Ordinal);
        Assert.Contains("confirm", action, StringComparison.Ordinal);
        Assert.Contains("if ($planOnly -and $confirm)", script, StringComparison.Ordinal);
        Assert.Contains("--confirm-apple-action", script, StringComparison.Ordinal);
        Assert.Contains("--summary", script, StringComparison.Ordinal);
        Assert.Contains("--output', 'json", script, StringComparison.Ordinal);
        Assert.Contains("instead of '$action'", script, StringComparison.Ordinal);
        Assert.Contains("marketing-version must use x.y.z", script, StringComparison.Ordinal);
        Assert.Contains("did not write its required receipt", script, StringComparison.Ordinal);
        Assert.Contains("IsPathRooted($projectRootSetting)", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("Write-ReleaseOutput -Name 'receipt-path'", StringComparison.Ordinal) <
            script.IndexOf("& $env:POWERFORGE_TOOL_PATH", StringComparison.Ordinal));
    }

    [Fact]
    public void VersionWorkflowCreatesOnlyAReviewedVersionPullRequest()
    {
        var root = FindRepoRoot();
        var workflow = Read(root, ".github", "workflows", "powerforge-apple-version-pr.yml");

        Assert.Contains("action: Version", workflow, StringComparison.Ordinal);
        Assert.Equal(2, Count(workflow, "uses: ./powerforge-shared/.github/actions/apple-release"));
        Assert.Contains("Tracked changes", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr create", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr list", workflow, StringComparison.Ordinal);
        Assert.Contains("Remote branch '$branch' exists with different release content", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr merge", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitAppReview", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseApprovedVersion", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvanceAndApprovalWorkflowsKeepReviewAndReleaseSeparated()
    {
        var root = FindRepoRoot();
        var advance = Read(root, ".github", "workflows", "powerforge-apple-advance.yml");
        var approval = Read(root, ".github", "workflows", "powerforge-apple-approval.yml");

        Assert.Equal(2, Count(advance, "action: Advance"));
        Assert.DoesNotContain("SubmitTestFlightReview", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitAppReview", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("action: Release", advance, StringComparison.Ordinal);
        Assert.Contains("environment: ${{ inputs.environment_name }}", approval, StringComparison.Ordinal);
        Assert.Contains("@('SubmitTestFlightReview', 'SubmitAppReview', 'Release')", approval, StringComparison.Ordinal);
        Assert.Contains("plan-only: \"true\"", approval, StringComparison.Ordinal);
        Assert.Contains("confirm: \"true\"", approval, StringComparison.Ordinal);
        Assert.Contains("^[0-9A-Fa-f]{40}$", advance, StringComparison.Ordinal);
        Assert.Contains("^[0-9A-Fa-f]{40}$", approval, StringComparison.Ordinal);
        Assert.Equal(1, Count(advance, "group: powerforge-apple-${{ github.repository }}"));
        Assert.Equal(1, Count(approval, "group: powerforge-apple-${{ github.repository }}"));
        Assert.Contains("${{ steps.plan.outputs.receipt-path }}", advance, StringComparison.Ordinal);
        Assert.Contains("${{ steps.advance.outputs.receipt-path }}", advance, StringComparison.Ordinal);
        Assert.Equal(2, Count(advance, "if-no-files-found: error"));
        Assert.Equal(2, Count(approval, "if-no-files-found: error"));
        Assert.Contains("-plan", advance, StringComparison.Ordinal);
        Assert.Contains("-actual", advance, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static int Count(string value, string search)
        => value.Split(search, StringSplitOptions.None).Length - 1;

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the PSPublishModule repository root.");
    }
}
