using System.Diagnostics;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoveryAccountIdentityTests
{
    [Theory]
    [InlineData("/var/lib/example", "/bin/bash", true)]
    [InlineData("/home/other", "/bin/bash", false)]
    [InlineData("/var/lib/example", "/bin/zsh", false)]
    public void Bootstrap_StopsBeforeFilesystemMutationWhenExistingIdentityDrifts(string home, string shell, bool accepted)
    {
        var account = new PowerForgeServerAccount { Name = "example", Home = "/var/lib/example", Shell = "/bin/bash" };
        var manifest = new PowerForgeServerRecoveryManifest { Accounts = [account] };
        var step = Assert.Single(WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []), item => item.Category == "accounts");
        Assert.Contains(WebCliCommandHandlers.BuildAccountIdentityCheckCommand(account), step.Command);
        Assert.Contains("exit 3", step.Command);
        if (!OperatingSystem.IsLinux()) return;

        var root = Directory.CreateTempSubdirectory("powerforge-account-identity-").FullName;
        try
        {
            var bin = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
            WriteExecutable("getent", $"#!/bin/sh\nprintf '%s\\n' 'example:x:1000:1000::{home}:{shell}'\n");
            WriteExecutable("id", "#!/bin/sh\nexit 0\n");
            WriteExecutable("useradd", "#!/bin/sh\ntouch useradd-called\nexit 99\n");
            var start = new ProcessStartInfo("/bin/sh")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Environment["PATH"] = bin + ":/usr/bin:/bin";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(step.Command + "\ntouch filesystem-mutation\n");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(10_000), output + error);
            Assert.Equal(accepted ? 0 : 3, process.ExitCode);
            Assert.Equal(accepted, File.Exists(Path.Combine(root, "filesystem-mutation")));
            Assert.False(File.Exists(Path.Combine(root, "useradd-called")));

            void WriteExecutable(string name, string script)
            {
                var path = Path.Combine(bin, name);
                File.WriteAllText(path, script);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CaptureTransport_IsNonLoginWhileInspectionKeepsLoginEnvironment()
    {
        Assert.StartsWith("/bin/sh -c ", WebCliCommandHandlers.BuildCaptureSshArguments("host", "printf ok")[^1]);
        Assert.StartsWith("sh -lc ", WebCliCommandHandlers.BuildSshArguments("host", "printf ok")[^1]);
    }
}
