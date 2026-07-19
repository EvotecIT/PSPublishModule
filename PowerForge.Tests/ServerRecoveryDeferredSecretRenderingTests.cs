namespace PowerForge.Tests;

public sealed class ServerRecoveryDeferredSecretRenderingTests
{
    [Fact]
    public void DeferredSecretInstall_RendersNestedFallbackOnANewLine()
    {
        var command = PowerForge.Web.Cli.WebCliCommandHandlers.BuildDeferredSecretInstallCommand(
            new PowerForge.Web.Cli.PowerForgeServerSecret
            {
                Id = "repository-secret",
                Path = "/srv/example/.secret",
                Owner = "root",
                Group = "root",
                Mode = "0600"
            },
            "/var/lib/powerforge/restore-secrets/example",
            "/srv/example");

        Assert.Contains("; else\nif [ -e '/srv/example/.secret' ]", command, StringComparison.Ordinal);
        Assert.DoesNotContain("; else if [", command, StringComparison.Ordinal);
    }
}
