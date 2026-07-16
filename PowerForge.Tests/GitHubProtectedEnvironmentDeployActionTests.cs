namespace PowerForge.Tests;

public sealed class GitHubProtectedEnvironmentDeployActionTests
{
    [Fact]
    public void SiteDeployAction_ShouldKeepProtectedInputsInCallerJobAndUseNativeScript()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-linux-site-deploy", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-linux-site-deploy", "Invoke-PowerForgeLinuxSiteDeploy.ps1");
        var publicVerification = ReadRepoFile(".github", "actions", "powerforge-linux-site-deploy", "Test-PowerForgePublicSite.ps1");

        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Contains("deployment-ssh-private-key", action, StringComparison.Ordinal);
        Assert.Contains("deployment-public-url", action, StringComparison.Ordinal);
        Assert.Contains("deployment-smoke-paths", action, StringComparison.Ordinal);
        Assert.Contains("Reject pull request deployments", action, StringComparison.Ordinal);
        Assert.DoesNotContain("github.event.pull_request.head.sha", action, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeLinuxSiteDeploy.ps1", action, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", action, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
        Assert.Contains("$metadata['engineSha']", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-site-deploy", script, StringComparison.Ordinal);
        Assert.Contains("IdentitiesOnly=yes", script, StringComparison.Ordinal);
        Assert.Contains("StrictHostKeyChecking=yes", script, StringComparison.Ordinal);
        Assert.Contains("cloudflare-api.token", script, StringComparison.Ordinal);
        Assert.Contains("--defer-public-verification", script, StringComparison.Ordinal);
        Assert.Contains("Assert-PowerForgePublicSite", script, StringComparison.Ordinal);
        Assert.Contains("deployment-public-url is required", script, StringComparison.Ordinal);
        Assert.DoesNotContain("https://$($env:POWERFORGE_DEPLOYMENT_SITE)", script, StringComparison.Ordinal);
        Assert.Contains("--finalize", script, StringComparison.Ordinal);
        Assert.Contains("--rollback", script, StringComparison.Ordinal);
        Assert.Contains("retrying once to confirm terminal state", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-CloudflarePurge", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgePublicRequest", publicVerification, StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", publicVerification, StringComparison.Ordinal);
        Assert.Contains("finally", script, StringComparison.Ordinal);
        Assert.InRange(NormalizedLineCount(script), 1, 250);
        Assert.InRange(NormalizedLineCount(publicVerification), 1, 120);
    }

    [Fact]
    public void ServicePackageAction_ShouldValidateAndUploadWithoutDeploymentCredentials()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-linux-service-package", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-linux-service-package", "Invoke-PowerForgeLinuxServicePackage.ps1");

        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Contains("service-validation-script", action, StringComparison.Ordinal);
        Assert.Contains("actions/checkout@", action, StringComparison.Ordinal);
        Assert.DoesNotContain("github.event.pull_request.head.sha", action, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@", action, StringComparison.Ordinal);
        Assert.Contains("overwrite: true", action, StringComparison.Ordinal);
        Assert.Contains("default: \"7\"", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeLinuxServicePackage.ps1", action, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", action, StringComparison.Ordinal);
        Assert.DoesNotContain("deployment-ssh", action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("realpath --canonicalize-existing", script, StringComparison.Ordinal);
        Assert.Contains("bash $validationScript", script, StringComparison.Ordinal);
        Assert.Contains("GITHUB_ENV", script, StringComparison.Ordinal);
        Assert.Contains("SetEnvironmentVariable($name, $null, 'Process')", script, StringComparison.Ordinal);
        Assert.Contains("Assert-WorkspacePath -Path $resolvedServiceRoot", script, StringComparison.Ordinal);
        Assert.Contains("tar --directory $resolvedServiceRoot", script, StringComparison.Ordinal);
        Assert.Contains("package.json", script, StringComparison.Ordinal);
        Assert.Contains("sourceRepository", script, StringComparison.Ordinal);
        Assert.Contains("workflowRunId", script, StringComparison.Ordinal);
        Assert.DoesNotContain("POWERFORGE_DEPLOYMENT_SSH", script, StringComparison.Ordinal);
        Assert.InRange(NormalizedLineCount(script), 1, 150);
    }

    [Fact]
    public void ServiceDeployAction_ShouldDownloadPromoteAndCleanupValidatedArtifact()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-linux-service-deploy", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-linux-service-deploy", "Invoke-PowerForgeLinuxServiceDeploy.ps1");

        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Contains("artifact-name", action, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@", action, StringComparison.Ordinal);
        Assert.Contains("Reject pull request deployments", action, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/checkout@", action, StringComparison.Ordinal);
        Assert.DoesNotContain("service-validation-script", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeLinuxServiceDeploy.ps1", action, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", action, StringComparison.Ordinal);
        Assert.DoesNotContain("bash $validationScript", script, StringComparison.Ordinal);
        Assert.Contains("service package metadata", script, StringComparison.Ordinal);
        Assert.Contains("does not match its expected source", script, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
        Assert.Contains("GITHUB_RUN_ID", script, StringComparison.Ordinal);
        Assert.Contains("flock -w 900", script, StringComparison.Ordinal);
        Assert.Contains("/tmp/powerforge-service-$($env:POWERFORGE_DEPLOYMENT_SERVICE).lock", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".powerforge/locks", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-service-deploy", script, StringComparison.Ordinal);
        Assert.Contains("IdentitiesOnly=yes", script, StringComparison.Ordinal);
        Assert.Contains("StrictHostKeyChecking=yes", script, StringComparison.Ordinal);
        Assert.Contains("finally", script, StringComparison.Ordinal);
        Assert.InRange(NormalizedLineCount(script), 1, 250);
    }

    [Fact]
    public void ServerBackupAction_ShouldEncryptValidateRetainAndPublishWithExactProvenance()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-server-backup", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-server-backup", "Invoke-PowerForgeServerBackup.ps1");

        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Contains("server-ssh-private-key", action, StringComparison.Ordinal);
        Assert.Contains("backup-repository-ssh-private-key", action, StringComparison.Ordinal);
        Assert.Contains("github.action_ref", action, StringComparison.Ordinal);
        Assert.Contains("Reject pull request backups", action, StringComparison.Ordinal);
        Assert.DoesNotContain("github.event.pull_request.head.sha", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeServerBackup.ps1", action, StringComparison.Ordinal);
        Assert.Contains("or empty when a concurrent retention update superseded it", action, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", action, StringComparison.Ordinal);
        Assert.Contains("--encrypt-remote", script, StringComparison.Ordinal);
        Assert.Contains("--fail-on-failure", script, StringComparison.Ordinal);
        Assert.Contains("encrypted-secrets.tar.gz.age", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", script, StringComparison.Ordinal);
        Assert.Contains("commit --amend --no-edit", script, StringComparison.Ordinal);
        Assert.Contains("Backup publication failed after three push attempts", script, StringComparison.Ordinal);
        Assert.Contains("origin/$backupBranch", script, StringComparison.Ordinal);
        Assert.Contains("'22'", script, StringComparison.Ordinal);
        Assert.Contains("capturePortText", script, StringComparison.Ordinal);
        Assert.Contains("'powerforge-capture'", script, StringComparison.Ordinal);
        Assert.Contains("recipientEnv", script, StringComparison.Ordinal);
        Assert.Contains("GetEnvironmentVariable($recipientEnvName", script, StringComparison.Ordinal);
        Assert.Contains("HostKeyAlias github.com", script, StringComparison.Ordinal);
        Assert.Contains("IdentityFile \"$serverKey\"", script, StringComparison.Ordinal);
        Assert.Contains("UserKnownHostsFile \"$serverKnownHosts\"", script, StringComparison.Ordinal);
        Assert.Contains("IdentityFile \"$backupKey\"", script, StringComparison.Ordinal);
        Assert.Contains("UserKnownHostsFile \"$backupKnownHosts\"", script, StringComparison.Ordinal);
        Assert.Contains("capture-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("Add-Member -NotePropertyName sshAlias", script, StringComparison.Ordinal);
        Assert.Contains("$env:GIT_SSH = $serverSshCommand", script, StringComparison.Ordinal);
        Assert.Contains("$env:GIT_SSH_VARIANT = 'ssh'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:GIT_SSH_COMMAND =", script, StringComparison.Ordinal);
        Assert.Contains("git -C $checkout ls-tree --name-only $publishedCommit", script, StringComparison.Ordinal);
        Assert.Contains("$publishedCaptureName", script, StringComparison.Ordinal);
        Assert.Contains("engineSha", script, StringComparison.Ordinal);
        Assert.Contains("finally", script, StringComparison.Ordinal);
        Assert.InRange(NormalizedLineCount(script), 1, 400);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativePath).ToArray()));
    }

    private static int NormalizedLineCount(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length;
}
