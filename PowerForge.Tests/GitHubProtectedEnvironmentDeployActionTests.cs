namespace PowerForge.Tests;

public sealed class GitHubProtectedEnvironmentDeployActionTests
{
    [Fact]
    public void SiteDeployAction_ShouldKeepProtectedInputsInCallerJobAndUseNativeScript()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-linux-site-deploy", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-linux-site-deploy", "Invoke-PowerForgeLinuxSiteDeploy.ps1");

        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Contains("deployment-ssh-private-key", action, StringComparison.Ordinal);
        Assert.Contains("Reject pull request deployments", action, StringComparison.Ordinal);
        Assert.DoesNotContain("github.event.pull_request.head.sha", action, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeLinuxSiteDeploy.ps1", action, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", action, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
        Assert.Contains("$metadata['engineSha']", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-site-deploy", script, StringComparison.Ordinal);
        Assert.Contains("StrictHostKeyChecking=yes", script, StringComparison.Ordinal);
        Assert.Contains("cloudflare-api.token", script, StringComparison.Ordinal);
        Assert.Contains("finally", script, StringComparison.Ordinal);
        Assert.InRange(NormalizedLineCount(script), 1, 250);
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
        Assert.Contains("powerforge-service-deploy", script, StringComparison.Ordinal);
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
