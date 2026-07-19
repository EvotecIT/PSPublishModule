namespace PowerForge.Tests;

public sealed class GitHubServiceLinuxDeployWorkflowTests
{
    [Fact]
    public void WorkflowPackagesUniqueArtifactAndUsesTemporarySshCredentials()
    {
        var workflow = ReadRepoFile(".github", "workflows", "powerforge-service-deploy.yml");
        var normalizedWorkflow = workflow.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("powerforge-service-{0}", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_private_key:\n        required: false", normalizedWorkflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_known_hosts:\n        required: false", normalizedWorkflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_private_key is required", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_known_hosts is required", workflow, StringComparison.Ordinal);
        Assert.Contains("service_validation_script", workflow, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", workflow, StringComparison.Ordinal);
        Assert.Contains("realpath -e", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-service-deployment-ssh", workflow, StringComparison.Ordinal);
        Assert.Contains("UserKnownHostsFile=$knownHostsPath", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-service-deploy --service", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--archive '$remoteBase", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-keyscan", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("$HOME/.ssh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--dereference", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("vars.POWERFORGE_DEPLOY_HOST", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("steps.deployment_target.outputs.host", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_host:\n        description:", normalizedWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PromoterUsesTrustedStagingAndRollsBackSystemdService()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-service-deploy.sh");

        Assert.Contains("POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("install -m 0600 \"$archive\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("{40,64}", script, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
        Assert.Contains("tar -tvf", script, StringComparison.Ordinal);
        Assert.Contains("mv -Tf", script, StringComparison.Ordinal);
        Assert.Contains("systemctl restart", script, StringComparison.Ordinal);
        Assert.Contains("systemctl stop", script, StringComparison.Ordinal);
        Assert.Contains("sourceSha", script, StringComparison.Ordinal);
        Assert.Contains("workflowRunId", script, StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("flock -n", StringComparison.Ordinal) < script.IndexOf("realpath -e", StringComparison.Ordinal));
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativePath).ToArray()));
    }
}
