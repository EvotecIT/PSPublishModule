namespace PowerForge.Tests;

public sealed class GitHubServerRecoveryValidationActionTests
{
    [Fact]
    public void Action_ShouldBeCredentialFreeAndUsePinnedDependencies()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-server-recovery-validate", "action.yml");

        Assert.Contains("manifest-path:", action, StringComparison.Ordinal);
        Assert.Contains("capture-user:", action, StringComparison.Ordinal);
        Assert.Contains("fail-on-warnings:", action, StringComparison.Ordinal);
        Assert.DoesNotContain("private-key", action, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("known-hosts", action, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("${{ secrets.", action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd", action, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", action, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@c2fa09f4bde5ebb9d1777cf28262a3eb3db3ced7", action, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_ENGINE_REF: ${{ github.action_ref }}", action, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_ENGINE_REPOSITORY: ${{ github.action_repository }}", action, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_CAPTURE_USER: ${{ inputs.capture-user }}", action, StringComparison.Ordinal);
        Assert.DoesNotContain("run: |", action, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ShouldGenerateAndLintPlansWithoutExecutingRecoveryCommands()
    {
        var script = ReadRepoFile(".github", "actions", "powerforge-server-recovery-validate", "Invoke-PowerForgeServerRecoveryValidation.ps1");

        Assert.Contains("server', 'bootstrap-plan", script, StringComparison.Ordinal);
        Assert.Contains("server', 'restore-secrets-plan", script, StringComparison.Ordinal);
        Assert.Contains("'-n', '--', $bootstrapScript", script, StringComparison.Ordinal);
        Assert.Contains("'-S', 'warning', '--', $bootstrapScript", script, StringComparison.Ordinal);
        Assert.Contains("No generated command was executed", script, StringComparison.Ordinal);
        Assert.DoesNotContain("server', 'capture", script, StringComparison.Ordinal);
        Assert.DoesNotContain("server', 'deploy", script, StringComparison.Ordinal);
        Assert.DoesNotContain("server', 'verify", script, StringComparison.Ordinal);
        Assert.DoesNotContain("server', 'inspect", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-WebRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-RestMethod", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_ShouldVerifyPinnedManagedSourcesAndEncryptedCaptureAuthorization()
    {
        var entrypoint = ReadRepoFile(".github", "actions", "powerforge-server-recovery-validate", "Invoke-PowerForgeServerRecoveryValidation.ps1");
        var sourceValidation = ReadRepoFile(".github", "actions", "powerforge-server-recovery-validate", "Assert-PowerForgeServerRecoverySources.ps1");

        Assert.Contains("Assert-PowerForgeServerRecoverySources.ps1", entrypoint, StringComparison.Ordinal);
        Assert.Contains("-CallerRepository $env:GITHUB_REPOSITORY", entrypoint, StringComparison.Ordinal);
        Assert.Contains("-CaptureUser $env:POWERFORGE_CAPTURE_USER", entrypoint, StringComparison.Ordinal);
        Assert.Contains("Resolve-ManagedSourcePath", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("git -C $root ls-tree $repositoryRef -- $relativePath", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("$Matches['mode'] -notin @('100644', '100755')", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("git -C $root hash-object -- $candidate", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("differs from its pinned repository commit", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("Managed recovery source must not traverse a symbolic link", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("/usr/local/sbin/powerforge-server-encrypted-capture", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("Cmnd_Alias", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("NOPASSWD:", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("exact hardened encrypted-capture command", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("$commands.Count -ne 1", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("$CaptureUser", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("$expectedHelperSource", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("$expectedHelperPath", sourceValidation, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-WebRequest", sourceValidation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-RestMethod", sourceValidation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ssh ", sourceValidation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_ShouldPinEngineAndBoundCallerAndTemporaryPaths()
    {
        var script = ReadRepoFile(".github", "actions", "powerforge-server-recovery-validate", "Invoke-PowerForgeServerRecoveryValidation.ps1");

        Assert.Contains("POWERFORGE_ENGINE_REF -notmatch '^[a-fA-F0-9]{40}$'", script, StringComparison.Ordinal);
        Assert.Contains("Test-Json -SchemaFile $schemaPath -ErrorAction Stop", script, StringComparison.Ordinal);
        Assert.Contains("$manifest.'$schema'", script, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_ENGINE_REPOSITORY)/$($env:POWERFORGE_ENGINE_REF)", script, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_CAPTURE_USER -notmatch '^[a-z_][a-z0-9_-]{0,31}$'", script, StringComparison.Ordinal);
        Assert.Contains("IsPathRooted($env:POWERFORGE_MANIFEST_PATH)", script, StringComparison.Ordinal);
        Assert.Contains("manifestPath.StartsWith($workspacePrefix", script, StringComparison.Ordinal);
        Assert.Contains("[IO.Path]::GetRelativePath($Root, $Path)", script, StringComparison.Ordinal);
        Assert.Contains("$item.LinkType -eq 'SymbolicLink'", script, StringComparison.Ordinal);
        Assert.Contains("[IO.FileAttributes]::ReparsePoint", script, StringComparison.Ordinal);
        Assert.Contains("Assert-PathHasNoSymbolicLink -Root $workspace -Path $manifestPath", script, StringComparison.Ordinal);
        Assert.Contains("validationRoot.StartsWith($runnerTempPrefix", script, StringComparison.Ordinal);
        Assert.Contains("[Guid]::NewGuid().ToString('N')", script, StringComparison.Ordinal);
        Assert.Contains("'--artifacts-path', $artifactsRoot", script, StringComparison.Ordinal);
        Assert.Contains("$artifactsRoot 'bin/PowerForge.Web.Cli/release/PowerForge.Web.Cli.dll'", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $validationRoot -Recurse -Force", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ShouldNotPrintManifestOrCliDiagnostics()
    {
        var script = ReadRepoFile(".github", "actions", "powerforge-server-recovery-validate", "Invoke-PowerForgeServerRecoveryValidation.ps1");

        Assert.DoesNotContain("Write-Host $manifest", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Write-Host $result.Stdout", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Write-Host $result.Stderr", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run the pinned PowerForge CLI locally for detailed diagnostics", script, StringComparison.Ordinal);
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
