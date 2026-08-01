using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void AppleActionPersistsActionableReceiptWhenCliPreflightFailsBeforeEngineReceipt()
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"apple-preflight-receipt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sandbox);
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var receiptPath = Path.Combine(sandbox, "build", "powerforge", "apple", "release-receipt.json");
            var outputPath = Path.Combine(sandbox, "github-output.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(receiptPath, """{"success":true,"action":"Status","stale":true}""");
            File.WriteAllText(
                configPath,
                """{"AppleApps":{"ProjectRoot":".","Automation":{"ReceiptPath":"build/powerforge/apple/release-receipt.json"}}}""");

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
                ["INPUT_PLAN_ONLY"] = "false",
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

    private static string CreateFailingTool(string directory, string envelope)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(directory, "failing-tool.cmd");
            File.WriteAllText(path, $"@echo off{Environment.NewLine}echo {envelope}{Environment.NewLine}exit /b 1{Environment.NewLine}");
            return path;
        }

        var shellPath = Path.Combine(directory, "failing-tool.sh");
        File.WriteAllText(shellPath, $"#!/bin/sh{Environment.NewLine}printf '%s\\n' '{envelope}'{Environment.NewLine}exit 1{Environment.NewLine}");
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
