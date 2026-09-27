using System.Diagnostics;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoveryCaptureShellTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureShell_IgnoresOwnedStartupFilesAndPreservesPipelineFailure(bool remote)
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "powerforge-capture-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var maliciousStartup = "touch \"$HOME/startup-loaded\"\nsed() { printf 'hijacked'; }\n";
            foreach (var name in new[] { ".bash_profile", ".bashrc", ".profile", "hook.sh" })
                File.WriteAllText(Path.Combine(root, name), maliciousStartup);
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            var fakeSed = Path.Combine(root, "bin", "sed");
            File.WriteAllText(fakeSed, "#!/bin/sh\nprintf hijacked\n");
            File.SetUnixFileMode(fakeSed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var sha = new string('a', 64);
            var command = $"printf 'engine_commit={sha}\\n' | /usr/bin/sed -n -E 's/^engine_commit=([0-9a-fA-F]{{64}})$/\\1/p'";
            var success = Execute(command);
            Assert.Equal(0, success.Exit);
            Assert.Equal(sha, success.Output.Trim());
            Assert.False(File.Exists(Path.Combine(root, "startup-loaded")));
            Assert.Equal("/usr/bin/sed", Execute("command -v sed").Output.Trim());
            var failed = Execute("false | /usr/bin/sed -n p");
            Assert.Equal(1, failed.Exit);
            Assert.False(File.Exists(Path.Combine(root, "startup-loaded")));

            (int Exit, string Output) Execute(string script)
            {
                var start = new ProcessStartInfo(remote ? "/bin/sh" : "/usr/bin/env")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.Environment["HOME"] = root;
                start.Environment["BASH_ENV"] = Path.Combine(root, "hook.sh");
                start.Environment["ENV"] = Path.Combine(root, "hook.sh");
                start.Environment["BASH_FUNC_sed%%"] = "() { printf 'hijacked'; }";
                start.Environment["BASH_FUNC_/usr/bin/sed%%"] = "() { printf 'hijacked'; }";
                start.Environment["PATH"] = Path.Combine(root, "bin") + ":" + Environment.GetEnvironmentVariable("PATH");
                if (remote)
                {
                    start.ArgumentList.Add("-c");
                    start.ArgumentList.Add(WebCliCommandHandlers.BuildCaptureSshArguments("fixture-target", WebCliCommandHandlers.BuildCaptureShellCommand(script))[^1]);
                }
                else
                    foreach (var argument in WebCliCommandHandlers.BuildCaptureShellArguments(script))
                        start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                Assert.True(process.WaitForExit(10_000), "Capture shell timed out: " + error);
                return (process.ExitCode, output);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
