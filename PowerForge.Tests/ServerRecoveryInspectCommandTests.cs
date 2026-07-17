using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoveryInspectCommandTests
{
    [Theory]
    [InlineData("directory", "/etc/powerforge/repository-ssh", "sudo -n test -d '/etc/powerforge/repository-ssh' && sudo -n test ! -L '/etc/powerforge/repository-ssh'")]
    [InlineData("file", "/etc/powerforge/sites/example.env", "sudo -n test -f '/etc/powerforge/sites/example.env' && sudo -n test ! -L '/etc/powerforge/sites/example.env'")]
    [InlineData("symlink", "/var/www/example/current", "sudo -n test -L '/var/www/example/current'")]
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
    public void ManagedPathChecksVerifyDeclaredOwnershipAndMode()
    {
        var managedPath = new PowerForgeServerPath
        {
            Kind = "file",
            Path = "/etc/powerforge/sites/example.env",
            Owner = "root",
            Group = "powerforge",
            Mode = "0640"
        };

        var command = WebCliCommandHandlers.BuildManagedPathCheckCommand(managedPath);

        Assert.Equal(
            "sudo -n test -f '/etc/powerforge/sites/example.env' && sudo -n test ! -L '/etc/powerforge/sites/example.env' && " +
            "test \"$(sudo -n stat -c '%U' -- '/etc/powerforge/sites/example.env')\" = 'root' && " +
            "test \"$(sudo -n stat -c '%G' -- '/etc/powerforge/sites/example.env')\" = 'powerforge' && " +
            "test \"$(sudo -n stat -c '%a' -- '/etc/powerforge/sites/example.env')\" = '640'",
            command);
    }

    [Fact]
    public void ManagedSymlinkResolutionUsesNonInteractiveSudo()
    {
        var command = WebCliCommandHandlers.BuildManagedSymlinkTargetCommand("/var/www/example/current");

        Assert.Equal("sudo -n readlink -f '/var/www/example/current'", command);
    }

    [Theory]
    [InlineData("x64", "x86_64")]
    [InlineData("arm64", "aarch64")]
    public void ManifestArchitecturesNormalizeToLinuxMachineNames(string value, string expected)
    {
        Assert.Equal(expected, WebCliCommandHandlers.NormalizeLinuxArchitecture(value));
    }

    [Fact]
    public void ManagedContentAndSudoersChecksAreNonInteractiveAndValueFree()
    {
        Assert.Equal(
            "sudo -n cmp -s -- '/srv/example/deploy/example.env' '/etc/example.env'",
            WebCliCommandHandlers.BuildManagedFileContentCheckCommand("/srv/example/deploy/example.env", "/etc/example.env"));
        Assert.Equal(
            "sudo -n visudo -cf '/etc/sudoers.d/powerforge-example'",
            WebCliCommandHandlers.BuildSudoersValidationCommand("/etc/sudoers.d/powerforge-example"));
    }

    [Fact]
    public void RepositoryChecksRequireAWorkTreePinnedRevisionAndNoLocalChanges()
    {
        const string path = "/srv/example";
        const string reference = "0123456789abcdef0123456789abcdef01234567";

        Assert.Equal(
            "test \"$(sudo -n git -C '/srv/example' rev-parse --is-inside-work-tree)\" = 'true'",
            WebCliCommandHandlers.BuildRepositoryExistsCheckCommand(path));
        Assert.Equal(
            "test \"$(sudo -n git -C '/srv/example' rev-parse HEAD)\" = \"$(sudo -n git -C '/srv/example' rev-parse '0123456789abcdef0123456789abcdef01234567^{commit}')\"",
            WebCliCommandHandlers.BuildRepositoryRefCheckCommand(path, reference));
        Assert.Equal(
            "test -z \"$(sudo -n git -C '/srv/example' status --porcelain --untracked-files=normal)\"",
            WebCliCommandHandlers.BuildRepositoryCleanCheckCommand(path));
    }
}
