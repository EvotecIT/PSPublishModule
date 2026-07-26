using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

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
            var release = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapOperationLockReleaseCommand([lockPath]);
            var script = $"set -Eeuo pipefail\ninstall -o root -g root -m 0644 /dev/null '{lockPath}'\n{acquire}\nif flock -n '{lockPath}' -c true; then exit 71; fi\n{release}\nrm -f -- '{lockPath}'\n";

            var held = RunRootBash(script);

            Assert.Equal(0, held.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BootstrapOperationLocks_CanBeHandedToCommandOwnedDeploymentOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-bootstrap-lock-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var lockPath = Path.Combine(root, "operation.lock").Replace('\\', '/');
            var acquire = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapOperationLockAcquireCommand([lockPath]);
            var release = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapOperationLockReleaseCommand([lockPath]);
            var script = $"set -Eeuo pipefail\ninstall -o root -g root -m 0644 /dev/null '{lockPath}'\n{acquire}\n{release}\nflock -n '{lockPath}' -c true\nrm -f -- '{lockPath}'\n";

            var result = RunRootBash(script);

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BootstrapOperationLocks_CanBeReacquiredAfterCommandOwnedDeploymentOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-bootstrap-lock-reacquire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var lockPath = Path.Combine(root, "operation.lock").Replace('\\', '/');
            var acquire = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapOperationLockAcquireCommand([lockPath]);
            var release = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapOperationLockReleaseCommand([lockPath]);
            var script = $"set -Eeuo pipefail\ninstall -o root -g root -m 0644 /dev/null '{lockPath}'\n{acquire}\n{release}\nflock -n '{lockPath}' -c true\n{acquire}\nif flock -n '{lockPath}' -c true; then exit 71; fi\n{release}\nrm -f -- '{lockPath}'\n";

            var result = RunRootBash(script);

            Assert.Equal(0, result.ExitCode);
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

    [Fact]
    public void DeferredSecretInstall_AllowsAnAbsentOptionalSecretOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-deferred-optional-" + Guid.NewGuid().ToString("N"));
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
            var command = PowerForge.Web.Cli.WebCliCommandHandlers.BuildDeferredSecretInstallCommand(
                new PowerForge.Web.Cli.PowerForgeServerSecret
                {
                    Id = "optional-repository-secret",
                    Path = normalizedTarget,
                    RequiredDuringBootstrap = false,
                    Owner = "root",
                    Group = "root",
                    Mode = "0600"
                },
                staging.Replace('\\', '/'),
                repository.Replace('\\', '/'));

            var result = RunBash("set -Eeuo pipefail\npowerforge_assert_root_controlled_path() { :; }\n" + command);

            Assert.Equal(0, result.ExitCode);
            Assert.False(File.Exists(normalizedTarget));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeferredSecretInstall_CreatesAMissingIgnoredParentOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-deferred-parent-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repo");
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(repository);
        try
        {
            RunProcess("git", repository, "init", "--quiet").EnsureSuccess();
            RunProcess("git", repository, "config", "user.email", "powerforge-tests@example.invalid").EnsureSuccess();
            RunProcess("git", repository, "config", "user.name", "PowerForge Tests").EnsureSuccess();
            File.WriteAllText(Path.Combine(repository, ".gitignore"), "runtime/\n");
            RunProcess("git", repository, "add", ".gitignore").EnsureSuccess();
            RunProcess("git", repository, "commit", "-m", "fixture", "--quiet").EnsureSuccess();

            var normalizedTarget = Path.Combine(repository, "runtime", "secret.env").Replace('\\', '/');
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
                    Id = "nested-repository-secret",
                    Path = normalizedTarget,
                    Owner = owner,
                    Group = group,
                    Mode = "0600"
                },
                normalizedStaging,
                normalizedRepository);

            var result = RunBash("set -Eeuo pipefail\npowerforge_assert_root_controlled_path() { :; }\n" + command);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("restored", File.ReadAllText(normalizedTarget));
            Assert.True(Directory.Exists(Path.GetDirectoryName(normalizedTarget)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OptionalDeferredSecretExtraction_UsesOnlyValidatedPresentMembersOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-optional-extract-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var staging = Path.Combine(root, "staging");
        var present = Path.Combine(source, "srv", "example", "present.env");
        Directory.CreateDirectory(Path.GetDirectoryName(present)!);
        Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllText(present, "present");
            RunProcess("tar", root, "-czf", Path.Combine(root, "secrets.tar.gz"), "-C", source, "srv/example/present.env").EnsureSuccess();
            var generated = PowerForge.Web.Cli.WebCliCommandHandlers.BuildRestoreSecretsScript(
                "archive.age",
                ["/srv/example/present.env", "/srv/example/absent.env"],
                [
                    new PowerForge.Web.Cli.PowerForgeServerRestoreSecretEntry
                    {
                        Id = "present",
                        Path = "/srv/example/present.env",
                        RequiredDuringBootstrap = false,
                        RestoreAfterRepositories = true
                    },
                    new PowerForge.Web.Cli.PowerForgeServerRestoreSecretEntry
                    {
                        Id = "absent",
                        Path = "/srv/example/absent.env",
                        RequiredDuringBootstrap = false,
                        RestoreAfterRepositories = true
                    }
                ],
                staging.Replace('\\', '/'));
            var validation = RunRestoreArchiveValidator(
                root,
                generated,
                Path.Combine(root, "secrets.tar.gz"),
                ["srv/example/present.env", "srv/example/absent.env"],
                ["srv/example/present.env", "srv/example/absent.env"],
                [],
                ["srv/example/present.env", "srv/example/absent.env"]);
            validation.Result.EnsureSuccess();
            Assert.Equal(
                [.. Encoding.UTF8.GetBytes("srv/example/present.env"), 0],
                File.ReadAllBytes(validation.PresentOptionalPaths));
            var extract = Assert.Single(generated.Split('\n'), static line =>
                line.StartsWith("if [ -s \"$tmp_dir/present-optional-deferred-paths\" ]", StringComparison.Ordinal));
            var normalizedRoot = root.Replace('\\', '/');
            var normalizedStaging = staging.Replace('\\', '/');
            var result = RunBash(
                $"set -Eeuo pipefail\ntmp_dir={BashQuote(normalizedRoot)}\nstaging_root={BashQuote(normalizedStaging)}\n{extract}\n" +
                $"test \"$(cat -- {BashQuote(normalizedStaging + "/srv/example/present.env")})\" = present\n" +
                $"test ! -e {BashQuote(normalizedStaging + "/srv/example/absent.env")}\n");

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreArchiveValidation_RejectsMissingRequiredDeferredSecretOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-required-deferred-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var direct = Path.Combine(source, "etc", "example", "direct.env");
        Directory.CreateDirectory(Path.GetDirectoryName(direct)!);
        try
        {
            File.WriteAllText(direct, "direct");
            var archive = Path.Combine(root, "secrets.tar.gz");
            RunProcess("tar", root, "-czf", archive, "-C", source, "etc/example/direct.env").EnsureSuccess();
            var generated = PowerForge.Web.Cli.WebCliCommandHandlers.BuildRestoreSecretsScript(
                "archive.age",
                ["/etc/example/direct.env", "/srv/example/required.env"],
                [
                    new PowerForge.Web.Cli.PowerForgeServerRestoreSecretEntry
                    {
                        Id = "required",
                        Path = "/srv/example/required.env",
                        RestoreAfterRepositories = true
                    }
                ],
                "/var/lib/powerforge/restore-secrets/fixture");

            var validation = RunRestoreArchiveValidator(
                root,
                generated,
                archive,
                ["etc/example/direct.env", "srv/example/required.env"],
                ["srv/example/required.env"],
                ["srv/example/required.env"],
                []);

            Assert.NotEqual(0, validation.Result.ExitCode);
            Assert.Contains("Required deferred secret is missing from the archive", validation.Result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreArchiveValidation_RejectsDescendantsOfAnExactDeferredFileOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-deferred-descendant-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var child = Path.Combine(source, "srv", "example", "secret.env", "child");
        Directory.CreateDirectory(Path.GetDirectoryName(child)!);
        try
        {
            File.WriteAllText(child, "child");
            var archive = Path.Combine(root, "secrets.tar.gz");
            RunProcess("tar", root, "-czf", archive, "-C", source, "srv/example/secret.env/child").EnsureSuccess();
            var generated = PowerForge.Web.Cli.WebCliCommandHandlers.BuildRestoreSecretsScript(
                "archive.age",
                ["/srv/example/secret.env"],
                [
                    new PowerForge.Web.Cli.PowerForgeServerRestoreSecretEntry
                    {
                        Id = "optional",
                        Path = "/srv/example/secret.env",
                        RequiredDuringBootstrap = false,
                        RestoreAfterRepositories = true
                    }
                ],
                "/var/lib/powerforge/restore-secrets/fixture");

            var validation = RunRestoreArchiveValidator(
                root,
                generated,
                archive,
                ["srv/example/secret.env"],
                ["srv/example/secret.env"],
                [],
                ["srv/example/secret.env"]);

            Assert.NotEqual(0, validation.Result.ExitCode);
            Assert.Contains("Deferred secret exact file path must not contain descendants", validation.Result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreScript_WithOptionalDeferredSecrets_PassesBashAndShellCheckOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-restore-shellcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scriptPath = Path.Combine(root, "restore-secrets.sh");
            var script = PowerForge.Web.Cli.WebCliCommandHandlers.BuildRestoreSecretsScript(
                "archive.age",
                ["/srv/example/required.env", "/srv/example/optional.env"],
                [
                    new PowerForge.Web.Cli.PowerForgeServerRestoreSecretEntry
                    {
                        Id = "required",
                        Path = "/srv/example/required.env",
                        RestoreAfterRepositories = true
                    },
                    new PowerForge.Web.Cli.PowerForgeServerRestoreSecretEntry
                    {
                        Id = "optional",
                        Path = "/srv/example/optional.env",
                        RequiredDuringBootstrap = false,
                        RestoreAfterRepositories = true
                    }
                ],
                "/var/lib/powerforge/restore-secrets/fixture");
            File.WriteAllText(scriptPath, script);

            RunProcess("bash", root, "-n", scriptPath).EnsureSuccess();
            RunProcess("shellcheck", root, "-S", "warning", "--", scriptPath).EnsureSuccess();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunBash(
        string script,
        IReadOnlyDictionary<string, string>? environment = null)
        => RunBashProcess("bash", [], script, environment);

    private static (int ExitCode, string Stdout, string Stderr) RunRootBash(string script)
    {
        var userId = RunProcess("id", Path.GetTempPath(), "-u").Stdout.Trim();
        return userId == "0"
            ? RunBash(script)
            : RunBashProcess("sudo", ["-n", "bash"], script, environment: null);
    }

    private static (int ExitCode, string Stdout, string Stderr) RunBashProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string script,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
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

    private static (ProcessResult Result, string PresentOptionalPaths) RunRestoreArchiveValidator(
        string root,
        string generatedScript,
        string archive,
        IReadOnlyCollection<string> allowed,
        IReadOnlyCollection<string> deferred,
        IReadOnlyCollection<string> requiredDeferred,
        IReadOnlyCollection<string> optionalDeferred)
    {
        var lines = generatedScript.Split('\n');
        var start = Array.FindIndex(lines, static line => line.EndsWith("<<'POWERFORGE_VALIDATE_ARCHIVE'", StringComparison.Ordinal));
        var end = start < 0 ? -1 : Array.IndexOf(lines, "POWERFORGE_VALIDATE_ARCHIVE", start + 1);
        Assert.True(start >= 0 && end > start, "Generated archive validator here-document was not found.");

        var validator = Path.Combine(root, "validate-archive.py");
        var allowedPath = Path.Combine(root, "allowed-paths");
        var deferredPath = Path.Combine(root, "deferred-paths");
        var requiredPath = Path.Combine(root, "required-deferred-paths");
        var optionalPath = Path.Combine(root, "optional-deferred-paths");
        var presentPath = Path.Combine(root, "present-optional-deferred-paths");
        File.WriteAllLines(validator, lines[(start + 1)..end]);
        File.WriteAllLines(allowedPath, allowed);
        File.WriteAllLines(deferredPath, deferred);
        File.WriteAllLines(requiredPath, requiredDeferred);
        File.WriteAllLines(optionalPath, optionalDeferred);

        var result = RunProcess(
            "python3",
            root,
            validator,
            archive,
            allowedPath,
            deferredPath,
            requiredPath,
            optionalPath,
            presentPath);
        return (result, presentPath);
    }

    private static string BashQuote(string value)
        => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
    {
        internal void EnsureSuccess()
        {
            Assert.True(ExitCode == 0, $"Process failed with exit code {ExitCode}: {Stderr}");
        }
    }
}
