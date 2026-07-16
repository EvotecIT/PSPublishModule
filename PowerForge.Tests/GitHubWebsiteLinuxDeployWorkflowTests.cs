namespace PowerForge.Tests;

public sealed class GitHubWebsiteLinuxDeployWorkflowTests
{
    [Fact]
    public void DeployWorkflow_ShouldKeepPagesDefaultAndExposeLinuxTarget()
    {
        var workflow = ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml");
        var normalizedWorkflow = workflow.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("default: \"github-pages\"", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_target == 'linux'", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_site", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_private_key", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_known_hosts", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-keyscan", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deployment_artifact_retention_days", workflow, StringComparison.Ordinal);
        Assert.Contains("github.workflow_ref", workflow, StringComparison.Ordinal);
        Assert.Contains("github.workflow_sha", workflow, StringComparison.Ordinal);
        Assert.Contains("needs.guardrails.outputs.workflow_repository", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("job.workflow_", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("{40,64}", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: ./.powerforge-deployment/.github/actions/powerforge-linux-site-deploy", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_url is required when deployment_target is linux", ReadRepoFile("Build", "Assert-PowerForgeWebsiteDeployGuardrails.ps1"), StringComparison.Ordinal);
        Assert.DoesNotContain("Publish and promote Linux release", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("scp @scpArgs", workflow, StringComparison.Ordinal);
        Assert.Contains("      pages: write", workflow, StringComparison.Ordinal);
        Assert.Contains("      id-token: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("vars.POWERFORGE_DEPLOY_HOST", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("steps.deployment_target.outputs.host", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_host:\n        description:", normalizedWorkflow, StringComparison.Ordinal);
        Assert.Contains("inputs.deployment_host || vars.POWERFORGE_WEBSITE_DEPLOY_HOST", workflow, StringComparison.Ordinal);
        Assert.Contains("inputs.deployment_port || vars.POWERFORGE_WEBSITE_DEPLOY_PORT || 22", workflow, StringComparison.Ordinal);
        Assert.Contains("inputs.deployment_user || vars.POWERFORGE_WEBSITE_DEPLOY_USER || 'powerforge-deploy'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployWorkflow_ShouldPublishExactProvenance()
    {
        var deployWorkflow = ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml");
        var runWorkflow = ReadRepoFile(".github", "workflows", "powerforge-website-run.yml");
        var deployAction = ReadRepoFile(".github", "actions", "powerforge-linux-site-deploy", "Invoke-PowerForgeLinuxSiteDeploy.ps1");

        Assert.Contains("source_ref:", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ inputs.source_ref || github.event.pull_request.head.sha || github.sha }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Equal(2, deployWorkflow.Split("source_ref: ${{ inputs.source_ref }}", StringSplitOptions.None).Length - 1);
        Assert.Contains("source-sha: ${{ needs.build.outputs.source_sha }}", deployWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("source-sha: ${{ github.event.pull_request.head.sha || github.sha }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("--result-path", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("Resolve actual PowerForge engine provenance", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("assetSha256", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("sourceSha", deployAction, StringComparison.Ordinal);
        Assert.Contains("engineSha", deployAction, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", deployAction, StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", deployAction, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxPromoter_ShouldProtectPromotionAndRollbackContracts()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-site-deploy.sh");
        var reconciler = ReadRepoFile("Deployment", "Linux", "powerforge-site-reconcile.sh");
        var reconcileTimer = ReadRepoFile("Deployment", "Linux", "systemd", "powerforge-site-reconcile.timer");

        Assert.Contains("/etc/powerforge/sites", script, StringComparison.Ordinal);
        Assert.Contains("Artifact checksum does not match", script, StringComparison.Ordinal);
        Assert.Contains("Archive contains path traversal", script, StringComparison.Ordinal);
        Assert.Contains("purge_cloudflare", script, StringComparison.Ordinal);
        Assert.Contains("verify_public_release", script, StringComparison.Ordinal);
        Assert.Contains("verify_origin_release", script, StringComparison.Ordinal);
        Assert.Contains("--defer-public-verification", script, StringComparison.Ordinal);
        Assert.Contains("finalize_deferred_release", script, StringComparison.Ordinal);
        Assert.Contains("was already finalized", script, StringComparison.Ordinal);
        Assert.Contains("was already rolled back", script, StringComparison.Ordinal);
        Assert.Contains("rollback_deferred_release", script, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_RELEASE_ID", script, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_PENDING_EXPIRES_AT", script, StringComparison.Ordinal);
        Assert.Contains("--expire-pending", script, StringComparison.Ordinal);
        Assert.Contains("PENDING_TTL_SECONDS", script, StringComparison.Ordinal);
        Assert.Contains("ensure_pending_reconciler", script, StringComparison.Ordinal);
        Assert.Contains("create_pending_release", script, StringComparison.Ordinal);
        Assert.Contains("configure_pending_cloudflare", script, StringComparison.Ordinal);
        Assert.Contains("remove_pending_release", script, StringComparison.Ordinal);
        Assert.Contains("rolling back", script, StringComparison.Ordinal);
        Assert.Contains("Migrating legacy current directory", script, StringComparison.Ordinal);
        Assert.Contains("restoring the legacy current directory", script, StringComparison.Ordinal);
        Assert.Contains("legacy_migrated=1", script, StringComparison.Ordinal);
        Assert.Contains("mv -Tf", script, StringComparison.Ordinal);
        Assert.Contains("umask 022", script, StringComparison.Ordinal);
        Assert.Contains("Cloudflare zone id is required", script, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_SITE_TRUSTED_STAGE_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("install -m 0600 \"$archive\"", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-site-{0}", ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml"), StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", script, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
        Assert.DoesNotContain("{40,64}", script, StringComparison.Ordinal);
        Assert.Contains("^/tmp/powerforge-([0-9]+)-([0-9]+)-", script, StringComparison.Ordinal);
        Assert.Contains("BASH_REMATCH[3]", script, StringComparison.Ordinal);
        string workflow = ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml");
        Assert.Contains("deployment_cloudflare_zone", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_cloudflare_api_token", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment-cloudflare-api-token: ${{ secrets.deployment_cloudflare_api_token }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("deployment-cloudflare-api-token: ${{ secrets.cloudflare_api_token }}", workflow, StringComparison.Ordinal);
        Assert.Contains("cloudflare-zone-id: ${{ vars.CLOUDFLARE_ZONE_ID || secrets.cloudflare_zone_id }}", workflow, StringComparison.Ordinal);
        Assert.Contains("zone-id: ${{ vars.CLOUDFLARE_ZONE_ID || secrets.cloudflare_zone_id }}", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment-public-url: ${{ inputs.deployment_url }}", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment-smoke-paths: ${{ inputs.deployment_smoke_paths }}", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: ./.powerforge-deployment/.github/actions/powerforge-cloudflare-cache-policy", workflow, StringComparison.Ordinal);
        Assert.Contains("checkout-caller: \"false\"", workflow, StringComparison.Ordinal);
        Assert.Contains("if: ${{ inputs.deployment_cloudflare_zone != '' }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  cache-policy:\n", workflow.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(
            workflow.IndexOf("powerforge-cloudflare-cache-policy", StringComparison.Ordinal) < workflow.IndexOf("powerforge-linux-site-deploy", StringComparison.Ordinal),
            "The optional cache policy and deployment must share one protected job and approval boundary.");
        Assert.Contains("name: ${{ inputs.deployment_environment }}", workflow, StringComparison.Ordinal);
        string deployAction = ReadRepoFile(".github", "actions", "powerforge-linux-site-deploy", "Invoke-PowerForgeLinuxSiteDeploy.ps1");
        Assert.Contains("per_page=5", deployAction, StringComparison.Ordinal);
        string environmentExample = ReadRepoFile("Deployment", "Linux", "powerforge-site.env.example");
        Assert.Contains("deployment_cloudflare_api_token", environmentExample, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow's cloudflare_api_token secret", environmentExample, StringComparison.Ordinal);
        Assert.Contains("cloudflare-api.token", script, StringComparison.Ordinal);
        Assert.Contains("Ephemeral Cloudflare zone id does not match", script, StringComparison.Ordinal);
        Assert.Contains("--expire-pending", reconciler, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_SITE_PENDING_STATE_ROOT", reconciler, StringComparison.Ordinal);
        Assert.Contains("OnUnitActiveSec=1min", reconcileTimer, StringComparison.Ordinal);
        Assert.Contains("Persistent=true", reconcileTimer, StringComparison.Ordinal);
        Assert.Contains("sudo bash Deployment/Linux/tests/powerforge-site-deploy-fixture.sh", ReadRepoFile(".github", "workflows", "BuildModule.yml"), StringComparison.Ordinal);
        Assert.InRange(NormalizedLineCount(script), 1, 650);
        Assert.InRange(NormalizedLineCount(reconciler), 1, 100);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return File.ReadAllText(Path.Combine([current.FullName, .. relativePath]));
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static int NormalizedLineCount(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length;
}
