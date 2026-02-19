using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebCliEngineLockTests
{
    [Fact]
    public void HandleSubCommand_EngineLock_Update_WritesLockFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-engine-lock-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var lockPath = Path.Combine(root, ".powerforge", "engine-lock.json");
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "engine-lock",
                new[]
                {
                    "--path", lockPath,
                    "--mode", "update",
                    "--repository", "EvotecIT/PSPublishModule",
                    "--ref", "0123456789abcdef",
                    "--channel", "candidate"
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(lockPath));

            using var doc = JsonDocument.Parse(File.ReadAllText(lockPath));
            var rootElement = doc.RootElement;
            Assert.Equal("EvotecIT/PSPublishModule", rootElement.GetProperty("repository").GetString());
            Assert.Equal("0123456789abcdef", rootElement.GetProperty("ref").GetString());
            Assert.Equal("candidate", rootElement.GetProperty("channel").GetString());
            Assert.False(string.IsNullOrWhiteSpace(rootElement.GetProperty("updatedUtc").GetString()));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_EngineLock_Verify_FailsOnDrift()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-engine-lock-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var lockPath = Path.Combine(root, ".powerforge", "engine-lock.json");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            File.WriteAllText(lockPath,
                """
                {
                  "repository": "EvotecIT/PSPublishModule",
                  "ref": "deadbeef",
                  "channel": "stable",
                  "updatedUtc": "2026-02-19T00:00:00.0000000+00:00"
                }
                """);

            var ok = WebCliCommandHandlers.HandleSubCommand(
                "engine-lock",
                new[] { "--path", lockPath, "--mode", "verify", "--repository", "EvotecIT/PSPublishModule", "--ref", "deadbeef" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);
            Assert.Equal(0, ok);

            var fail = WebCliCommandHandlers.HandleSubCommand(
                "engine-lock",
                new[] { "--path", lockPath, "--mode", "verify", "--ref", "cafebabe" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);
            Assert.Equal(1, fail);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_EngineLock_UsesConfigRoot_ForDefaultPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-engine-lock-config-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteConfig = Path.Combine(root, "site.json");
            File.WriteAllText(siteConfig,
                """
                {
                  "name": "test"
                }
                """);

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "engine-lock",
                new[] { "--config", siteConfig, "--mode", "update", "--ref", "b16b00b5" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            var expectedLockPath = Path.Combine(root, ".powerforge", "engine-lock.json");
            Assert.True(File.Exists(expectedLockPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_EngineLock_Verify_RequireImmutableRef_FailsForNonShaRef()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-engine-lock-verify-immutable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var lockPath = Path.Combine(root, ".powerforge", "engine-lock.json");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            File.WriteAllText(lockPath,
                """
                {
                  "repository": "EvotecIT/PSPublishModule",
                  "ref": "main",
                  "channel": "stable",
                  "updatedUtc": "2026-02-19T00:00:00.0000000+00:00"
                }
                """);

            var fail = WebCliCommandHandlers.HandleSubCommand(
                "engine-lock",
                new[] { "--path", lockPath, "--mode", "verify", "--require-immutable-ref" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);
            Assert.Equal(1, fail);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
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
}
