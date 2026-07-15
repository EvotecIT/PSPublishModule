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
        Assert.Contains("actions/download-artifact@", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeLinuxSiteDeploy.ps1", action, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", action, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
        Assert.Contains("engineSha", script, StringComparison.Ordinal);
        Assert.Contains("powerforge-site-deploy", script, StringComparison.Ordinal);
        Assert.Contains("StrictHostKeyChecking=yes", script, StringComparison.Ordinal);
        Assert.Contains("cloudflare-api.token", script, StringComparison.Ordinal);
        Assert.Contains("finally", script, StringComparison.Ordinal);
        Assert.InRange(NormalizedLineCount(script), 1, 250);
    }

    [Fact]
    public void ServiceDeployAction_ShouldValidatePackagePromoteAndCleanupInNativeScript()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-linux-service-deploy", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-linux-service-deploy", "Invoke-PowerForgeLinuxServiceDeploy.ps1");

        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Contains("service-validation-script", action, StringComparison.Ordinal);
        Assert.Contains("actions/checkout@", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeLinuxServiceDeploy.ps1", action, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", action, StringComparison.Ordinal);
        Assert.Contains("service-root resolved outside", script, StringComparison.Ordinal);
        Assert.Contains("artifactSha256", script, StringComparison.Ordinal);
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
        Assert.Contains("Invoke-PowerForgeServerBackup.ps1", action, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", action, StringComparison.Ordinal);
        Assert.Contains("--encrypt-remote", script, StringComparison.Ordinal);
        Assert.Contains("--fail-on-failure", script, StringComparison.Ordinal);
        Assert.Contains("encrypted-secrets.tar.gz.age", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", script, StringComparison.Ordinal);
        Assert.Contains("commit --amend --no-edit", script, StringComparison.Ordinal);
        Assert.Contains("Backup publication failed after three push attempts", script, StringComparison.Ordinal);
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
