namespace PowerForge.Tests;

public sealed class GitHubServerBackupWorkflowTests
{
    [Fact]
    public void Workflow_ShouldUseSeparatePinnedSshIdentitiesAndEnvironmentSecrets()
    {
        var workflow = ReadRepoFile(".github", "workflows", "powerforge-server-backup.yml");

        Assert.Contains("environment:", workflow, StringComparison.Ordinal);
        Assert.Contains("server_ssh_private_key", workflow, StringComparison.Ordinal);
        Assert.Contains("backup_repository_ssh_private_key", workflow, StringComparison.Ordinal);
        Assert.Contains("repository_read_token || github.token", workflow, StringComparison.Ordinal);
        Assert.Contains("server_ed25519", workflow, StringComparison.Ordinal);
        Assert.Contains("backup_repository_ed25519", workflow, StringComparison.Ordinal);
        Assert.Contains("StrictHostKeyChecking yes", workflow, StringComparison.Ordinal);
        Assert.Contains("IdentitiesOnly yes", workflow, StringComparison.Ordinal);
        Assert.Contains("SERVER_SSH_COMMAND", workflow, StringComparison.Ordinal);
        Assert.Contains("--ssh $env:SERVER_SSH_COMMAND", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-keyscan", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workflow_ShouldRequireEncryptedCompleteCaptureAndExactProvenance()
    {
        var workflow = ReadRepoFile(".github", "workflows", "powerforge-server-backup.yml");

        Assert.Contains("--encrypt-remote", workflow, StringComparison.Ordinal);
        Assert.Contains("--fail-on-failure", workflow, StringComparison.Ordinal);
        Assert.Contains("plain-files.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("encrypted-secrets.tar.gz.age", workflow, StringComparison.Ordinal);
        Assert.Contains("capture-metadata.json", workflow, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", workflow, StringComparison.Ordinal);
        Assert.Contains("engineSha", workflow, StringComparison.Ordinal);
        Assert.Contains("sourceSha = $env:SOURCE_SHA_INPUT", workflow, StringComparison.Ordinal);
        Assert.Contains("SOURCE_SHA_INPUT: ${{ github.event.pull_request.head.sha || github.sha }}", workflow, StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_ShouldPublishManifestRetentionWithPushRaceRetry()
    {
        var workflow = ReadRepoFile(".github", "workflows", "powerforge-server-backup.yml");

        Assert.Contains("backupTarget.retention.keepLatest", workflow, StringComparison.Ordinal);
        Assert.Contains("Select-Object -Skip $keepLatest", workflow, StringComparison.Ordinal);
        Assert.Contains("git rebase \"origin/$BACKUP_BRANCH\"", workflow, StringComparison.Ordinal);
        Assert.Contains("mapfile -t captures", workflow, StringComparison.Ordinal);
        Assert.Contains("git commit --amend --no-edit", workflow, StringComparison.Ordinal);
        Assert.Contains("for attempt in 1 2 3", workflow, StringComparison.Ordinal);
        Assert.Contains("git push origin \"HEAD:$BACKUP_BRANCH\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("upload-artifact", workflow, StringComparison.OrdinalIgnoreCase);
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
