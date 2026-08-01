using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppleActionPersistsActionableReceiptWhenCliPreflightFailsBeforeEngineReceipt(bool planOnly)
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"apple-preflight-receipt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sandbox);
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var receiptPath = Path.Combine(
                sandbox,
                "build",
                "powerforge",
                "apple",
                planOnly ? "release-plan.json" : "release-receipt.json");
            var outputPath = Path.Combine(sandbox, "github-output.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(receiptPath, """{"success":true,"action":"Status","stale":true}""");
            File.WriteAllText(
                configPath,
                """{"AppleApps":{"ProjectRoot":".","Automation":{"ReceiptPath":"build/powerforge/apple/release-receipt.json","PlanReceiptPath":"build/powerforge/apple/release-plan.json"}}}""");

            const string errorMessage =
                "AppleApps App Store Connect API-key authentication requires AppStoreConnectApiKeyPath, AppStoreConnectApiKeyId, and AppStoreConnectApiIssuerId.";
            var envelope = JsonSerializer.Serialize(new
            {
                success = false,
                exitCode = 1,
                error = "Apple release workflow failed.",
                result = new { success = false, errorMessage }
            });
            var toolPath = CreateFailingTool(sandbox, envelope);
            var scriptPath = Path.Combine(
                root,
                ".github",
                "actions",
                "apple-release",
                "Invoke-PowerForgeAppleRelease.ps1");
            var environment = new Dictionary<string, string?>
            {
                ["INPUT_ACTION"] = "Doctor",
                ["INPUT_CONFIG_PATH"] = configPath,
                ["INPUT_MARKETING_VERSION"] = string.Empty,
                ["INPUT_SOURCE_COMMIT"] = new string('a', 40),
                ["INPUT_EXPECTED_PLAN_SHA256"] = string.Empty,
                ["INPUT_TARGET"] = string.Empty,
                ["INPUT_PLAN_ONLY"] = planOnly.ToString().ToLowerInvariant(),
                ["INPUT_CONFIRM"] = "false",
                ["POWERFORGE_TOOL_PATH"] = toolPath,
                ["POWERFORGE_VERSION"] = "test",
                ["GITHUB_OUTPUT"] = outputPath
            };

            var result = RunWithEnvironment(
                "pwsh",
                sandbox,
                environment,
                "-NoProfile",
                "-File",
                scriptPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(receiptPath), result.StandardOutput + result.StandardError);
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            var rootElement = receipt.RootElement;
            Assert.False(rootElement.GetProperty("success").GetBoolean());
            Assert.Equal("Doctor", rootElement.GetProperty("action").GetString());
            Assert.False(rootElement.TryGetProperty("stale", out _));
            Assert.Equal(errorMessage, rootElement.GetProperty("errorMessage").GetString());
            var diagnostic = Assert.Single(rootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("APPLE_APP_STORE_CONNECT_CREDENTIALS_MISSING", diagnostic.GetProperty("code").GetString());
            Assert.Equal("credential", diagnostic.GetProperty("category").GetString());
            Assert.Contains("protected Apple environment", diagnostic.GetProperty("action").GetString(), StringComparison.Ordinal);

            var outputs = File.ReadAllText(outputPath);
            Assert.Contains("receipt-path=", outputs, StringComparison.Ordinal);
            Assert.Contains("APPLE_APP_STORE_CONNECT_CREDENTIALS_MISSING", outputs, StringComparison.Ordinal);
            Assert.DoesNotContain(errorMessage, result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void AppleActionReplacesStaleReceiptEvenWhenFailedEnvelopeHasDiagnostics()
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"apple-diagnostic-receipt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sandbox);
            var receiptPath = Path.Combine(sandbox, "build", "powerforge", "apple", "release-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(receiptPath, """{"success":true,"stale":true}""");
            var envelope = JsonSerializer.Serialize(new
            {
                success = false,
                exitCode = 1,
                result = new
                {
                    success = false,
                    errorMessage = "Remote readiness failed.",
                    diagnostics = new[]
                    {
                        new
                        {
                            severity = "error",
                            category = "readiness",
                            code = "APPLE_READINESS_FAILED",
                            summary = "Remote readiness failed.",
                            action = "Correct readiness and retry.",
                            retryable = false
                        }
                    }
                }
            });

            var result = RunAppleWrapper(root, sandbox, CreateFailingTool(sandbox, envelope));

            Assert.NotEqual(0, result.ExitCode);
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            Assert.False(receipt.RootElement.GetProperty("success").GetBoolean());
            Assert.False(receipt.RootElement.TryGetProperty("stale", out _));
            Assert.Equal(
                "APPLE_READINESS_FAILED",
                Assert.Single(receipt.RootElement.GetProperty("diagnostics").EnumerateArray()).GetProperty("code").GetString());
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void AppleActionPreservesReceiptWrittenByFailedInvocationWhenStdoutIsMalformed()
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"apple-engine-receipt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sandbox);
            var receiptPath = Path.Combine(sandbox, "build", "powerforge", "apple", "release-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(receiptPath, """{"success":true,"stale":true}""");
            var currentReceipt = """{"success":false,"action":"Doctor","engineMarker":true,"diagnostics":[]}""";
            var toolPath = CreateFailingTool(sandbox, "not-json", receiptPath, currentReceipt);

            var result = RunAppleWrapper(root, sandbox, toolPath);

            Assert.NotEqual(0, result.ExitCode);
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            Assert.True(receipt.RootElement.GetProperty("engineMarker").GetBoolean());
            Assert.False(receipt.RootElement.TryGetProperty("errorMessage", out _));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void AppleActionNeverWritesFallbackReceiptOutsideProjectRootOrThroughLink()
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"apple-safe-receipt-{Guid.NewGuid():N}");
        var outside = Path.Combine(root, ".test-temp", $"apple-safe-receipt-outside-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sandbox);
            Directory.CreateDirectory(outside);
            var outsideReceipt = Path.Combine(outside, "receipt.json");
            File.WriteAllText(outsideReceipt, "sentinel");
            var configuredPath = outsideReceipt;
            if (!OperatingSystem.IsWindows())
            {
                var linkedDirectory = Path.Combine(sandbox, "linked");
                Directory.CreateSymbolicLink(linkedDirectory, outside);
                configuredPath = Path.Combine(linkedDirectory, "receipt.json");
            }
            File.WriteAllText(
                Path.Combine(sandbox, "powerforge.release.json"),
                JsonSerializer.Serialize(new
                {
                    AppleApps = new
                    {
                        ProjectRoot = ".",
                        Automation = new { ReceiptPath = configuredPath }
                    }
                }));
            var envelope = JsonSerializer.Serialize(new
            {
                success = false,
                exitCode = 1,
                result = new { success = false, errorMessage = "Receipt path is invalid." }
            });

            var result = RunAppleWrapper(root, sandbox, CreateFailingTool(sandbox, envelope), writeConfig: false);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal("sentinel", File.ReadAllText(outsideReceipt));
            var safeReceipt = Path.Combine(sandbox, "build", "powerforge", "apple", "release-receipt.json");
            Assert.True(File.Exists(safeReceipt), result.StandardOutput + result.StandardError);
            using var receipt = JsonDocument.Parse(File.ReadAllText(safeReceipt));
            Assert.Equal("Receipt path is invalid.", receipt.RootElement.GetProperty("errorMessage").GetString());
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppleActionRedirectsUnusableConfiguredReceiptShapesToSafeFallback(bool targetIsDirectory)
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"apple-unusable-receipt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sandbox);
            var configuredPath = targetIsDirectory
                ? "configured-receipt"
                : Path.Combine("blocked-parent", "receipt.json");
            if (targetIsDirectory)
            {
                Directory.CreateDirectory(Path.Combine(sandbox, configuredPath));
            }
            else
            {
                File.WriteAllText(Path.Combine(sandbox, "blocked-parent"), "not-a-directory");
            }
            File.WriteAllText(
                Path.Combine(sandbox, "powerforge.release.json"),
                JsonSerializer.Serialize(new
                {
                    AppleApps = new
                    {
                        ProjectRoot = ".",
                        Automation = new { ReceiptPath = configuredPath }
                    }
                }));
            var envelope = JsonSerializer.Serialize(new
            {
                success = false,
                exitCode = 1,
                result = new { success = false, errorMessage = "Receipt path is unusable." }
            });

            var result = RunAppleWrapper(root, sandbox, CreateFailingTool(sandbox, envelope), writeConfig: false);

            Assert.NotEqual(0, result.ExitCode);
            var safeReceipt = Path.Combine(sandbox, "build", "powerforge", "apple", "release-receipt.json");
            Assert.True(File.Exists(safeReceipt), result.StandardOutput + result.StandardError);
            using var receipt = JsonDocument.Parse(File.ReadAllText(safeReceipt));
            Assert.Equal("Receipt path is unusable.", receipt.RootElement.GetProperty("errorMessage").GetString());
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void AppleActionPreservesFilesystemRootWhenResolvingReceiptPath()
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"apple-root-receipt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sandbox);
            var filesystemRoot = Path.GetPathRoot(sandbox)!;
            var receiptPath = Path.Combine(sandbox, "root-configured-receipt.json");
            var configuredPath = Path.GetRelativePath(filesystemRoot, receiptPath);
            File.WriteAllText(
                Path.Combine(sandbox, "powerforge.release.json"),
                JsonSerializer.Serialize(new
                {
                    AppleApps = new
                    {
                        ProjectRoot = filesystemRoot,
                        Automation = new { ReceiptPath = configuredPath }
                    }
                }));
            var envelope = JsonSerializer.Serialize(new
            {
                success = false,
                exitCode = 1,
                result = new { success = false, errorMessage = "Root receipt test." }
            });

            var result = RunAppleWrapper(root, sandbox, CreateFailingTool(sandbox, envelope), writeConfig: false);

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(receiptPath), result.StandardOutput + result.StandardError);
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            Assert.Equal("Root receipt test.", receipt.RootElement.GetProperty("errorMessage").GetString());
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    private static ProcessResult RunAppleWrapper(
        string root,
        string sandbox,
        string toolPath,
        bool writeConfig = true)
    {
        var configPath = Path.Combine(sandbox, "powerforge.release.json");
        if (writeConfig)
        {
            File.WriteAllText(
                configPath,
                """{"AppleApps":{"ProjectRoot":".","Automation":{"ReceiptPath":"build/powerforge/apple/release-receipt.json"}}}""");
        }
        var outputPath = Path.Combine(sandbox, "github-output.txt");
        var scriptPath = Path.Combine(
            root,
            ".github",
            "actions",
            "apple-release",
            "Invoke-PowerForgeAppleRelease.ps1");
        var environment = new Dictionary<string, string?>
        {
            ["INPUT_ACTION"] = "Doctor",
            ["INPUT_CONFIG_PATH"] = configPath,
            ["INPUT_MARKETING_VERSION"] = string.Empty,
            ["INPUT_SOURCE_COMMIT"] = new string('a', 40),
            ["INPUT_EXPECTED_PLAN_SHA256"] = string.Empty,
            ["INPUT_TARGET"] = string.Empty,
            ["INPUT_PLAN_ONLY"] = "false",
            ["INPUT_CONFIRM"] = "false",
            ["POWERFORGE_TOOL_PATH"] = toolPath,
            ["POWERFORGE_VERSION"] = "test",
            ["GITHUB_OUTPUT"] = outputPath
        };
        return RunWithEnvironment("pwsh", sandbox, environment, "-NoProfile", "-File", scriptPath);
    }

    private static string CreateFailingTool(
        string directory,
        string envelope,
        string? receiptPath = null,
        string? receiptJson = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(directory, "failing-tool.cmd");
            var windowsWriteReceipt = receiptPath is null
                ? string.Empty
                : $"> \"{receiptPath}\" echo {receiptJson}{Environment.NewLine}";
            File.WriteAllText(path, $"@echo off{Environment.NewLine}{windowsWriteReceipt}echo {envelope}{Environment.NewLine}exit /b 1{Environment.NewLine}");
            return path;
        }

        var shellPath = Path.Combine(directory, "failing-tool.sh");
        var escapedReceiptPath = receiptPath?.Replace("'", "'\\''", StringComparison.Ordinal);
        var escapedReceiptJson = receiptJson?.Replace("'", "'\\''", StringComparison.Ordinal);
        var writeReceipt = receiptPath is null
            ? string.Empty
            : $"printf '%s\\n' '{escapedReceiptJson}' > '{escapedReceiptPath}'{Environment.NewLine}";
        File.WriteAllText(shellPath, $"#!/bin/sh{Environment.NewLine}{writeReceipt}printf '%s\\n' '{envelope}'{Environment.NewLine}exit 1{Environment.NewLine}");
        File.SetUnixFileMode(
            shellPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static ProcessResult RunWithEnvironment(
        string fileName,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var pair in environment)
            startInfo.Environment[pair.Key] = pair.Value;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var candidates = OperatingSystem.IsWindows()
            ? new[] { command + ".exe", command + ".cmd", command + ".bat", command }
            : new[] { command };
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => candidates.Any(candidate => File.Exists(Path.Combine(directory, candidate))));
    }
}
