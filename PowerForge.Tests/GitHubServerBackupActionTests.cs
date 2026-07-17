namespace PowerForge.Tests;

public sealed class GitHubServerBackupActionTests
{
    [Fact]
    public void Action_ShouldKeepCredentialsInCallerEnvironmentAndSeparateSshIdentities()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-server-backup", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-server-backup", "Invoke-PowerForgeServerBackup.ps1");

        Assert.Contains("server-ssh-private-key", action, StringComparison.Ordinal);
        Assert.Contains("server-host", action, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_SERVER_HOST: ${{ inputs.server-host }}", action, StringComparison.Ordinal);
        Assert.Contains("backup-repository-ssh-private-key", action, StringComparison.Ordinal);
        Assert.Contains("server_ed25519", script, StringComparison.Ordinal);
        Assert.Contains("backup_repository_ed25519", script, StringComparison.Ordinal);
        Assert.Contains("StrictHostKeyChecking yes", script, StringComparison.Ordinal);
        Assert.Contains("IdentitiesOnly yes", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-keyscan", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Action_ShouldPreferProtectedHostAndPreserveManifestFallback()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-server-backup", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-server-backup", "Invoke-PowerForgeServerBackup.ps1");

        Assert.Contains("Optional capture-host override from the caller job's protected environment", action, StringComparison.Ordinal);
        Assert.Contains("[string]::IsNullOrWhiteSpace($env:POWERFORGE_SERVER_HOST)", script, StringComparison.Ordinal);
        Assert.Contains("[string]$manifest.target.host", script, StringComparison.Ordinal);
        Assert.Contains("$env:POWERFORGE_SERVER_HOST.Trim()", script, StringComparison.Ordinal);
        Assert.Contains("server-host or target.host must be a valid hostname or IP address", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$manifest.target.host = $captureHost", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Action_ShouldRequireCompleteEncryptedCaptureAndExactProvenance()
    {
        var script = ReadRepoFile(".github", "actions", "powerforge-server-backup", "Invoke-PowerForgeServerBackup.ps1");

        Assert.Contains("--encrypt-remote", script, StringComparison.Ordinal);
        Assert.Contains("--fail-on-failure", script, StringComparison.Ordinal);
        Assert.Contains("plain-files.tar.gz", script, StringComparison.Ordinal);
        Assert.Contains("encrypted-secrets.tar.gz.age", script, StringComparison.Ordinal);
        Assert.Contains("capture-metadata.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("refCaptureCommandId", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Captured repository revision", script, StringComparison.Ordinal);
        Assert.DoesNotContain("{40,64}", script, StringComparison.Ordinal);
        Assert.Contains("{40}|[a-fA-F0-9]{64}", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", script, StringComparison.Ordinal);
        Assert.Contains("engineSha", script, StringComparison.Ordinal);
        Assert.Contains("sourceSha", script, StringComparison.Ordinal);
        Assert.Contains("workflowRunAttempt", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Action_ShouldSurfaceBoundedArchiveDiagnosticsWithoutPrintingCommandCaptures()
    {
        var script = ReadRepoFile(".github", "actions", "powerforge-server-backup", "Invoke-PowerForgeServerBackup.ps1");

        Assert.Contains("Write-CaptureFailureDiagnostic", script, StringComparison.Ordinal);
        Assert.Contains("plain-files.stderr.txt", script, StringComparison.Ordinal);
        Assert.Contains("encrypted-secrets.stderr.txt", script, StringComparison.Ordinal);
        Assert.Contains("-TotalCount 40", script, StringComparison.Ordinal);
        Assert.Contains("$diagnostic.Length -gt 4096", script, StringComparison.Ordinal);
        Assert.Contains("::stop-commands::$stopToken", script, StringComparison.Ordinal);
        Assert.Contains("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F\\x7F]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("commands/*.stderr", script, StringComparison.Ordinal);
        Assert.DoesNotContain("commands\\*.stderr", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Action_ShouldStateCurrentTreeRetentionAndPreserveCompatibility()
    {
        var script = ReadRepoFile(".github", "actions", "powerforge-server-backup", "Invoke-PowerForgeServerBackup.ps1");

        Assert.Contains("retention.keepLatestInTree", script, StringComparison.Ordinal);
        Assert.Contains("retention.keepLatest", script, StringComparison.Ordinal);
        Assert.Contains("retainedCapturesInTree", script, StringComparison.Ordinal);
        Assert.Contains("gitHistoryRetention = 'preserve'", script, StringComparison.Ordinal);
        Assert.Contains("retention.keepDays is not implemented", script, StringComparison.Ordinal);
        Assert.Contains("Select-Object -Skip $keepLatestInTree", script, StringComparison.Ordinal);
        Assert.Contains("for ($attempt = 1; $attempt -le 3; $attempt++)", script, StringComparison.Ordinal);
        Assert.Contains("git -C $checkout push origin", script, StringComparison.Ordinal);
        Assert.DoesNotContain("upload-artifact", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_ShouldHaveOneBackupImplementation()
    {
        Assert.False(File.Exists(GetRepoPath(".github", "workflows", "powerforge-server-backup.yml")));
        Assert.True(File.Exists(GetRepoPath(".github", "actions", "powerforge-server-backup", "Invoke-PowerForgeServerBackup.ps1")));
    }

    private static string ReadRepoFile(params string[] relativePath)
        => File.ReadAllText(GetRepoPath(relativePath));

    private static string GetRepoPath(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return Path.Combine([current.FullName, .. relativePath]);
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
