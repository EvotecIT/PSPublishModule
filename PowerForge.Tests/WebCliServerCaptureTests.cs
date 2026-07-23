using System.Diagnostics;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebCliServerCaptureTests
{
    [Fact]
    public void HydrateCapturedRepositoryRefs_UsesSuccessfulExactCommandOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-ref-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var outputPath = Path.Combine(root, "static-source-ref.out.txt");
            File.WriteAllText(outputPath, "ABCDEF0123456789ABCDEF0123456789ABCDEF01\n");
            var manifest = new PowerForgeServerRecoveryManifest
            {
                Repositories =
                [
                    new PowerForgeServerRepository
                    {
                        Role = "application",
                        Ref = "1111111111111111111111111111111111111111",
                        RefCaptureCommandId = "static-source-ref"
                    }
                ]
            };
            var warnings = new List<string>();

            WebCliCommandHandlers.HydrateCapturedRepositoryRefs(
                manifest,
                [new PowerForgeServerCaptureCommandResult { Id = "static-source-ref", Success = true, StdoutPath = outputPath }],
                warnings);

            Assert.Equal("abcdef0123456789abcdef0123456789abcdef01", manifest.Repositories[0].Ref);
            Assert.Empty(warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HydrateCapturedRepositoryRefs_DropsStaleRefAndWarnsOnMalformedOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-ref-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var outputPath = Path.Combine(root, "static-source-ref.out.txt");
            var manifest = new PowerForgeServerRecoveryManifest
            {
                Repositories =
                [
                    new PowerForgeServerRepository
                    {
                        Role = "application",
                        Ref = "1111111111111111111111111111111111111111",
                        RefCaptureCommandId = "static-source-ref"
                    }
                ]
            };
            var warnings = new List<string>();

            foreach (var invalidRevision in new[] { "main", new string('a', 41), new string('b', 63) })
            {
                File.WriteAllText(outputPath, invalidRevision + "\n");
                manifest.Repositories[0].Ref = "1111111111111111111111111111111111111111";
                warnings.Clear();

                WebCliCommandHandlers.HydrateCapturedRepositoryRefs(
                    manifest,
                    [new PowerForgeServerCaptureCommandResult { Id = "static-source-ref", Success = true, StdoutPath = outputPath }],
                    warnings);

                Assert.Null(manifest.Repositories[0].Ref);
                Assert.Contains(warnings, warning => warning.Contains("not an exact commit", StringComparison.Ordinal));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HydrateCapturedRepositoryRefs_RequiresAllConsensusCommandsToAgree()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-ref-consensus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xyzOutputPath = Path.Combine(root, "xyz-source-ref.out.txt");
            var plOutputPath = Path.Combine(root, "pl-source-ref.out.txt");
            const string revision = "abcdef0123456789abcdef0123456789abcdef01";
            File.WriteAllText(xyzOutputPath, revision + "\n");
            File.WriteAllText(plOutputPath, revision.ToUpperInvariant() + "\n");
            var manifest = new PowerForgeServerRecoveryManifest
            {
                Repositories =
                [
                    new PowerForgeServerRepository
                    {
                        Role = "application",
                        Ref = "1111111111111111111111111111111111111111",
                        RefCaptureCommandIds = ["xyz-source-ref", "pl-source-ref"]
                    }
                ]
            };
            var commandResults = new[]
            {
                new PowerForgeServerCaptureCommandResult { Id = "xyz-source-ref", Success = true, StdoutPath = xyzOutputPath },
                new PowerForgeServerCaptureCommandResult { Id = "pl-source-ref", Success = true, StdoutPath = plOutputPath }
            };
            var warnings = new List<string>();

            WebCliCommandHandlers.HydrateCapturedRepositoryRefs(manifest, commandResults, warnings);

            Assert.Equal(revision, manifest.Repositories[0].Ref);
            Assert.Empty(warnings);

            File.WriteAllText(plOutputPath, "1234567890abcdef1234567890abcdef12345678\n");
            manifest.Repositories[0].Ref = "1111111111111111111111111111111111111111";

            WebCliCommandHandlers.HydrateCapturedRepositoryRefs(manifest, commandResults, warnings);

            Assert.Null(manifest.Repositories[0].Ref);
            Assert.Contains(warnings, warning => warning.Contains("revision captures disagree", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCaptureCommandOutputStem_SeparatesFilenameAliases()
    {
        var stems = new[]
        {
            WebCliCommandHandlers.BuildCaptureCommandOutputStem(0, "xyz-source-ref"),
            WebCliCommandHandlers.BuildCaptureCommandOutputStem(1, "xyz/source/ref"),
            WebCliCommandHandlers.BuildCaptureCommandOutputStem(2, "XYZ-SOURCE-REF"),
            WebCliCommandHandlers.BuildCaptureCommandOutputStem(3, new string('x', 300))
        };

        Assert.Equal(stems.Length, stems.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(stems, stem => Assert.True(stem.Length <= 85));
    }

    [Fact]
    public void BuildRemoteTarScript_EnforcesRequiredExactPaths()
    {
        var script = WebCliCommandHandlers.BuildRemoteTarScript(
        [
            new PowerForgeServerManagedFile { Target = "/etc/example.conf", Required = true },
            new PowerForgeServerManagedFile { Target = "/var/lib/example/optional.json" }
        ]);

        Assert.Equal("set -e; sudo -n tar -czf - /etc/example.conf /var/lib/example/optional.json", script);
    }

    [Fact]
    public void BuildRemoteTarScript_RejectsWildcardPaths()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteTarScript(
            [
                new PowerForgeServerManagedFile { Target = "/etc/letsencrypt/*", Required = true }
            ]));

        Assert.Contains("unsupported characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRemoteTarScript_AllowsMissingPathsOnlyForOptionalArchive()
    {
        var script = WebCliCommandHandlers.BuildRemoteTarScript(
        [
            new PowerForgeServerManagedFile { Target = "/var/lib/example/optional.json" }
        ]);

        Assert.Equal("set -e; sudo -n tar -czf - --ignore-failed-read /var/lib/example/optional.json", script);
    }

    [Fact]
    public void BuildRemoteTarScript_RejectsUnsafePathCharacters()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteTarScript(
            [
                new PowerForgeServerManagedFile { Target = "/etc/example;id", Required = true }
            ]));

        Assert.Contains("unsupported characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRemoteEncryptedTarScript_DelegatesOnlyToRootOwnedCiphertextWrapper()
    {
        var script = WebCliCommandHandlers.BuildRemoteEncryptedTarScript(
        [
            new PowerForgeServerManagedFile { Target = "/etc/example/required.env", Required = true }
        ], "age1example");

        Assert.Equal("sudo -n /usr/local/sbin/powerforge-server-encrypted-capture --recipient 'age1example' -- '/etc/example/required.env'", script);
        Assert.DoesNotContain(" tar ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("| age", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRemoteEncryptedTarScript_DistinguishesMixedOptionalPaths()
    {
        var script = WebCliCommandHandlers.BuildRemoteEncryptedTarScript(
        [
            new PowerForgeServerManagedFile { Target = "/var/lib/example/optional.env" },
            new PowerForgeServerManagedFile { Target = "/etc/example/required.env", Required = true }
        ], "age1example");

        Assert.Equal(
            "sudo -n /usr/local/sbin/powerforge-server-encrypted-capture --recipient 'age1example' -- '/etc/example/required.env' --optional '/var/lib/example/optional.env'",
            script);
    }

    [Fact]
    public void BuildRemoteEncryptedCaptureSudoersCommand_FixesRecipientAndRejectsWildcards()
    {
        var command = WebCliCommandHandlers.BuildRemoteEncryptedCaptureSudoersCommand(
        [
            new PowerForgeServerManagedFile { Target = "/etc/example/required.env", Required = true }
        ], "age1example");

        Assert.Equal("/usr/local/sbin/powerforge-server-encrypted-capture --recipient age1example -- /etc/example/required.env", command);
        Assert.Equal(
            "/usr/local/sbin/powerforge-server-encrypted-capture --recipient age1example -- /etc/example/required.env --optional /var/lib/example/optional.env",
            WebCliCommandHandlers.BuildRemoteEncryptedCaptureSudoersCommand(
            [
                new PowerForgeServerManagedFile { Target = "/var/lib/example/optional.env" },
                new PowerForgeServerManagedFile { Target = "/etc/example/required.env", Required = true }
            ], "age1example"));
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteEncryptedCaptureSudoersCommand(
            [
                new PowerForgeServerManagedFile { Target = "/etc/example/*", Required = true }
            ], "age1example"));
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteEncryptedCaptureSudoersCommand(
            [
                new PowerForgeServerManagedFile { Target = "etc/example/secret.env", Required = true }
            ], "age1example"));
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteEncryptedCaptureSudoersCommand(
            [
                new PowerForgeServerManagedFile { Target = "/etc/example/../secret.env", Required = true }
            ], "age1example"));
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteEncryptedCaptureSudoersCommand(
            [
                new PowerForgeServerManagedFile { Target = "/etc/example/secr\u00E9t.env", Required = true }
            ], "age1example"));
    }

    [Fact]
    public void BuildRemoteOperationLockCommand_HoldsEveryDeclaredLockForSessionLifetime()
    {
        var command = WebCliCommandHandlers.BuildRemoteOperationLockCommand(
        [
            "/var/lock/powerforge-site-example.lock",
            "/var/lock/powerforge-contact-example.lock",
            "/var/lock/powerforge-site-example.lock"
        ]);

        Assert.Equal(1, command.Split("flock -n '/var/lock/powerforge-site-example.lock'", StringSplitOptions.None).Length - 1);
        Assert.Contains("flock -n '/var/lock/powerforge-site-example.lock'", command, StringComparison.Ordinal);
        Assert.Contains("flock -n '/var/lock/powerforge-contact-example.lock'", command, StringComparison.Ordinal);
        Assert.Contains("test -f '/var/lock/powerforge-site-example.lock'", command, StringComparison.Ordinal);
        Assert.Contains("test -f '/var/lock/powerforge-contact-example.lock'", command, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_OPERATION_LOCKED", command, StringComparison.Ordinal);
        Assert.Contains("cat >/dev/null", command, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteOperationLockCommand(["/tmp/example.lock"]));
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteOperationLockCommand(["/var/lock/.lock"]));
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteOperationLockCommand(["/var/lock/_site.lock"]));
    }

    [Fact]
    public void BuildRemoteOperationLockCommand_CanWaitForAConcurrentHostOperation()
    {
        var command = WebCliCommandHandlers.BuildRemoteOperationLockCommand(
            ["/var/lock/powerforge-site-example.lock"],
            waitSeconds: 900);

        Assert.Contains("flock -w 900 '/var/lock/powerforge-site-example.lock'", command, StringComparison.Ordinal);
        Assert.DoesNotContain("flock -n", command, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildRemoteOperationLockCommand(
                ["/var/lock/powerforge-site-example.lock"],
                waitSeconds: 3601));
    }

    [Fact]
    public void RemoteOperationLock_ReportsADeadSessionBeforeWorkCanContinue()
    {
        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("exit 17");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("exit 17");
        }
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        using var operationLock = new WebCliCommandHandlers.RemoteOperationLock(process);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            operationLock.EnsureHeld("before archive capture"));

        Assert.Contains("ended before archive capture", exception.Message, StringComparison.Ordinal);
    }
}
