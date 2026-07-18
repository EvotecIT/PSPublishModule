using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoveryInspectCommandTests
{
    [Fact]
    public void OptionalSubsystemChecksRunOnlyWhenDeclared()
    {
        var manifest = new PowerForgeServerRecoveryManifest();

        Assert.False(WebCliCommandHandlers.HasDeclaredApacheState(manifest));
        Assert.False(WebCliCommandHandlers.HasDeclaredFirewallState(manifest));

        manifest.Packages = new PowerForgeServerPackages { ApacheModules = ["headers"] };
        manifest.Firewall = new PowerForgeServerFirewall();

        Assert.True(WebCliCommandHandlers.HasDeclaredApacheState(manifest));
        Assert.True(WebCliCommandHandlers.HasDeclaredFirewallState(manifest));
    }

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

        Assert.Contains("sudo -n test -e \"$powerforge_path\"", command, StringComparison.Ordinal);
        Assert.Contains("sudo -n test ! -L \"$powerforge_path\"", command, StringComparison.Ordinal);
        Assert.Contains("powerforge_assert_root_controlled_path \"$(dirname -- '/etc/powerforge/sites/example.env')\"", command, StringComparison.Ordinal);
        Assert.Contains("sudo -n test -f '/etc/powerforge/sites/example.env' && sudo -n test ! -L '/etc/powerforge/sites/example.env'", command, StringComparison.Ordinal);
        Assert.Contains("test \"$(sudo -n stat -c '%U' -- '/etc/powerforge/sites/example.env')\" = 'root'", command, StringComparison.Ordinal);
        Assert.Contains("test \"$(sudo -n stat -c '%G' -- '/etc/powerforge/sites/example.env')\" = 'powerforge'", command, StringComparison.Ordinal);
        Assert.EndsWith("test \"$(sudo -n stat -c '%a' -- '/etc/powerforge/sites/example.env')\" = '640'", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedPathChecksUseNumericUidAndGidFormats()
    {
        var command = WebCliCommandHandlers.BuildManagedPathCheckCommand(new PowerForgeServerPath
        {
            Kind = "directory",
            Path = "/var/lib/example",
            Owner = "0",
            Group = "65534",
            Mode = "0750"
        });

        Assert.Contains("stat -c '%u' -- '/var/lib/example')\" = '0'", command, StringComparison.Ordinal);
        Assert.Contains("stat -c '%g' -- '/var/lib/example')\" = '65534'", command, StringComparison.Ordinal);
        Assert.Contains("powerforge_assert_root_controlled_path \"$(dirname -- '/var/lib/example')\"", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedSymlinkResolutionUsesNonInteractiveSudo()
    {
        var command = WebCliCommandHandlers.BuildManagedSymlinkTargetCommand("/var/www/example/current");

        Assert.Equal("sudo -n readlink -f '/var/www/example/current'", command);
    }

    [Fact]
    public void OperationLockChecksRequireExactRootOwnedRegularFile()
    {
        Assert.Equal(
            "sudo -n test -f '/var/lock/powerforge-site-example.lock' && sudo -n test ! -L '/var/lock/powerforge-site-example.lock' && " +
            "test \"$(sudo -n stat -c '%U:%G %a' -- '/var/lock/powerforge-site-example.lock')\" = 'root:root 644'",
            WebCliCommandHandlers.BuildOperationLockCheckCommand("/var/lock/powerforge-site-example.lock"));
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
            "sudo -n test -f '/etc/apache2/conf-available/platform-managed.conf' && sudo -n test ! -L '/etc/apache2/conf-available/platform-managed.conf'",
            WebCliCommandHandlers.BuildManagedFileExistsCheckCommand("/etc/apache2/conf-available/platform-managed.conf"));
        var securedContentCheck = WebCliCommandHandlers.BuildManagedFileContentCheckCommand(
            "/srv/example/deploy/example.env",
            "/etc/example.env",
            "/srv/example");
        Assert.Contains("sudo -n test ! -L '/srv/example/deploy/example.env'", securedContentCheck, StringComparison.Ordinal);
        Assert.Contains("sudo -n realpath -e -- '/srv/example/deploy/example.env'", securedContentCheck, StringComparison.Ordinal);
        Assert.Contains("sudo -n realpath -e -- '/srv/example'", securedContentCheck, StringComparison.Ordinal);
        Assert.Contains("sudo -n git -c \"safe.directory=$powerforge_managed_repository_real\" -C \"$powerforge_managed_repository_real\" ls-tree 'HEAD' -- 'deploy/example.env'", securedContentCheck, StringComparison.Ordinal);
        Assert.Contains("case \"$powerforge_managed_source_mode\" in 100644|100755)", securedContentCheck, StringComparison.Ordinal);
        Assert.Contains("sudo -n git -c \"safe.directory=$powerforge_managed_repository_real\" -C \"$powerforge_managed_repository_real\" cat-file -t 'HEAD:deploy/example.env'", securedContentCheck, StringComparison.Ordinal);
        Assert.Contains("sudo -n git -c \"safe.directory=$powerforge_managed_repository_real\" -C \"$powerforge_managed_repository_real\" cat-file -p 'HEAD:deploy/example.env' | sudo -n cmp -s -- '/srv/example/deploy/example.env' - || { echo", securedContentCheck, StringComparison.Ordinal);
        Assert.EndsWith(
            "sudo -n cmp -s -- '/srv/example/deploy/example.env' '/etc/example.env'",
            securedContentCheck,
            StringComparison.Ordinal);
        var repositoryManagedCheck = WebCliCommandHandlers.BuildRepositoryManagedFileCheckCommand(
            "/srv/example/deploy/apache.conf",
            "/etc/apache2/sites-available/example.conf",
            "/srv/example");
        Assert.Contains("sudo -n test -f '/etc/apache2/sites-available/example.conf'", repositoryManagedCheck, StringComparison.Ordinal);
        Assert.Contains("sudo -n test ! -L '/etc/apache2/sites-available/example.conf'", repositoryManagedCheck, StringComparison.Ordinal);
        Assert.Contains("stat -c '%U' -- '/etc/apache2/sites-available/example.conf')\" = 'root'", repositoryManagedCheck, StringComparison.Ordinal);
        Assert.Contains("stat -c '%G' -- '/etc/apache2/sites-available/example.conf')\" = 'root'", repositoryManagedCheck, StringComparison.Ordinal);
        Assert.Contains("stat -c '%a' -- '/etc/apache2/sites-available/example.conf')\" = '644'", repositoryManagedCheck, StringComparison.Ordinal);
        Assert.EndsWith(
            "sudo -n cmp -s -- '/srv/example/deploy/apache.conf' '/etc/apache2/sites-available/example.conf'",
            repositoryManagedCheck,
            StringComparison.Ordinal);
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
            "sudo -n test -d '/srv/example' && sudo -n test ! -L '/srv/example' && " +
            "powerforge_repository_path=$(sudo -n realpath -e -- '/srv/example') && test \"$powerforge_repository_path\" = '/srv/example' && " +
            "powerforge_git_root=$(sudo -n git -c \"safe.directory=$powerforge_repository_path\" -C \"$powerforge_repository_path\" rev-parse --show-toplevel) && " +
            "test \"$powerforge_git_root\" = \"$powerforge_repository_path\"",
            WebCliCommandHandlers.BuildRepositoryExistsCheckCommand(path));
        Assert.Equal(
            "sudo -n test -d '/srv/example' && sudo -n test ! -L '/srv/example' && " +
            "powerforge_repository_path=$(sudo -n realpath -e -- '/srv/example') && test \"$powerforge_repository_path\" = '/srv/example' && " +
            "powerforge_git_head=$(sudo -n git -c \"safe.directory=$powerforge_repository_path\" -C \"$powerforge_repository_path\" rev-parse HEAD) && " +
            "powerforge_git_expected=$(sudo -n git -c \"safe.directory=$powerforge_repository_path\" -C \"$powerforge_repository_path\" rev-parse '0123456789abcdef0123456789abcdef01234567^{commit}') && " +
            "test \"$powerforge_git_head\" = \"$powerforge_git_expected\"",
            WebCliCommandHandlers.BuildRepositoryRefCheckCommand(path, reference));
        Assert.Equal(
            "sudo -n test -d '/srv/example' && sudo -n test ! -L '/srv/example' && " +
            "powerforge_repository_path=$(sudo -n realpath -e -- '/srv/example') && test \"$powerforge_repository_path\" = '/srv/example' && " +
            "powerforge_git_status=$(sudo -n git --no-optional-locks -c \"safe.directory=$powerforge_repository_path\" -C \"$powerforge_repository_path\" status --porcelain --untracked-files=normal) && " +
            "test -z \"$powerforge_git_status\"",
            WebCliCommandHandlers.BuildRepositoryCleanCheckCommand(path));
    }
}
