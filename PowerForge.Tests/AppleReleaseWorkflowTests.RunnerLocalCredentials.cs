using System.Text.Json;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    private const string TestIssuerId = "12345678-1234-1234-1234-123456789abc";
    private const string TestKeyId = "ABC123DEFG";

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
