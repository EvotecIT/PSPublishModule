using System.Text.Json;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    private const string TestIssuerId = "12345678-1234-1234-1234-123456789abc";
    private const string TestKeyId = "ABC123DEFG";

    [Fact]
    public void PinnedLocalOperatorRequiresExactToolAndCleanMergedConsumerSources()
    {
        var root = FindRepoRoot();
        var script = Read(root, "scripts", "Invoke-PinnedPowerForge.ps1");
        var evidence = Read(root, "scripts", "Invoke-PinnedPowerForge.Evidence.ps1");
        Assert.Contains("^(?:[0-9A-Fa-f]{40}|[0-9A-Fa-f]{64})$", script, StringComparison.Ordinal);
        Assert.Contains("RequiredCommit $ExpectedCommit", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedConsumerRepository", script, StringComparison.Ordinal);
        Assert.Contains("symbolic-ref', '--short', 'HEAD", script, StringComparison.Ordinal);
        Assert.Contains("fetch', '--quiet', 'origin', $RequiredBranch", script, StringComparison.Ordinal);
        Assert.Contains("refs/remotes/origin/$RequiredBranch", script, StringComparison.Ordinal);
        Assert.Contains("status', '--porcelain=v1', '--untracked-files=all", script, StringComparison.Ordinal);
        Assert.Contains("-DeferContentCheck", script, StringComparison.Ordinal);
        Assert.Contains("ls-files', '--others', '--ignored', '--exclude-standard", evidence, StringComparison.Ordinal);
        Assert.Contains("Consumer source contains non-reviewed content", evidence, StringComparison.Ordinal);
        Assert.Contains("GIT_NO_REPLACE_OBJECTS", script, StringComparison.Ordinal);
        Assert.Contains("for-each-ref', '--format=%(refname)', 'refs/replace", script, StringComparison.Ordinal);
        Assert.Contains("core.fsmonitor=false", script, StringComparison.Ordinal);
        Assert.Contains("ls-files', '--error-unmatch'", script, StringComparison.Ordinal);
        Assert.Contains("'restore', $cliProject, '--locked-mode', '--packages', $nugetPackages, '--artifacts-path', $artifactsRoot", script, StringComparison.Ordinal);
        Assert.Contains("-WorkingDirectory $buildToolRoot", script, StringComparison.Ordinal);
        Assert.Contains("-WorkingDirectory $consumer", script, StringComparison.Ordinal);
        Assert.Contains("$start.Environment['NUGET_PACKAGES'] = $NuGetPackagesPath", script, StringComparison.Ordinal);
        Assert.Contains("archive --format=tar --output=$archivePath HEAD", script, StringComparison.Ordinal);
        Assert.Contains("$buildToolRoot = New-TrackedToolSnapshot", script, StringComparison.Ordinal);
        Assert.Contains("build snapshot must not contain symbolic links or reparse points", script, StringComparison.Ordinal);
        Assert.Contains("$savedCredentialEnvironment = Suspend-AppleCredentialEnvironment", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Restore-AppleCredentialEnvironment", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("$savedCredentialEnvironment = Suspend-AppleCredentialEnvironment", StringComparison.Ordinal) <
            script.IndexOf("Assert-CleanRepository -Root $toolRoot", StringComparison.Ordinal));
        Assert.Contains("run download $runId", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires an explicit --config", script, StringComparison.Ordinal);
        Assert.Contains("'apple-review-details'", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-TrackedInputValidator -SourceCommit $consumerHead", script, StringComparison.Ordinal);
        Assert.Contains("Get-RedactedToolText -Text ($stdout.GetAwaiter().GetResult())", script, StringComparison.Ordinal);
        Assert.Contains("appStoreConnectApi(?:KeyPath|KeyId|IssuerId)", script, StringComparison.Ordinal);
        Assert.Contains("Get-Content -LiteralPath $keyPath -Raw", script, StringComparison.Ordinal);
        Assert.Contains("[Console]::Error.Write($safeStdErr)", script, StringComparison.Ordinal);
        Assert.Contains("Assert-FixedLocalCredentialProfile", script, StringComparison.Ordinal);
        Assert.Contains("$run.event -ne 'workflow_dispatch'", script, StringComparison.Ordinal);
        Assert.Contains("$run.path -ne $workflowMatch.Groups['path'].Value", script, StringComparison.Ordinal);
        Assert.Contains("$run.head_repository.full_name -ne $repository", script, StringComparison.Ordinal);
        Assert.Contains("UploadExisting is forbidden at the pinned local operator boundary", script, StringComparison.Ordinal);
        Assert.Contains("if ($null -ne (Get-OptionValue -Option '--capture-provenance'))", script, StringComparison.Ordinal);
        Assert.Contains("Capture provenance source commit", script, StringComparison.Ordinal);
        Assert.Contains("--apple-source-commit must match the exact consumer HEAD", evidence, StringComparison.Ordinal);
        Assert.Contains("Assert-ScreenshotPublicationBinding -SourceCommit $consumerHead", script, StringComparison.Ordinal);
        Assert.Contains("if ($ArgumentList[0] -ne 'apple-release' -or", evidence, StringComparison.Ordinal);
        Assert.Contains("$argument -eq '--capture-provenance'", evidence, StringComparison.Ordinal);
        Assert.Contains("$forwardedArgumentList = Get-ForwardedArgumentList -SourceCommit $consumerHead", script, StringComparison.Ordinal);
        Assert.Contains("Screenshot approval manifests do not identify one exact retained capture root and inventory", evidence, StringComparison.Ordinal);
        Assert.Contains("Resolve-PathFromBase -BasePath", evidence, StringComparison.Ordinal);
        Assert.Contains("No screenshot configuration matches the selected release targets", evidence, StringComparison.Ordinal);
        Assert.Contains("permissions must not grant group or other access", script, StringComparison.Ordinal);
        Assert.Contains("must not grant access through a POSIX ACL", script, StringComparison.Ordinal);
        Assert.Contains("must not have hard links", script, StringComparison.Ordinal);
        Assert.Contains("[Diagnostics.ProcessStartInfo]::new()", script, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError = $true", script, StringComparison.Ordinal);
        Assert.Contains("$start.Environment.Clear()", script, StringComparison.Ordinal);
        Assert.Contains("-IncludeAppleCredentials", script, StringComparison.Ordinal);
        Assert.Contains("Assert-FixedAppleToolConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("$script:validatedReleaseConfigPaths", script, StringComparison.Ordinal);
        Assert.Contains("if ($command -eq 'apple-release' -and $config)", script, StringComparison.Ordinal);
        Assert.Contains("Get-OptionValue -Option '--release-config'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$script:validatedConfigPaths", script, StringComparison.Ordinal);
        Assert.Contains("/usr/bin/xcodebuild", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[string] $DotNet =", script, StringComparison.Ordinal);

        var captureWorkflow = Read(root, ".github", "workflows", "powerforge-apple-screenshot-capture.yml");
        var screenshotWorkflow = Read(root, ".github", "workflows", "powerforge-apple-screenshots.yml");
        var approvalWorkflow = Read(root, ".github", "workflows", "powerforge-apple-screenshot-approve.yml");
        foreach (var workflow in new[] { captureWorkflow, screenshotWorkflow, approvalWorkflow })
        {
            Assert.Contains("marketing_version:\n        description: Exact x.y or x.y.z", workflow, StringComparison.Ordinal);
            Assert.Contains("marketing_version must use x.y or x.y.z", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("marketing_version must be blank", workflow, StringComparison.Ordinal);
        }
        Assert.Contains("'^\\d+\\.\\d+(?:\\.\\d+)?$'", captureWorkflow, StringComparison.Ordinal);
        Assert.Contains("'^\\d+\\.\\d+(?:\\.\\d+)?$'", screenshotWorkflow, StringComparison.Ordinal);
        Assert.Contains("^[0-9]+\\.[0-9]+(\\.[0-9]+)?$", approvalWorkflow, StringComparison.Ordinal);
        Assert.Contains("'^\\d+\\.\\d+(?:\\.\\d+)?$'", approvalWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenshotCaptureProvenanceInventoriesExactPngBytesAndDimensions()
    {
        var root = FindRepoRoot();
        var workflow = Read(root, ".github", "workflows", "powerforge-apple-screenshot-capture.yml");
        Assert.Contains("Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256", workflow, StringComparison.Ordinal);
        Assert.Contains("sips -g pixelWidth -g pixelHeight", workflow, StringComparison.Ordinal);
        Assert.Contains("path = [IO.Path]::GetRelativePath($artifactRoot, $_.FullName)", workflow, StringComparison.Ordinal);
        Assert.Contains("screenshots = $screenshots", workflow, StringComparison.Ordinal);
        Assert.Contains("CAPTURE_RUNTIME: ${{ inputs.runtime }}", workflow, StringComparison.Ordinal);
        Assert.Contains("$captureRuntime = $env:CAPTURE_RUNTIME.Trim()", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("sw_vers", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackedReleaseInputValidatorRejectsAnIgnoredAppleProject()
    {
        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"powerforge-tracked-project-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, ".powerforge"));
            var project = Directory.CreateDirectory(Path.Combine(sandbox, "Ignored.xcodeproj"));
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var manifestPath = Path.Combine(sandbox, ".powerforge", "powerforge.tool.json");
            File.WriteAllText(configPath,
                """{ "AppleApps": { "ProjectRoot": ".", "Apps": [ { "ProjectPath": "Ignored.xcodeproj" } ] } }""");
            File.WriteAllText(manifestPath, "{}");
            File.WriteAllText(Path.Combine(sandbox, ".gitignore"), "Ignored.xcodeproj/\n");
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), "// ignored project");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", "powerforge.release.json", ".powerforge/powerforge.tool.json", ".gitignore").EnsureSuccess();
            Run(
                "git",
                sandbox,
                "-c", "user.name=PowerForge Tests",
                "-c", "user.email=powerforge-tests@example.invalid",
                "commit", "--quiet", "-m", "Tracked release project").EnsureSuccess();
            var commit = Run("git", sandbox, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();
            var validator = Path.Combine(
                root,
                ".github",
                "actions",
                "apple-release",
                "Assert-TrackedAppleReleaseInputs.ps1");

            var result = Run(
                "pwsh",
                sandbox,
                "-NoProfile",
                "-File", validator,
                "-ConfigPath", configPath,
                "-ToolManifestPath", manifestPath,
                "-SourceCommit", commit);

            Assert.NotEqual(0, result.ExitCode);
            var output = result.StandardOutput + result.StandardError;
            Assert.True(
                output.Contains("AppleApps.Apps.ProjectPath/project.pbxproj", StringComparison.OrdinalIgnoreCase) &&
                output.Contains("must be tracked at the exact source", StringComparison.OrdinalIgnoreCase),
                output);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void TrackedReleaseInputValidatorAcceptsWorkspaceMetadataAndSkipsDisabledTargets()
    {
        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"powerforge-tracked-workspace-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, ".powerforge"));
            var workspace = Directory.CreateDirectory(Path.Combine(sandbox, "Sample.xcworkspace"));
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var manifestPath = Path.Combine(sandbox, ".powerforge", "powerforge.tool.json");
            File.WriteAllText(
                configPath,
                """{ "AppleApps": { "ProjectRoot": ".", "Apps": [ { "ProjectPath": "Sample.xcworkspace" }, { "Enabled": false, "ProjectPath": "Removed.xcodeproj" } ] } }""");
            File.WriteAllText(manifestPath, "{}");
            File.WriteAllText(Path.Combine(workspace.FullName, "contents.xcworkspacedata"), "<Workspace version=\"1.0\" />");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", ".").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Tracked workspace");
            var commit = Run("git", sandbox, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();

            var result = RunTrackedReleaseInputValidator(root, sandbox, configPath, manifestPath, commit);

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void TrackedReleaseInputValidatorUsesNestedProjectGenerationSource()
    {
        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"powerforge-tracked-generation-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, ".powerforge"));
            Directory.CreateDirectory(Path.Combine(sandbox, "ios"));
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var manifestPath = Path.Combine(sandbox, ".powerforge", "powerforge.tool.json");
            File.WriteAllText(
                configPath,
                """{ "AppleApps": { "ProjectRoot": ".", "Apps": [ { "ProjectPath": "ios/App.xcodeproj", "GenerateProjectIfMissing": true } ] } }""");
            File.WriteAllText(manifestPath, "{}");
            File.WriteAllText(Path.Combine(sandbox, "ios", "project.yml"), "name: App\n");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", "powerforge.release.json", ".powerforge/powerforge.tool.json").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Tracked config without generation input");
            var untrackedCommit = Run("git", sandbox, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();

            var rejected = RunTrackedReleaseInputValidator(root, sandbox, configPath, manifestPath, untrackedCommit);

            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Contains("ios/project.yml", rejected.StandardOutput + rejected.StandardError, StringComparison.OrdinalIgnoreCase);
            Run("git", sandbox, "add", "ios/project.yml").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Track nested generation input");
            var trackedCommit = Run("git", sandbox, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();

            var accepted = RunTrackedReleaseInputValidator(root, sandbox, configPath, manifestPath, trackedCommit);

            Assert.Equal(0, accepted.ExitCode);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Theory]
    [InlineData("AppStoreConnectApiKeyPath", ".appstoreconnect/AuthKey_CONFIG.p8")]
    [InlineData("AppStoreConnectApiKeyId", "CONFIGKEY1")]
    [InlineData("AppStoreConnectApiIssuerId", "00000000-0000-0000-0000-000000000000")]
    public void TrackedReleaseInputValidatorRejectsCredentialOverrides(string propertyName, string value)
    {
        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"powerforge-tracked-credential-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, ".powerforge"));
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var manifestPath = Path.Combine(sandbox, ".powerforge", "powerforge.tool.json");
            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(new
                {
                    AppleApps = new Dictionary<string, object?>
                    {
                        ["ProjectRoot"] = ".",
                        [propertyName] = value
                    }
                }));
            File.WriteAllText(manifestPath, "{}");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", ".").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Tracked credential override");
            var commit = Run("git", sandbox, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();

            var result = RunTrackedReleaseInputValidator(root, sandbox, configPath, manifestPath, commit, rejectCredentialOverrides: true);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains($"AppleApps.{propertyName} is forbidden", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void TrackedReleaseInputValidatorRejectsNotarytoolKeychainProfileOverride()
    {
        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"powerforge-keychain-profile-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, ".powerforge"));
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var manifestPath = Path.Combine(sandbox, ".powerforge", "powerforge.tool.json");
            File.WriteAllText(configPath,
                """{ "AppleApps": { "ProjectRoot": ".", "DirectDistribution": { "KeychainProfile": "unreviewed-notary-profile" } } }""");
            File.WriteAllText(manifestPath, "{}");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", ".").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Tracked notary credential override");
            var commit = Run("git", sandbox, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();

            var result = RunTrackedReleaseInputValidator(root, sandbox, configPath, manifestPath, commit, rejectCredentialOverrides: true);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("AppleApps.DirectDistribution.KeychainProfile is forbidden", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void RunnerLocalDoctorUsesPrivateProfileWithoutSerializingCredentials()
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var toolPath = CreateSuccessfulCredentialProbe(sandbox);
            var result = RunRunnerLocalWrapper(root, sandbox, toolPath, "Doctor");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(sandbox, "credential-proof.txt")), result.StandardOutput + result.StandardError);
            var combined = result.StandardOutput + result.StandardError +
                           File.ReadAllText(Path.Combine(sandbox, "github-output.txt"));
            Assert.DoesNotContain(TestIssuerId, combined, StringComparison.Ordinal);
            Assert.DoesNotContain(TestKeyId, combined, StringComparison.Ordinal);
            Assert.DoesNotContain("AuthKey_ABC123DEFG.p8", combined, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Theory]
    [InlineData("Release", "self-hosted", "macOS")]
    [InlineData("Doctor", "github-hosted", "macOS")]
    [InlineData("Doctor", "self-hosted", "Linux")]
    public void RunnerLocalCredentialsFailBeforeToolOutsideReadOnlyPrivateMacBoundary(
        string action,
        string runnerEnvironment,
        string runnerOs)
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var toolPath = CreateSuccessfulCredentialProbe(sandbox);
            var result = RunRunnerLocalWrapper(root, sandbox, toolPath, action, runnerEnvironment, runnerOs);

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(sandbox, "credential-proof.txt")));
            AssertRunnerLocalFailureReceipt(sandbox);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void RunnerLocalCredentialsRejectActionCredentialMixing()
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var toolPath = CreateSuccessfulCredentialProbe(sandbox);
            var result = RunRunnerLocalWrapper(
                root,
                sandbox,
                toolPath,
                "Doctor",
                additionalEnvironment: new Dictionary<string, string?>
                {
                    ["APP_STORE_CONNECT_KEY_ID"] = TestKeyId
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(sandbox, "credential-proof.txt")));
            AssertRunnerLocalFailureReceipt(sandbox);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Theory]
    [InlineData("AppStoreConnectApiKeyPath", ".appstoreconnect/AuthKey_CONFIG.p8")]
    [InlineData("AppStoreConnectApiKeyId", "CONFIGKEY1")]
    [InlineData("AppStoreConnectApiIssuerId", "00000000-0000-0000-0000-000000000000")]
    public void RunnerLocalCredentialsRejectEveryReleaseConfigCredentialField(string name, string value)
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(new
                {
                    AppleApps = new Dictionary<string, object?>
                    {
                        ["ProjectRoot"] = ".",
                        ["Automation"] = new { ReceiptPath = "build/powerforge/apple/release-receipt.json" },
                        [name] = value
                    }
                }));

            var result = RunRunnerLocalWrapper(root, sandbox, CreateSuccessfulCredentialProbe(sandbox), "Doctor");

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(sandbox, "credential-proof.txt")));
            AssertRunnerLocalFailureReceipt(sandbox);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void RunnerLocalCredentialsRetainStructuredFailureWhenHomeIsMissing()
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var result = RunRunnerLocalWrapper(
                root,
                sandbox,
                CreateSuccessfulCredentialProbe(sandbox),
                "Doctor",
                additionalEnvironment: new Dictionary<string, string?> { ["HOME"] = string.Empty });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(sandbox, "credential-proof.txt")));
            AssertRunnerLocalFailureReceipt(sandbox);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void RunnerLocalDoctorRedactsCredentialMaterialFromFailureHandoffs()
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var keyPath = Path.Combine(sandbox, "runner-home", ".appstoreconnect", "AuthKey_ABC123DEFG.p8");
            var privateKeyBodyLine = File.ReadLines(keyPath).First(line => !line.StartsWith("-----", StringComparison.Ordinal));
            var result = RunRunnerLocalWrapper(root, sandbox, CreateCredentialLeakingTool(sandbox), "Doctor");

            Assert.NotEqual(0, result.ExitCode);
            var receipt = File.ReadAllText(Path.Combine(sandbox, "build", "powerforge", "apple", "release-receipt.json"));
            var outputs = File.ReadAllText(Path.Combine(sandbox, "github-output.txt"));
            var handoff = result.StandardOutput + result.StandardError + receipt + outputs;
            Assert.DoesNotContain(TestIssuerId, handoff, StringComparison.Ordinal);
            Assert.DoesNotContain(TestKeyId, handoff, StringComparison.Ordinal);
            Assert.DoesNotContain(keyPath, handoff, StringComparison.Ordinal);
            Assert.DoesNotContain(privateKeyBodyLine, handoff, StringComparison.Ordinal);
            Assert.Contains("[redacted]", receipt, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void RunnerLocalCredentialsRejectUnsupportedProfileContent()
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var envPath = Path.Combine(sandbox, "runner-home", ".appstoreconnect", "env");
            File.AppendAllText(envPath, "echo unsupported\n");
            var result = RunRunnerLocalWrapper(root, sandbox, CreateSuccessfulCredentialProbe(sandbox), "Doctor");

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(sandbox, "credential-proof.txt")));
            AssertRunnerLocalFailureReceipt(sandbox);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void RunnerLocalCredentialsRejectLiteralAliasCopies()
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var envPath = Path.Combine(sandbox, "runner-home", ".appstoreconnect", "env");
            File.WriteAllText(
                envPath,
                File.ReadAllText(envPath).Replace(
                    "export ASC_KEY_ID=\"$APP_STORE_CONNECT_KEY_ID\"",
                    $"export ASC_KEY_ID=\"{TestKeyId}\"",
                    StringComparison.Ordinal));
            var result = RunRunnerLocalWrapper(root, sandbox, CreateSuccessfulCredentialProbe(sandbox), "Doctor");

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(sandbox, "credential-proof.txt")));
            AssertRunnerLocalFailureReceipt(sandbox);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("permissive-profile")]
    [InlineData("linked-key")]
    [InlineData("hardlinked-key")]
    [InlineData("fifo-key")]
    [InlineData("invalid-pem")]
    [InlineData("public-key-pem")]
    [InlineData("sec1-key-pem")]
    public void RunnerLocalCredentialsRejectUnsafeLocalMaterial(string unsafeShape)
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var keySetting = unsafeShape == "outside"
            ? "$HOME/outside/AuthKey_ABC123DEFG.p8"
            : "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8";
        var sandbox = CreateRunnerLocalSandbox(root, keySetting);
        try
        {
            var home = Path.Combine(sandbox, "runner-home");
            if (unsafeShape == "outside")
            {
                var outside = Path.Combine(home, "outside");
                Directory.CreateDirectory(outside);
                SetUnixMode(outside, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var outsideKey = Path.Combine(outside, "AuthKey_ABC123DEFG.p8");
                File.WriteAllText(outsideKey, "test-key");
                SetUnixMode(outsideKey, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            else if (unsafeShape == "permissive-profile")
            {
                SetUnixMode(
                    Path.Combine(home, ".appstoreconnect", "env"),
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }
            else if (unsafeShape == "linked-key")
            {
                var key = Path.Combine(home, ".appstoreconnect", "AuthKey_ABC123DEFG.p8");
                File.Delete(key);
                File.CreateSymbolicLink(key, Path.Combine(sandbox, "outside-key.p8"));
                File.WriteAllText(Path.Combine(sandbox, "outside-key.p8"), "test-key");
            }
            else if (unsafeShape == "hardlinked-key")
            {
                var key = Path.Combine(home, ".appstoreconnect", "AuthKey_ABC123DEFG.p8");
                var outsideKey = Path.Combine(sandbox, "outside-hardlink-source.p8");
                File.WriteAllText(outsideKey, File.ReadAllText(key));
                File.Delete(key);
                Run("ln", sandbox, outsideKey, key).EnsureSuccess();
            }
            else if (unsafeShape == "fifo-key")
            {
                var key = Path.Combine(home, ".appstoreconnect", "AuthKey_ABC123DEFG.p8");
                File.Delete(key);
                Run("mkfifo", sandbox, key).EnsureSuccess();
            }
            else if (unsafeShape == "invalid-pem")
            {
                var key = Path.Combine(home, ".appstoreconnect", "AuthKey_ABC123DEFG.p8");
                File.WriteAllText(key, "-----BEGIN PRIVATE KEY-----\nnot-a-key\n-----END PRIVATE KEY-----\n");
            }
            else if (unsafeShape == "public-key-pem")
            {
                var key = Path.Combine(home, ".appstoreconnect", "AuthKey_ABC123DEFG.p8");
                using var publicKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                File.WriteAllText(key, publicKey.ExportSubjectPublicKeyInfoPem());
            }
            else
            {
                var key = Path.Combine(home, ".appstoreconnect", "AuthKey_ABC123DEFG.p8");
                using var sec1Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                File.WriteAllText(key, sec1Key.ExportECPrivateKeyPem());
            }

            var result = RunRunnerLocalWrapper(root, sandbox, CreateSuccessfulCredentialProbe(sandbox), "Doctor");

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(sandbox, "credential-proof.txt")));
            AssertRunnerLocalFailureReceipt(sandbox);
            var handoff = result.StandardOutput + result.StandardError +
                          File.ReadAllText(Path.Combine(sandbox, "build", "powerforge", "apple", "release-receipt.json"));
            Assert.DoesNotContain("AuthKey_ABC123DEFG.p8", handoff, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    private static string CreateRunnerLocalSandbox(string root, string keySetting)
    {
        var sandbox = Path.Combine(root, ".test-temp", $"runner-local-credentials-{Guid.NewGuid():N}");
        var profile = Path.Combine(sandbox, "runner-home", ".appstoreconnect");
        Directory.CreateDirectory(profile);
        SetUnixMode(profile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var keyPath = Path.Combine(profile, "AuthKey_ABC123DEFG.p8");
        using (var privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            File.WriteAllText(keyPath, privateKey.ExportPkcs8PrivateKeyPem());
        }
        SetUnixMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var envPath = Path.Combine(profile, "env");
        File.WriteAllText(
            envPath,
            $"export APP_STORE_CONNECT_ISSUER_ID=\"{TestIssuerId}\"{Environment.NewLine}" +
            $"export APP_STORE_CONNECT_KEY_ID=\"{TestKeyId}\"{Environment.NewLine}" +
            $"export APP_STORE_CONNECT_PRIVATE_KEY_PATH=\"{keySetting}\"{Environment.NewLine}" +
            "export ASC_ISSUER_ID=\"$APP_STORE_CONNECT_ISSUER_ID\"\n" +
            "export ASC_KEY_ID=\"$APP_STORE_CONNECT_KEY_ID\"\n" +
            "export ASC_PRIVATE_KEY_PATH=\"$APP_STORE_CONNECT_PRIVATE_KEY_PATH\"\n");
        SetUnixMode(envPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(
            Path.Combine(sandbox, "powerforge.release.json"),
            """{"AppleApps":{"ProjectRoot":".","Automation":{"ReceiptPath":"build/powerforge/apple/release-receipt.json"}}}""");
        return sandbox;
    }

    private static void CommitTrackedReleaseSandbox(string sandbox, string message)
    {
        Run(
            "git",
            sandbox,
            "-c", "user.name=PowerForge Tests",
            "-c", "user.email=powerforge-tests@example.invalid",
            "commit", "--quiet", "-m", message).EnsureSuccess();
    }

    private static ProcessResult RunTrackedReleaseInputValidator(
        string root,
        string sandbox,
        string configPath,
        string manifestPath,
        string commit,
        bool rejectCredentialOverrides = false,
        bool allowMissingProject = false)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-File", Path.Combine(root, ".github", "actions", "apple-release", "Assert-TrackedAppleReleaseInputs.ps1"),
            "-ConfigPath", configPath,
            "-ToolManifestPath", manifestPath,
            "-SourceCommit", commit
        };
        if (rejectCredentialOverrides) arguments.Add("-RejectCredentialOverrides");
        if (allowMissingProject) arguments.Add("-AllowMissingProject");
        return Run("pwsh", sandbox, arguments.ToArray());
    }

    private static ProcessResult RunRunnerLocalWrapper(
        string root,
        string sandbox,
        string toolPath,
        string action,
        string runnerEnvironment = "self-hosted",
        string runnerOs = "macOS",
        IReadOnlyDictionary<string, string?>? additionalEnvironment = null)
    {
        var environment = new Dictionary<string, string?>
        {
            ["HOME"] = Path.Combine(sandbox, "runner-home"),
            ["RUNNER_ENVIRONMENT"] = runnerEnvironment,
            ["RUNNER_OS"] = runnerOs,
            ["INPUT_ACTION"] = action,
            ["INPUT_CONFIG_PATH"] = Path.Combine(sandbox, "powerforge.release.json"),
            ["INPUT_MARKETING_VERSION"] = string.Empty,
            ["INPUT_SOURCE_COMMIT"] = new string('a', 40),
            ["INPUT_EXPECTED_PLAN_SHA256"] = string.Empty,
            ["INPUT_TARGET"] = string.Empty,
            ["INPUT_PLAN_ONLY"] = "false",
            ["INPUT_CONFIRM"] = "false",
            ["INPUT_RUNNER_LOCAL_CREDENTIALS"] = "true",
            ["POWERFORGE_TOOL_PATH"] = toolPath,
            ["POWERFORGE_VERSION"] = "test",
            ["APP_STORE_CONNECT_ISSUER_ID"] = string.Empty,
            ["APP_STORE_CONNECT_KEY_ID"] = string.Empty,
            ["APP_STORE_CONNECT_PRIVATE_KEY_PATH"] = string.Empty,
            ["GITHUB_OUTPUT"] = Path.Combine(sandbox, "github-output.txt")
        };
        if (additionalEnvironment is not null)
        {
            foreach (var pair in additionalEnvironment) environment[pair.Key] = pair.Value;
        }
        return RunWithEnvironment(
            "pwsh",
            sandbox,
            environment,
            "-NoProfile",
            "-File",
            Path.Combine(root, ".github", "actions", "apple-release", "Invoke-PowerForgeAppleRelease.ps1"));
    }

    private static string CreateSuccessfulCredentialProbe(string sandbox)
    {
        var path = Path.Combine(sandbox, "credential-probe.sh");
        File.WriteAllText(
            path,
            """
            #!/bin/sh
            set -eu
            [ "$APP_STORE_CONNECT_ISSUER_ID" = "12345678-1234-1234-1234-123456789abc" ]
            [ "$APP_STORE_CONNECT_KEY_ID" = "ABC123DEFG" ]
            [ "$APP_STORE_CONNECT_PRIVATE_KEY_PATH" = "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8" ]
            [ -r "$APP_STORE_CONNECT_PRIVATE_KEY_PATH" ]
            printf 'ok\n' > credential-proof.txt
            mkdir -p build/powerforge/apple
            printf '%s\n' '{"success":true,"action":"Doctor","targets":[],"diagnostics":[],"nextActions":[]}' > build/powerforge/apple/release-receipt.json
            printf '%s\n' '{"success":true,"result":{"action":"Doctor","receiptPath":"build/powerforge/apple/release-receipt.json","targets":[],"diagnostics":[],"nextActions":[]}}'
            """);
        SetUnixMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static string CreateCredentialLeakingTool(string sandbox)
    {
        var path = Path.Combine(sandbox, "credential-leaking-tool.sh");
        File.WriteAllText(
            path,
            """
            #!/bin/sh
            set -eu
            key_body=$(sed -n '2p' "$APP_STORE_CONNECT_PRIVATE_KEY_PATH")
            message="issuer=$APP_STORE_CONNECT_ISSUER_ID key=$APP_STORE_CONNECT_KEY_ID path=$APP_STORE_CONNECT_PRIVATE_KEY_PATH body=$key_body"
            mkdir -p build/powerforge/apple
            printf '{"success":false,"action":"Doctor","errorMessage":"%s","targets":[],"diagnostics":[{"severity":"error","category":"credential","code":"APPLE_TEST_LEAK","summary":"%s","evidence":"%s","action":"%s","retryable":false}],"nextActions":[]}\n' "$message" "$message" "$message" "$message" > build/powerforge/apple/release-receipt.json
            printf '{"success":false,"result":{"action":"Doctor","receiptPath":"build/powerforge/apple/release-receipt.json","errorMessage":"%s","targets":[],"diagnostics":[{"severity":"error","category":"credential","code":"APPLE_TEST_LEAK","summary":"%s","evidence":"%s","action":"%s","retryable":false}],"nextActions":[]}}\n' "$message" "$message" "$message" "$message"
            printf '%s\n' "$message" >&2
            exit 1
            """);
        SetUnixMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static void SetUnixMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, mode);
    }

    private static void AssertRunnerLocalFailureReceipt(string sandbox)
    {
        var receiptPath = Path.Combine(sandbox, "build", "powerforge", "apple", "release-receipt.json");
        Assert.True(File.Exists(receiptPath));
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var diagnostic = Assert.Single(receipt.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("APPLE_RUNNER_LOCAL_CREDENTIALS_INVALID", diagnostic.GetProperty("code").GetString());
    }
}
