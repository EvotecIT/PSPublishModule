using System.Collections;
using System.Diagnostics;
using System.Management.Automation.Language;
using YamlDotNet.Serialization;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
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
        var trackedInputs = Read(root, ".github", "actions", "apple-release", "Assert-TrackedAppleReleaseInputs.ps1");

        Assert.Contains("plan-only", action, StringComparison.Ordinal);
        Assert.Contains("confirm", action, StringComparison.Ordinal);
        Assert.Contains("plan-sha256", action, StringComparison.Ordinal);
        Assert.Contains("expected-plan-sha256", action, StringComparison.Ordinal);
        Assert.Contains("Assert-TrackedAppleReleaseInputs.ps1", action, StringComparison.Ordinal);
        Assert.Contains("valid exact plan SHA-256", script, StringComparison.Ordinal);
        Assert.Contains("FileAttributes]::ReparsePoint", trackedInputs, StringComparison.Ordinal);
        Assert.Contains("ls-files --error-unmatch", trackedInputs, StringComparison.Ordinal);
        Assert.Contains("diff --quiet HEAD", trackedInputs, StringComparison.Ordinal);
        Assert.Contains("does not match source-commit", trackedInputs, StringComparison.Ordinal);
        Assert.Contains("AppleApps.ProjectRoot must resolve inside the exact checked-out source", trackedInputs, StringComparison.Ordinal);
        Assert.Contains("AppleApps.ProjectRoot must not traverse a symbolic link or reparse point", trackedInputs, StringComparison.Ordinal);
        foreach (var property in new[]
                 {
                     "ScreenshotConfigPath", "ScreenshotConfigPaths",
                     "MetadataConfigPath", "MetadataConfigPaths",
                     "AppInfoConfigPath", "AppInfoConfigPaths",
                     "GovernanceConfigPath", "GovernanceConfigPaths"
                 })
        {
            Assert.Contains($"'{property}'", trackedInputs, StringComparison.Ordinal);
        }
        Assert.Contains("AppleApps.Automation.VersionSourcePath", trackedInputs, StringComparison.Ordinal);
        Assert.Contains("if ($planOnly -and $confirm)", script, StringComparison.Ordinal);
        Assert.Contains("--confirm-apple-action", script, StringComparison.Ordinal);
        Assert.Contains("--apple-expected-plan-sha256", script, StringComparison.Ordinal);
        Assert.Contains("--summary", script, StringComparison.Ordinal);
        Assert.Contains("--output', 'json", script, StringComparison.Ordinal);
        Assert.Contains("instead of '$action'", script, StringComparison.Ordinal);
        Assert.Contains("marketing-version must use x.y.z", script, StringComparison.Ordinal);
        Assert.Contains("did not write its required receipt", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$json | Write-Host", script, StringComparison.Ordinal);
        Assert.Contains("safeDiagnostics", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$envelope.error", script, StringComparison.Ordinal);
        var failureStart = script.IndexOf("if ($exitCode -ne 0)", StringComparison.Ordinal);
        var failureEnd = script.IndexOf("if (-not $envelope.success)", failureStart, StringComparison.Ordinal);
        Assert.True(failureStart >= 0 && failureEnd > failureStart);
        Assert.DoesNotContain(
            "summary = [string] $_.summary",
            script.Substring(failureStart, failureEnd - failureStart),
            StringComparison.Ordinal);
        var failureDiagnosticsOutput = script.IndexOf(
            "Write-ReleaseOutput -Name 'diagnostics'",
            failureStart,
            StringComparison.Ordinal);
        var failureThrow = script.IndexOf(
            "throw \"PowerForge Apple action '$action' failed",
            failureStart,
            StringComparison.Ordinal);
        Assert.True(failureDiagnosticsOutput > failureStart && failureDiagnosticsOutput < failureThrow);
        Assert.Contains("IsPathRooted($projectRootSetting)", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("Write-ReleaseOutput -Name 'receipt-path'", StringComparison.Ordinal) <
            script.IndexOf("& $env:POWERFORGE_TOOL_PATH", StringComparison.Ordinal));
    }

    [Fact]
    public void GovernanceSnapshotSurfacesUseTheSharedAtomicDocumentService()
    {
        var root = FindRepoRoot();
        var command = Read(root, "PSPublishModule", "Cmdlets", "ExportAppStoreConnectGovernanceCommand.cs");
        var cli = Read(root, "PowerForge.Cli", "Program.Command.AppleGovernance.cs");
        var service = Read(root, "PowerForge", "Services", "AppStoreConnectGovernanceDocumentService.cs");

        Assert.Contains("AppStoreConnectGovernanceDocumentService", command, StringComparison.Ordinal);
        Assert.Contains("AppStoreConnectGovernanceDocumentService", cli, StringComparison.Ordinal);
        Assert.Contains("File.Replace", service, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete(outputPath)", command, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteGovernanceReceipt", cli, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackedReleaseInputValidatorRejectsAnIgnoredConfiguredInput()
    {
        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"powerforge-tracked-inputs-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, ".powerforge"));
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var manifestPath = Path.Combine(sandbox, ".powerforge", "powerforge.tool.json");
            File.WriteAllText(configPath,
                """{ "AppleApps": { "ProjectRoot": ".", "ScreenshotConfigPath": "ignored-screenshots.json" } }""");
            File.WriteAllText(manifestPath, "{}");
            File.WriteAllText(Path.Combine(sandbox, ".gitignore"), "ignored-screenshots.json\n");
            File.WriteAllText(Path.Combine(sandbox, "ignored-screenshots.json"), "{}");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", "powerforge.release.json", ".powerforge/powerforge.tool.json", ".gitignore").EnsureSuccess();
            Run(
                "git",
                sandbox,
                "-c", "user.name=PowerForge Tests",
                "-c", "user.email=powerforge-tests@example.invalid",
                "commit", "--quiet", "-m", "Tracked release inputs").EnsureSuccess();
            var commit = Run("git", sandbox, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();
            var validator = Path.Combine(
                root,
                ".github",
                "actions",
                "apple-release",
                "Assert-TrackedAppleReleaseInputs.ps1");

            var result = Run(
                "pwsh",
                sandbox,
                "-NoProfile",
                "-File", validator,
                "-ConfigPath", configPath,
                "-ToolManifestPath", manifestPath,
                "-SourceCommit", commit);

            Assert.NotEqual(0, result.ExitCode);
            var output = result.StandardOutput + result.StandardError;
            Assert.True(
                output.Contains("AppleApps.ScreenshotConfigPath", StringComparison.OrdinalIgnoreCase) &&
                output.Contains("must be tracked at the exact source", StringComparison.OrdinalIgnoreCase),
                output);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
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
        Assert.Contains("version_pr_token", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("|| github.token", workflow, StringComparison.Ordinal);
        Assert.Contains("GH_TOKEN: ${{ secrets.version_pr_token }}", workflow, StringComparison.Ordinal);
        Assert.Contains("version_pr_token must not be the repository GITHUB_TOKEN", workflow, StringComparison.Ordinal);
        Assert.Contains("expected-plan-sha256: ${{ steps.plan.outputs.plan-sha256 }}", workflow, StringComparison.Ordinal);
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
        Assert.Contains("environment: ${{ inputs.planning_environment_name }}", approval, StringComparison.Ordinal);
        Assert.Contains("needs: plan", approval, StringComparison.Ordinal);
        Assert.Contains("plan_sha256: ${{ steps.plan.outputs.plan-sha256 }}", approval, StringComparison.Ordinal);
        Assert.Contains("REVIEWED_PLAN_SHA256: ${{ needs.plan.outputs.plan_sha256 }}", approval, StringComparison.Ordinal);
        Assert.Contains("CURRENT_PLAN_SHA256: ${{ steps.replan.outputs.plan-sha256 }}", approval, StringComparison.Ordinal);
        Assert.Contains("expected-plan-sha256: ${{ needs.plan.outputs.plan_sha256 }}", approval, StringComparison.Ordinal);
        Assert.Contains("expected-plan-sha256: ${{ steps.plan.outputs.plan-sha256 }}", advance, StringComparison.Ordinal);
        Assert.Contains("Apple state or release inputs changed after review", approval, StringComparison.Ordinal);
        Assert.Contains("allowed_dispatchers_json", approval, StringComparison.Ordinal);
        Assert.Contains("authorized Apple release dispatcher", approval, StringComparison.Ordinal);
        Assert.Contains("DISPATCHER: ${{ github.triggering_actor }}", approval, StringComparison.Ordinal);
        Assert.Contains("ORIGINAL_DISPATCHER: ${{ github.actor }}", approval, StringComparison.Ordinal);
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
        Assert.Equal(3, Count(approval, "if-no-files-found: error"));
        Assert.Contains("-plan", advance, StringComparison.Ordinal);
        Assert.Contains("-actual", advance, StringComparison.Ordinal);
        Assert.Contains("./powerforge-shared/.github/actions/build-powerforge", advance, StringComparison.Ordinal);
        Assert.Contains("./powerforge-shared/.github/actions/build-powerforge", approval, StringComparison.Ordinal);
        Assert.Equal(2, Count(advance, "tool-path: ${{ steps.build-powerforge.outputs.tool-path }}"));
        Assert.Equal(3, Count(approval, "tool-path: ${{ steps.build-powerforge.outputs.tool-path }}"));
        Assert.Equal(3, Count(approval, "uses: ./powerforge-shared/.github/actions/apple-release"));
        Assert.True(
            approval.IndexOf("  plan:", StringComparison.Ordinal) <
            approval.IndexOf("  approve:", StringComparison.Ordinal));
        Assert.True(
            approval.IndexOf("Replan the exact approved transition", StringComparison.Ordinal) <
            approval.IndexOf("Execute the approved transition", StringComparison.Ordinal));
        Assert.Contains("source_bootstrap_script", advance, StringComparison.Ordinal);
        Assert.Contains("source_bootstrap_script must resolve to a child", advance, StringComparison.Ordinal);
        Assert.Contains("FileAttributes]::ReparsePoint", advance, StringComparison.Ordinal);
        Assert.Contains("ls-files --error-unmatch", advance, StringComparison.Ordinal);
        Assert.Contains("diff --quiet", advance, StringComparison.Ordinal);
        Assert.Contains("& $scriptPath", advance, StringComparison.Ordinal);
        Assert.Contains("must not modify tracked or untracked release source", advance, StringComparison.Ordinal);
        Assert.Contains("source_bootstrap_script must not modify the pinned shared checkout", advance, StringComparison.Ordinal);
        Assert.Contains("status --porcelain=v1 --untracked-files=all", advance, StringComparison.Ordinal);
        Assert.Contains("Restore pinned shared checkout after source bootstrap", advance, StringComparison.Ordinal);
        Assert.Contains("Reverify pinned shared checkout after source bootstrap", advance, StringComparison.Ordinal);
        Assert.True(
            advance.IndexOf("& $scriptPath", StringComparison.Ordinal) <
            advance.IndexOf("source_bootstrap_script must not modify the pinned shared checkout", StringComparison.Ordinal));
        Assert.True(
            advance.IndexOf("Restore pinned shared checkout after source bootstrap", StringComparison.Ordinal) <
            advance.IndexOf("Build exact PowerForge source", StringComparison.Ordinal));
        Assert.True(
            advance.IndexOf("Prepare tracked source dependencies", StringComparison.Ordinal) <
            advance.IndexOf("Plan safe release advancement", StringComparison.Ordinal));
    }

    [Fact]
    public void MonitorWorkflowRunsDoctorAndMaintainsOneProactiveIncident()
    {
        var root = FindRepoRoot();
        var workflow = Read(root, ".github", "workflows", "powerforge-apple-monitor.yml");
        var action = Read(root, ".github", "actions", "apple-release", "action.yml");
        var script = Read(root, ".github", "actions", "apple-release", "Invoke-PowerForgeAppleRelease.ps1");

        Assert.Contains("action: Doctor", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: ${{ inputs.environment_name }}", workflow, StringComparison.Ordinal);
        Assert.Contains("Build the exact monitored PowerForge source", workflow, StringComparison.Ordinal);
        Assert.Contains("./powerforge-shared/.github/actions/build-powerforge", workflow, StringComparison.Ordinal);
        Assert.Contains("runtime: ${{ inputs.tool_runtime }}", workflow, StringComparison.Ordinal);
        Assert.Contains("tool-path:", workflow, StringComparison.Ordinal);
        Assert.Contains("continue-on-error: true", workflow, StringComparison.Ordinal);
        Assert.Contains("issues: write", workflow, StringComparison.Ordinal);
        Assert.Contains("gh issue create", workflow, StringComparison.Ordinal);
        Assert.Contains("gh issue comment", workflow, StringComparison.Ordinal);
        Assert.Contains("gh issue close", workflow, StringComparison.Ordinal);
        Assert.Contains("gh api --paginate --slurp", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue list --state open --limit 100", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-apple-monitor-incident:v1", workflow, StringComparison.Ordinal);
        Assert.Contains("([string] $_.body).Contains($monitorMarker)", workflow, StringComparison.Ordinal);
        Assert.Contains("Unable to enumerate all open repository issues", workflow, StringComparison.Ordinal);
        Assert.Contains("Unable to close recovered Apple incident", workflow, StringComparison.Ordinal);
        Assert.Contains("Unable to create the proactive Apple incident", workflow, StringComparison.Ordinal);
        Assert.Contains("Unable to update proactive Apple incident", workflow, StringComparison.Ordinal);
        Assert.Contains("Unable to close duplicate Apple incident", workflow, StringComparison.Ordinal);
        Assert.Contains("GH_REPO: ${{ github.repository }}", workflow, StringComparison.Ordinal);
        Assert.Contains("source-commit: ${{ inputs.source_ref }}", workflow, StringComparison.Ordinal);
        Assert.Contains("Fail when Apple Doctor found a problem", workflow, StringComparison.Ordinal);
        Assert.Contains("diagnostics:", action, StringComparison.Ordinal);
        Assert.Contains("'Status', 'Doctor'", script, StringComparison.Ordinal);
        Assert.Contains("reportedDiagnostics", script, StringComparison.Ordinal);
        Assert.Contains("IsNullOrWhiteSpace($env:PRIVATE_KEY)", action, StringComparison.Ordinal);
        Assert.True(
            action.IndexOf("IsNullOrWhiteSpace($env:PRIVATE_KEY)", StringComparison.Ordinal) <
            action.IndexOf("WriteAllText($path", StringComparison.Ordinal));
    }

    [Fact]
    public void GovernanceWorkflowPlansBeforeProtectedConfirmedApplyWithCompactReceipts()
    {
        var root = FindRepoRoot();
        var action = Read(root, ".github", "actions", "apple-governance", "action.yml");
        var script = Read(root, ".github", "actions", "apple-governance", "Invoke-PowerForgeAppleGovernance.ps1");
        var workflow = Read(root, ".github", "workflows", "powerforge-apple-governance.yml");

        Assert.Contains("Snapshot, Validate, Plan, or Apply", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeAppleGovernance.ps1", action, StringComparison.Ordinal);
        Assert.Contains("reviewed-plan-path", action, StringComparison.Ordinal);
        Assert.Contains("Apply requires confirm=true", script, StringComparison.Ordinal);
        Assert.Contains("Apply requires reviewed-plan-path", script, StringComparison.Ordinal);
        Assert.Contains("@('--reviewed-plan', $env:INPUT_REVIEWED_PLAN_PATH)", script, StringComparison.Ordinal);
        Assert.Contains("'--key-path', $env:APP_STORE_CONNECT_PRIVATE_KEY_PATH", script, StringComparison.Ordinal);
        Assert.Contains("'--key-id', $env:APP_STORE_CONNECT_KEY_ID", script, StringComparison.Ordinal);
        Assert.Contains("'--issuer-id', $env:APP_STORE_CONNECT_ISSUER_ID", script, StringComparison.Ordinal);
        Assert.Contains("--fail-on-drift", script, StringComparison.Ordinal);
        Assert.Contains("@('--summary', '--output', 'json')", script, StringComparison.Ordinal);
        Assert.Contains("[redacted]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$text | Write-Host", script, StringComparison.Ordinal);

        Assert.Contains("source_ref", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge_ref", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("default: powerforge.release.json", workflow, StringComparison.Ordinal);
        Assert.Contains("must be an exact 40-character commit SHA", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: ${{ inputs.environment_name }}", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: plan", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: ${{ inputs.approval_environment_name }}", workflow, StringComparison.Ordinal);
        Assert.Contains("authorized Apple governance dispatcher", workflow, StringComparison.Ordinal);
        Assert.Contains("DISPATCHER: ${{ github.triggering_actor }}", workflow, StringComparison.Ordinal);
        Assert.Contains("ORIGINAL_DISPATCHER: ${{ github.actor }}", workflow, StringComparison.Ordinal);
        Assert.Contains("$name must resolve to a child", workflow, StringComparison.Ordinal);
        Assert.Contains("must not traverse a symbolic link or reparse point", workflow, StringComparison.Ordinal);
        Assert.Contains("ls-files --error-unmatch", workflow, StringComparison.Ordinal);
        Assert.Contains("Build exact PowerForge source", workflow, StringComparison.Ordinal);
        Assert.Contains("operation: Validate", workflow, StringComparison.Ordinal);
        Assert.Contains("operation: Plan", workflow, StringComparison.Ordinal);
        Assert.Contains("operation: Apply", workflow, StringComparison.Ordinal);
        Assert.True(
            workflow.IndexOf("Upload governance plan for review", StringComparison.Ordinal) <
            workflow.IndexOf("  apply:", StringComparison.Ordinal));
        Assert.True(
            workflow.IndexOf("  apply:", StringComparison.Ordinal) <
            workflow.IndexOf("environment: ${{ inputs.approval_environment_name }}", StringComparison.Ordinal));
        Assert.Contains("actions/download-artifact@", workflow, StringComparison.Ordinal);
        Assert.Contains("reviewed-plan-path: ${{ runner.temp }}/reviewed-governance-plan/powerforge-apple-governance-plan.json", workflow, StringComparison.Ordinal);
        Assert.Contains("confirm: \"true\"", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-apple-governance-plan.json", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-apple-governance-actual.json", workflow, StringComparison.Ordinal);
        Assert.Equal(2, Count(workflow, "if-no-files-found: error"));
        Assert.DoesNotContain("secrets: inherit", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenshotWorkflowRequiresPinnedCaptureProtectedApprovalAndExactByteManifests()
    {
        var root = FindRepoRoot();
        var workflow = Read(root, ".github", "workflows", "powerforge-apple-screenshots.yml");

        Assert.Contains("source_ref", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge_ref", workflow, StringComparison.Ordinal);
        Assert.Contains("must be an exact 40-character commit SHA", workflow, StringComparison.Ordinal);
        Assert.Contains("Capture deterministic App Store screenshots", workflow, StringComparison.Ordinal);
        Assert.Contains("group: powerforge-apple-screenshot-capture-${{ github.repository }}", workflow, StringComparison.Ordinal);
        Assert.Contains("group: powerforge-apple-${{ github.repository }}", workflow, StringComparison.Ordinal);
        Assert.Contains("escapes the checked-out source", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: ${{ inputs.approval_environment }}", workflow, StringComparison.Ordinal);
        Assert.Contains("Prepare isolated reviewed capture paths", workflow, StringComparison.Ordinal);
        Assert.Contains("capture_artifact_path must not traverse a symbolic link or reparse point", workflow, StringComparison.Ordinal);
        Assert.Contains("path: source/${{ inputs.capture_artifact_path }}/**/*.png", workflow, StringComparison.Ordinal);
        Assert.Contains("path: ${{ steps.capture-path.outputs.review_path }}", workflow, StringComparison.Ordinal);
        Assert.Contains("Materialize only reviewed PNG files", workflow, StringComparison.Ordinal);
        Assert.Contains("Reviewed capture artifacts must contain only PNG files", workflow, StringComparison.Ordinal);
        Assert.Contains("Screenshot destination must not contain symbolic links or reparse points", workflow, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -Force", workflow, StringComparison.Ordinal);
        Assert.Contains("Materialized screenshot set does not exactly match the reviewed PNG set", workflow, StringComparison.Ordinal);
        Assert.Contains("schemaVersion = 2", workflow, StringComparison.Ordinal);
        Assert.Contains("xcodeVersion = $xcodeVersion", workflow, StringComparison.Ordinal);
        Assert.Contains("'--runtime', $captureRuntime", workflow, StringComparison.Ordinal);
        Assert.Contains("'apple-screenshots', 'manifest'", workflow, StringComparison.Ordinal);
        Assert.Contains("'--release-config', $releaseConfigPath", workflow, StringComparison.Ordinal);
        Assert.Contains("'--allowed-root', '${{ steps.capture-path.outputs.path }}'", workflow, StringComparison.Ordinal);
        Assert.Contains("'--out', $manifestPath", workflow, StringComparison.Ordinal);
        Assert.Contains("'--write-root', $sourceRoot", workflow, StringComparison.Ordinal);
        Assert.Contains("Screenshot approval manifest output escapes source", workflow, StringComparison.Ordinal);
        Assert.Contains("'--source-commit', $env:SOURCE_REF", workflow, StringComparison.Ordinal);
        Assert.Contains("'--approved-by', \"GitHub protected environment:", workflow, StringComparison.Ordinal);
        Assert.Contains("'--initiated-by', '${{ github.triggering_actor }}'", workflow, StringComparison.Ordinal);
        Assert.Contains("'--approval-evidence', '${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}'", workflow, StringComparison.Ordinal);
        Assert.Contains("action: Screenshots", workflow, StringComparison.Ordinal);
        Assert.Contains("source-commit: ${{ inputs.source_ref }}", workflow, StringComparison.Ordinal);
        Assert.Contains("target: ${{ inputs.target }}", workflow, StringComparison.Ordinal);
        Assert.Contains("confirm: \"true\"", workflow, StringComparison.Ordinal);
        Assert.Contains("retention-days: 90", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceBuildActionUsesCanonicalReleaseBuilderAndPinnedDotNetSetup()
    {
        var root = FindRepoRoot();
        var action = Read(root, ".github", "actions", "build-powerforge", "action.yml");

        Assert.Contains("actions/setup-dotnet@c2fa09f4bde5ebb9d1777cf28262a3eb3db3ced7", action, StringComparison.Ordinal);
        Assert.Contains("Build/Build-Project.ps1", action, StringComparison.Ordinal);
        Assert.Contains("-Target PowerForge", action, StringComparison.Ordinal);
        Assert.Contains("runtime must be osx-arm64 or osx-x64", action, StringComparison.Ordinal);
        Assert.Contains("tool-path=$toolPath", action, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualScreenshotApprovalBindsReviewedRunToAuthorizedActor()
    {
        var root = FindRepoRoot();
        var capture = Read(root, ".github", "workflows", "powerforge-apple-screenshot-capture.yml");
        var approval = Read(root, ".github", "workflows", "powerforge-apple-screenshot-approve.yml");

        Assert.Contains("Exact source commit to capture", capture, StringComparison.Ordinal);
        Assert.Contains("powerforge-apple-screenshots-${{ inputs.source_ref }}", capture, StringComparison.Ordinal);
        Assert.Contains("powerforge-apple-screenshot-provenance-${{ inputs.source_ref }}", capture, StringComparison.Ordinal);
        Assert.Contains("workflowRef = $env:GITHUB_WORKFLOW_REF", capture, StringComparison.Ordinal);
        Assert.Contains("marketingVersion = $env:MARKETING_VERSION", capture, StringComparison.Ordinal);
        Assert.Contains("schemaVersion = 2", capture, StringComparison.Ordinal);
        Assert.Contains("xcodeVersion = $xcodeVersion", capture, StringComparison.Ordinal);
        Assert.Contains("runtime = $captureRuntime", capture, StringComparison.Ordinal);
        Assert.Contains("device = $env:CAPTURE_DEVICE", capture, StringComparison.Ordinal);
        Assert.Contains("Capture script must be tracked at the exact source commit", capture, StringComparison.Ordinal);
        Assert.Contains("Capture script differs from the exact source commit", capture, StringComparison.Ordinal);
        Assert.Contains("Capture artifact must not traverse a symbolic link or reparse point", capture, StringComparison.Ordinal);
        Assert.Contains("path: source/${{ inputs.capture_artifact_path }}/**/*.png", capture, StringComparison.Ordinal);
        Assert.Contains("allowed_dispatchers_json", approval, StringComparison.Ordinal);
        Assert.Contains("DISPATCHER: ${{ github.triggering_actor }}", approval, StringComparison.Ordinal);
        Assert.Contains("ORIGINAL_DISPATCHER: ${{ github.actor }}", approval, StringComparison.Ordinal);
        Assert.Contains("'--initiated-by', '${{ github.triggering_actor }}'", approval, StringComparison.Ordinal);
        Assert.Contains("Resolve exact source from reviewed capture", approval, StringComparison.Ordinal);
        Assert.Contains("repos/${GITHUB_REPOSITORY}/actions/runs/${CAPTURE_RUN_ID}/artifacts", approval, StringComparison.Ordinal);
        Assert.Contains("Capture run ${CAPTURE_RUN_ID} was not produced by ${CAPTURE_WORKFLOW_PATH}", approval, StringComparison.Ordinal);
        Assert.Contains("must use the repository default-branch workflow definition", approval, StringComparison.Ordinal);
        Assert.Contains("must contain exactly one unexpired ${artifact_prefix}<sha> artifact", approval, StringComparison.Ordinal);
        Assert.Contains("powerforge-apple-screenshot-provenance-", approval, StringComparison.Ordinal);
        Assert.Contains("Capture provenance does not match the successful dedicated workflow run, source commit, version, and capture matrix", approval, StringComparison.Ordinal);
        Assert.Contains(".marketingVersion == $marketing_version", approval, StringComparison.Ordinal);
        Assert.Contains("does not match capture source", approval, StringComparison.Ordinal);
        Assert.Contains("'--approved-by', \"GitHub protected environment:", approval, StringComparison.Ordinal);
        Assert.Contains("run-id: ${{ inputs.capture_run_id }}", approval, StringComparison.Ordinal);
        Assert.Contains("approval-evidence", approval, StringComparison.Ordinal);
        Assert.Contains("target: ${{ inputs.target }}", approval, StringComparison.Ordinal);
        Assert.Contains("environment: ${{ inputs.environment_name }}", approval, StringComparison.Ordinal);
        Assert.Contains("group: powerforge-apple-${{ github.repository }}", approval, StringComparison.Ordinal);
        Assert.DoesNotContain("powerforge-apple-screenshot-approval-", approval, StringComparison.Ordinal);
        Assert.Contains("capture_artifact_path must resolve to a child of the checked-out source", approval, StringComparison.Ordinal);
        Assert.Contains("FileAttributes]::ReparsePoint", approval, StringComparison.Ordinal);
        Assert.Contains("path: ${{ steps.capture-path.outputs.review_path }}", approval, StringComparison.Ordinal);
        Assert.Contains("Restore the exact reviewed capture artifact outside source", approval, StringComparison.Ordinal);
        Assert.Contains("Materialize only reviewed PNG files", approval, StringComparison.Ordinal);
        Assert.Contains("Reviewed capture artifacts must contain only PNG files", approval, StringComparison.Ordinal);
        Assert.Contains("Screenshot destination must not contain symbolic links or reparse points", approval, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -Force", approval, StringComparison.Ordinal);
        Assert.Contains("Materialized screenshot set does not exactly match the reviewed PNG set", approval, StringComparison.Ordinal);
        Assert.Contains("powerforge-apple-screenshot-provenance.json", approval, StringComparison.Ordinal);
        Assert.Contains("'--xcode-version', $xcodeVersion", approval, StringComparison.Ordinal);
        Assert.Contains("'--runtime', $captureRuntime", approval, StringComparison.Ordinal);
        Assert.Contains("'--release-config', $releaseConfigPath", approval, StringComparison.Ordinal);
        Assert.Contains("'--allowed-root', '${{ steps.capture-path.outputs.path }}'", approval, StringComparison.Ordinal);
        Assert.Contains("'--out', $manifestPath", approval, StringComparison.Ordinal);
        Assert.Contains("'--write-root', $sourceRoot", approval, StringComparison.Ordinal);
        Assert.Contains("Screenshot approval manifest output escapes source", approval, StringComparison.Ordinal);
        Assert.Contains("'--source-commit', $env:SOURCE_REF", approval, StringComparison.Ordinal);
        Assert.Contains("source-commit: ${{ needs.resolve-source.outputs.source_ref }}", approval, StringComparison.Ordinal);
        Assert.Contains("Assert-TrackedAppleReleaseInputs.ps1", approval, StringComparison.Ordinal);
        Assert.Equal(2, Count(approval, "& $trackedInputValidator"));

        var combined = Read(root, ".github", "workflows", "powerforge-apple-screenshots.yml");
        Assert.Contains("Capture script must be tracked at the exact source commit", combined, StringComparison.Ordinal);
        Assert.Contains("Capture script differs from the exact source commit", combined, StringComparison.Ordinal);
        Assert.Contains("Capture artifact must not traverse a symbolic link or reparse point", combined, StringComparison.Ordinal);
        Assert.Contains("Assert-TrackedAppleReleaseInputs.ps1", combined, StringComparison.Ordinal);
        Assert.Equal(2, Count(combined, "& $trackedInputValidator"));
    }

    [Fact]
    public void AppleReusableWorkflowsDoNotRequireGitHubHostedRunners()
    {
        var root = FindRepoRoot();
        foreach (var workflowName in new[]
                 {
                     "powerforge-apple-screenshots.yml",
                     "powerforge-apple-screenshot-capture.yml",
                     "powerforge-apple-screenshot-approve.yml",
                     "powerforge-apple-monitor.yml",
                     "powerforge-apple-version-pr.yml",
                     "powerforge-apple-advance.yml",
                     "powerforge-apple-governance.yml",
                     "powerforge-apple-approval.yml"
                 })
        {
            var workflow = Read(root, ".github", "workflows", workflowName);
            Assert.DoesNotContain("runs-on: ubuntu-", workflow, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("runs-on: windows-", workflow, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("runs-on: macos-", workflow, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("self-hosted", workflow, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mapfile ", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("readarray ", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain(",,}", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("declare -A", workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppleEnvironmentCredentialsRemainOptionalAtTheWorkflowCallBoundary()
    {
        var root = FindRepoRoot();
        foreach (var workflowName in new[]
                 {
                     "powerforge-apple-screenshots.yml",
                     "powerforge-apple-screenshot-approve.yml",
                     "powerforge-apple-monitor.yml",
                     "powerforge-apple-version-pr.yml",
                     "powerforge-apple-advance.yml",
                     "powerforge-apple-governance.yml",
                     "powerforge-apple-approval.yml"
                 })
        {
            var workflow = Read(root, ".github", "workflows", workflowName)
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach (var secretName in new[]
                     {
                         "app_store_connect_issuer_id",
                         "app_store_connect_key_id",
                         "app_store_connect_private_key"
                     })
            {
                Assert.Contains(
                    $"      {secretName}:\n        required: false",
                    workflow,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void AppleWorkflowRunScriptsDoNotInlineCallerControlledInputs()
    {
        var root = FindRepoRoot();
        foreach (var workflowName in new[]
                 {
                     "powerforge-apple-screenshots.yml",
                     "powerforge-apple-screenshot-capture.yml",
                     "powerforge-apple-screenshot-approve.yml",
                     "powerforge-apple-monitor.yml",
                     "powerforge-apple-version-pr.yml",
                     "powerforge-apple-advance.yml",
                     "powerforge-apple-governance.yml",
                     "powerforge-apple-approval.yml",
                     "pspublishmodule-public-release.yml"
                 })
        {
            var workflow = Read(root, ".github", "workflows", workflowName);
            Assert.DoesNotContain("if ('${{ inputs.", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("-ine '${{ inputs.", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("Source: ${{ inputs.", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("actions/runs/${{ inputs.", workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppleWorkflowYamlAndEmbeddedPowerShellParse()
    {
        var root = FindRepoRoot();
        foreach (var workflowName in new[]
                 {
                     "powerforge-apple-screenshots.yml",
                     "powerforge-apple-screenshot-capture.yml",
                     "powerforge-apple-screenshot-approve.yml",
                     "powerforge-apple-monitor.yml",
                     "powerforge-apple-version-pr.yml",
                     "powerforge-apple-advance.yml",
                     "powerforge-apple-governance.yml",
                     "powerforge-apple-approval.yml",
                     "pspublishmodule-public-release.yml"
                 })
        {
            var workflow = Read(root, ".github", "workflows", workflowName);
            var document = new DeserializerBuilder().Build().Deserialize<object>(workflow);
            Assert.NotNull(document);
            foreach (var script in EnumeratePowerShellRunScripts(document))
            {
                Parser.ParseInput(script, out _, out var errors);
                Assert.True(errors.Length == 0, $"{workflowName}: {string.Join("; ", errors.Select(error => error.Message))}");
            }
        }
    }

    [Fact]
    public void WebhookCommandExamplesIncludeEveryMandatoryMutationParameter()
    {
        var root = FindRepoRoot();
        foreach (var command in new[] { "New", "Set" })
        {
            var source = Read(root, "PSPublishModule", "Cmdlets", $"{command}AppStoreConnectWebhookCommand.cs");
            var generated = Read(root, "Module", "Docs", $"{command}-AppStoreConnectWebhook.md");

            Assert.Contains("-Secret 'a-strong-webhook-secret'", source, StringComparison.Ordinal);
            Assert.Contains("-EventType 'BUILD_UPLOAD_STATE_UPDATED'", source, StringComparison.Ordinal);
            Assert.Contains("-Secret 'a-strong-webhook-secret'", generated, StringComparison.Ordinal);
            Assert.Contains("-EventType 'BUILD_UPLOAD_STATE_UPDATED'", generated, StringComparison.Ordinal);
        }
    }

    private static string Read(string root, params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static int Count(string value, string search)
        => value.Split(search, StringSplitOptions.None).Length - 1;

    private static ProcessResult Run(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        internal ProcessResult EnsureSuccess()
        {
            Assert.True(
                ExitCode == 0,
                $"Process failed with exit code {ExitCode}.{Environment.NewLine}{StandardOutput}{Environment.NewLine}{StandardError}");
            return this;
        }
    }

    private static IEnumerable<string> EnumeratePowerShellRunScripts(object? node)
    {
        if (node is IDictionary<object, object> mapping)
        {
            if (mapping.TryGetValue("run", out var run) && run is string script &&
                mapping.TryGetValue("shell", out var shell) &&
                shell is string shellName && shellName.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
            {
                yield return script;
            }

            foreach (var value in mapping.Values)
            foreach (var child in EnumeratePowerShellRunScripts(value))
                yield return child;
        }
        else if (node is IEnumerable sequence and not string)
        {
            foreach (var value in sequence)
            foreach (var child in EnumeratePowerShellRunScripts(value))
                yield return child;
        }
    }

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
