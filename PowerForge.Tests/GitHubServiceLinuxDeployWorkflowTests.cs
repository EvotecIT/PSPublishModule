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
        Assert.Contains("Assert-SourceRevision", workflow, StringComparison.Ordinal);
        Assert.Contains("does not match its exact provenance commit", workflow, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", workflow, StringComparison.Ordinal);
        Assert.Contains("realpath -e", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-service-deployment-ssh", workflow, StringComparison.Ordinal);
        Assert.Contains("UserKnownHostsFile=$knownHostsPath", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-service-deploy-v1 --service", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment-transport.tar", workflow, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardInput = $true", workflow, StringComparison.Ordinal);
        Assert.Contains("Format-SshDiagnostic", workflow, StringComparison.Ordinal);
        Assert.Contains("Remote stderr", workflow, StringComparison.Ordinal);
        Assert.Contains("earlier output truncated", workflow, StringComparison.Ordinal);
        Assert.Contains("tail --bytes=8193", workflow, StringComparison.Ordinal);
        Assert.Contains("  | $_", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadToEndAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("scp @", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("sudo /usr/local/sbin/powerforge-service-deploy --service", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--archive '$remoteBase", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-keyscan", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("$HOME/.ssh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--dereference", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("vars.POWERFORGE_DEPLOY_HOST", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("steps.deployment_target.outputs.host", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_host:\n        description:", normalizedWorkflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-ssh-diagnostic-fixture.ps1", ReadRepoFile(".github", "workflows", "BuildModule.yml"), StringComparison.Ordinal);
        Assert.Contains("bash Tests/Linux/powerforge-service-deploy.tests.sh", ReadRepoFile(".github", "workflows", "BuildModule.yml"), StringComparison.Ordinal);
    }

    [Fact]
    public void PromoterUsesTrustedStagingAndRollsBackSystemdService()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-service-deploy.sh");

        Assert.Contains("POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("install -m 0600 \"$archive\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("{40,64}", script, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
        Assert.Contains("tar --list --verbose --numeric-owner --full-time", script, StringComparison.Ordinal);
        Assert.Contains("mv -Tf", script, StringComparison.Ordinal);
        Assert.Contains("systemctl restart", script, StringComparison.Ordinal);
        Assert.Contains("systemctl stop", script, StringComparison.Ordinal);
        Assert.Contains("sourceSha", script, StringComparison.Ordinal);
        Assert.Contains("workflowRunId", script, StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", script, StringComparison.Ordinal);
        Assert.Contains("assert_trusted_directory_chain \"$CONFIG_ROOT\"", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-systemd-${unit_lock_key}.lock", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-root-${service_root_lock_key}.lock", script, StringComparison.Ordinal);
        Assert.Contains("service-deployment-state", script, StringComparison.Ordinal);
        Assert.Contains("service-${service_id}.transaction", script, StringComparison.Ordinal);
        Assert.Contains("Recovering incomplete systemd writable-path transaction", script, StringComparison.Ordinal);
        Assert.Contains("sync_deployment_state", script, StringComparison.Ordinal);
        Assert.Contains("must not overlap deployment control path", script, StringComparison.Ordinal);
        Assert.Contains("rollback 143", script, StringComparison.Ordinal);
        Assert.Contains("Rejected release retained for recovery", script, StringComparison.Ordinal);
        Assert.Contains("MAX_RELEASE_ARCHIVE_ENTRIES=100000", script, StringComparison.Ordinal);
        Assert.Contains("Artifact must be an uncompressed tar archive", script, StringComparison.Ordinal);
        Assert.Contains("Archive expands beyond the deployment size limit", script, StringComparison.Ordinal);
        Assert.DoesNotContain("archive-names", script, StringComparison.Ordinal);
        Assert.DoesNotContain("archive-listing", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RestrictedSshTransport_ShouldAllowOnlyPinnedServicePromotion()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-service-deploy-ssh.sh");

        Assert.Contains("SSH_ORIGINAL_COMMAND", script, StringComparison.Ordinal);
        Assert.Contains("--allow-service", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-service-deploy-v1", script, StringComparison.Ordinal);
        Assert.Contains("max_payload_bytes=1073741824", script, StringComparison.Ordinal);
        Assert.Contains("max_metadata_bytes=1048576", script, StringComparison.Ordinal);
        Assert.Contains("Sparse deployment payload files are not supported", script, StringComparison.Ordinal);
        Assert.Contains("Deployment payload must be an uncompressed tar archive", script, StringComparison.Ordinal);
        Assert.Contains("dispatcher_lock_target", script, StringComparison.Ordinal);
        Assert.Contains("exec 8<\"$dispatcher_lock_target\"", script, StringComparison.Ordinal);
        Assert.Contains("timeout --foreground", script, StringComparison.Ordinal);
        Assert.Contains("logical_size", script, StringComparison.Ordinal);
        Assert.Contains("allocated_bytes", script, StringComparison.Ordinal);
        Assert.Contains("head --bytes=1024", script, StringComparison.Ordinal);
        Assert.Contains("artifact.tar", script, StringComparison.Ordinal);
        Assert.Contains("deployment.json", script, StringComparison.Ordinal);
        Assert.Contains("flock -w 900", script, StringComparison.Ordinal);
        Assert.Contains("sudo /usr/local/sbin/powerforge-service-deploy --service", script, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativePath).ToArray()));
    }
}
