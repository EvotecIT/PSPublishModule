namespace PowerForge.Tests;

public sealed class GitHubWebsiteLinuxDeployWorkflowTests
{
    [Fact]
    public void DeployWorkflow_ShouldKeepPagesDefaultAndExposeLinuxTarget()
    {
        var workflow = ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml");

        Assert.Contains("default: \"github-pages\"", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_target == 'linux'", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_site", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_private_key", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_known_hosts", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-keyscan", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deployment_artifact_retention_days", workflow, StringComparison.Ordinal);
        Assert.Contains("$env:RUNNER_TEMP 'powerforge-deployment-ssh'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Join-Path $HOME '.ssh'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployWorkflow_ShouldPublishExactProvenance()
    {
        var deployWorkflow = ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml");
        var runWorkflow = ReadRepoFile(".github", "workflows", "powerforge-website-run.yml");

        Assert.Contains("--result-path", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("Resolve actual PowerForge engine provenance", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("assetSha256", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("sourceSha", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("engineSha", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", deployWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxPromoter_ShouldProtectPromotionAndRollbackContracts()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-site-deploy.sh");

        Assert.Contains("/etc/powerforge/sites", script, StringComparison.Ordinal);
        Assert.Contains("Artifact checksum does not match", script, StringComparison.Ordinal);
        Assert.Contains("Archive contains path traversal", script, StringComparison.Ordinal);
        Assert.Contains("purge_cloudflare", script, StringComparison.Ordinal);
        Assert.Contains("Public endpoint did not serve", script, StringComparison.Ordinal);
        Assert.Contains("Origin endpoint did not serve", script, StringComparison.Ordinal);
        Assert.Contains("rolling back", script, StringComparison.Ordinal);
        Assert.Contains("mv -Tf", script, StringComparison.Ordinal);
        Assert.Contains("umask 022", script, StringComparison.Ordinal);
        Assert.Contains("CLOUDFLARE_ZONE_ID is required", script, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_SITE_TRUSTED_STAGE_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("install -m 0600 \"$archive\"", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-site-{0}", ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml"), StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", script, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
        Assert.Contains("^/tmp/powerforge-([0-9]+)-([0-9]+)-", script, StringComparison.Ordinal);
        Assert.Contains("BASH_REMATCH[3]", script, StringComparison.Ordinal);
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
}
