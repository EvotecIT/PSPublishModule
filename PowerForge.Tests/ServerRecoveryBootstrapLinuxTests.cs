using System.Diagnostics;
using System.Runtime.Versioning;

namespace PowerForge.Tests;

public sealed class ServerRecoveryBootstrapLinuxTests
{
    [Fact]
    public void BootstrapOperationLocks_RemainHeldForTheGeneratedShellLifetimeOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-bootstrap-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var lockPath = Path.Combine(root, "operation.lock").Replace('\\', '/');
            var acquire = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapOperationLockAcquireCommand([lockPath]);
            var script = $"set -Eeuo pipefail\n: >'{lockPath}'\n{acquire}\nif flock -n '{lockPath}' -c true; then exit 71; fi\n";

            var held = RunBash(script);
            var released = RunBash($"flock -n '{lockPath}' -c true");

            Assert.Equal(0, held.ExitCode);
            Assert.Equal(0, released.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApacheActivation_RollsBackBeforeReturningValidationFailureOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-apache-rollback-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "bin");
        var log = Path.Combine(root, "commands.log");
        Directory.CreateDirectory(bin);
        try
        {
            foreach (var command in new[] { "a2ensite", "a2dissite", "a2enconf", "a2disconf", "systemctl" })
            {
                WriteExecutable(
                    Path.Combine(bin, command),
                    "#!/usr/bin/env bash\nprintf '%s %s\\n' \"$(basename -- \"$0\")\" \"$*\" >>\"$POWERFORGE_TEST_LOG\"\n");
            }
            WriteExecutable(
                Path.Combine(bin, "apachectl"),
                "#!/usr/bin/env bash\nprintf 'apachectl %s\\n' \"$*\" >>\"$POWERFORGE_TEST_LOG\"\nexit 1\n");

            var suffix = Guid.NewGuid().ToString("N");
            var commandText = PowerForge.Web.Cli.WebCliCommandHandlers.BuildApacheActivationCommand(
                new PowerForge.Web.Cli.PowerForgeServerApache
                {
                    Service = "apache2",
                    ValidateCommand = "apachectl configtest",
                    Sites =
                    [
                        new PowerForge.Web.Cli.PowerForgeServerApacheFile
                        {
                            Target = $"/etc/apache2/sites-available/powerforge-{suffix}.conf",
                            Enabled = true
                        }
                    ]
                });

            var result = RunBash(
                "set -Eeuo pipefail\n" + commandText,
                new Dictionary<string, string>
                {
                    ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                    ["POWERFORGE_TEST_LOG"] = log
                });
            var commands = File.ReadAllLines(log);

            Assert.NotEqual(0, result.ExitCode);
            Assert.StartsWith("a2ensite powerforge-", commands[0], StringComparison.Ordinal);
            Assert.Equal("apachectl configtest", commands[1]);
            Assert.StartsWith("a2dissite powerforge-", commands[2], StringComparison.Ordinal);
            Assert.Equal("apachectl configtest", commands[3]);
            Assert.DoesNotContain(commands, line => line.StartsWith("systemctl ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeferredSecretInstall_RejectsTrackedAndSymlinkTargetsOnLinux(bool trackedTarget)
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-deferred-target-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repo");
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(repository);
        try
        {
            RunProcess("git", repository, "init", "--quiet").EnsureSuccess();
            RunProcess("git", repository, "config", "user.email", "powerforge-tests@example.invalid").EnsureSuccess();
            RunProcess("git", repository, "config", "user.name", "PowerForge Tests").EnsureSuccess();
            var target = Path.Combine(repository, ".secret");
            if (trackedTarget)
            {
                File.WriteAllText(target, "tracked");
                RunProcess("git", repository, "add", ".secret").EnsureSuccess();
            }
            else
            {
                File.WriteAllText(Path.Combine(repository, ".gitignore"), ".secret\n");
                RunProcess("git", repository, "add", ".gitignore").EnsureSuccess();
                File.CreateSymbolicLink(target, Path.Combine(root, "outside-secret"));
            }
            RunProcess("git", repository, "commit", "-m", "fixture", "--quiet").EnsureSuccess();

            var normalizedTarget = target.Replace('\\', '/');
            var normalizedRepository = repository.Replace('\\', '/');
            var normalizedStaging = staging.Replace('\\', '/');
            var staged = normalizedStaging.TrimEnd('/') + normalizedTarget;
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllText(staged, "restored");
            var command = PowerForge.Web.Cli.WebCliCommandHandlers.BuildDeferredSecretInstallCommand(
                new PowerForge.Web.Cli.PowerForgeServerSecret
                {
                    Id = "repository-secret",
                    Path = normalizedTarget,
                    Owner = "root",
                    Group = "root",
                    Mode = "0600"
                },
                normalizedStaging,
                normalizedRepository);

            var result = RunBash("set -Eeuo pipefail\npowerforge_assert_root_controlled_path() { :; }\n" + command);

            Assert.Equal(3, result.ExitCode);
            Assert.Contains(
                trackedTarget ? "must not be tracked" : "regular non-symlink file",
                result.Stderr,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeferredSecretInstall_IsRerunnableAfterStagingIsRemovedOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-deferred-rerun-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repo");
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(repository);
        try
        {
            RunProcess("git", repository, "init", "--quiet").EnsureSuccess();
            RunProcess("git", repository, "config", "user.email", "powerforge-tests@example.invalid").EnsureSuccess();
            RunProcess("git", repository, "config", "user.name", "PowerForge Tests").EnsureSuccess();
            File.WriteAllText(Path.Combine(repository, ".gitignore"), ".secret\n");
            RunProcess("git", repository, "add", ".gitignore").EnsureSuccess();
            RunProcess("git", repository, "commit", "-m", "fixture", "--quiet").EnsureSuccess();

            var normalizedTarget = Path.Combine(repository, ".secret").Replace('\\', '/');
            var normalizedRepository = repository.Replace('\\', '/');
            var normalizedStaging = staging.Replace('\\', '/');
            var staged = normalizedStaging.TrimEnd('/') + normalizedTarget;
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllText(staged, "restored");
            var owner = RunProcess("id", root, "-u").Stdout.Trim();
            var group = RunProcess("id", root, "-g").Stdout.Trim();
            var command = PowerForge.Web.Cli.WebCliCommandHandlers.BuildDeferredSecretInstallCommand(
                new PowerForge.Web.Cli.PowerForgeServerSecret
                {
                    Id = "repository-secret",
                    Path = normalizedTarget,
                    Owner = owner,
                    Group = group,
                    Mode = "0600"
                },
                normalizedStaging,
                normalizedRepository);
            var script = "set -Eeuo pipefail\npowerforge_assert_root_controlled_path() { :; }\n" + command;

            var first = RunBash(script);
            var second = RunBash(script);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal("restored", File.ReadAllText(normalizedTarget));
            Assert.False(File.Exists(staged));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunBash(
        string script,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (environment is not null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }
        using var process = Process.Start(startInfo)!;
        process.StandardInput.Write(script);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    [SupportedOSPlatform("linux")]
    private static void WriteExecutable(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static ProcessResult RunProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
    {
        internal void EnsureSuccess()
        {
            Assert.True(ExitCode == 0, $"Process failed with exit code {ExitCode}: {Stderr}");
        }
    }
}
