using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoveryInspectCommandTests
{
    [Theory]
    [InlineData("directory", "/etc/powerforge/repository-ssh", "sudo -n test -d '/etc/powerforge/repository-ssh'")]
    [InlineData("file", "/etc/powerforge/sites/example.env", "sudo -n test -e '/etc/powerforge/sites/example.env'")]
    [InlineData("symlink", "/var/www/example/current", "sudo -n test -e '/var/www/example/current'")]
    public void ManagedPathChecksUseNonInteractiveSudo(string kind, string path, string expected)
    {
        var managedPath = new PowerForgeServerPath
        {
            Kind = kind,
            Path = path
        };

        Assert.Equal(expected, WebCliCommandHandlers.BuildManagedPathCheckCommand(managedPath));
    }

    [Fact]
    public void ManagedSymlinkResolutionUsesNonInteractiveSudo()
    {
        var command = WebCliCommandHandlers.BuildManagedSymlinkTargetCommand("/var/www/example/current");

        Assert.Equal("sudo -n readlink -f '/var/www/example/current'", command);
    }
}
