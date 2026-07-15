using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebCliServerCaptureTests
{
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
    public void BuildRemoteEncryptedTarScript_PropagatesTarFailuresThroughEncryptionPipeline()
    {
        var script = WebCliCommandHandlers.BuildRemoteEncryptedTarScript(
        [
            new PowerForgeServerManagedFile { Target = "/etc/example/required.env", Required = true }
        ], "age1example");

        Assert.StartsWith("bash -o pipefail -c ", script, StringComparison.Ordinal);
        Assert.Contains("sudo -n tar -czf - /etc/example/required.env", script, StringComparison.Ordinal);
        Assert.Contains("| age -r", script, StringComparison.Ordinal);
    }
}
