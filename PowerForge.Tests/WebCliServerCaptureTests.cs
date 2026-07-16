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
    public void BuildRemoteTarScript_EnforcesRequiredExactAndGlobPaths()
    {
        var script = WebCliCommandHandlers.BuildRemoteTarScript(
        [
            new PowerForgeServerManagedFile { Target = "/etc/example.conf", Required = true },
            new PowerForgeServerManagedFile { Target = "/etc/example/*.json", Required = true },
            new PowerForgeServerManagedFile { Target = "/var/lib/example/optional.json" }
        ]);

        Assert.Equal("set -e; sudo -n tar -czf - /etc/example.conf /etc/example/*.json /var/lib/example/optional.json", script);
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
    public void BuildRemoteEncryptedCaptureSudoersCommand_FixesRecipientAndRejectsWildcards()
    {
        var command = WebCliCommandHandlers.BuildRemoteEncryptedCaptureSudoersCommand(
        [
            new PowerForgeServerManagedFile { Target = "/etc/example/required.env", Required = true }
        ], "age1example");

        Assert.Equal("/usr/local/sbin/powerforge-server-encrypted-capture --recipient age1example -- /etc/example/required.env", command);
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
}
