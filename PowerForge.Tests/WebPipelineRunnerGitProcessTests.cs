using System;
using System.Diagnostics;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerGitProcessTests
{
    [Fact]
    public void RunGitCommand_DrainsHighVolumeOutputBeforeReturning()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-git-output-drain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var scriptPath = Path.Combine(root, "emit-output.sh");
            File.WriteAllText(scriptPath,
                """
                #!/bin/sh
                i=0
                while [ "$i" -lt 10000 ]; do
                  printf 'stdout-%05d\n' "$i"
                  printf 'stderr-%05d\n' "$i" >&2
                  i=$((i + 1))
                done
                printf 'stdout-complete\n'
                printf 'stderr-complete\n' >&2
                """);

            var portableScriptPath = scriptPath.Replace('\\', '/');
            var result = WebPipelineRunner.RunGitCommand(
                root,
                new[]
                {
                    "-c",
                    $"alias.powerforge-output=!sh \"{portableScriptPath}\"",
                    "powerforge-output"
                },
                authHeader: null,
                timeoutSeconds: 60);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(10001, result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Equal(10001, result.Error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.EndsWith("stdout-complete", result.Output, StringComparison.Ordinal);
            Assert.EndsWith("stderr-complete", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for transient test files.
            }
        }
    }

    private static bool IsGitAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
