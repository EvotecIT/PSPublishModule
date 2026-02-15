using System;
using System.Diagnostics;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public class WebCliGitSyncTests
{
    [Fact]
    public void HandleSubCommand_GitSync_ClonesRepository()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-git-sync-clone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "hello from source");

            RunGit(source, "init");
            RunGit(source, "config", "user.email", "pf-tests@example.local");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "init", "--quiet");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "git-sync",
                new[]
                {
                    "--repo", source,
                    "--destination", checkout
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(checkout, "README.md")));
            Assert.Equal("hello from source", File.ReadAllText(Path.Combine(checkout, "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_GitSync_SpecMode_ClonesRepository()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-git-sync-spec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "hello from spec");

            RunGit(source, "init");
            RunGit(source, "config", "user.email", "pf-tests@example.local");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "init", "--quiet");

            var specPath = Path.Combine(root, "git-sync.json");
            File.WriteAllText(specPath,
                $$"""
                {
                  "repo": "{{EscapeJson(source)}}",
                  "destination": "./checkout"
                }
                """);

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "git-sync",
                new[] { "--spec", specPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.Equal("hello from spec", File.ReadAllText(Path.Combine(root, "checkout", "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_GitSync_FailsWhenDestinationMissing()
    {
        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "git-sync",
            new[] { "--repo", "EvotecIT/IntelligenceX" },
            outputJson: true,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    private static bool IsGitAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}{Environment.NewLine}{stdout}");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
