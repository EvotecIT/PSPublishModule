namespace PowerForge.Tests;

public sealed class ServerRecoveryBootstrapPlanTests
{
    [Fact]
    public void JsonOutputMode_DoesNotBecomePlanOutputDirectory()
    {
        var outputDirectory = PowerForge.Web.Cli.WebCliCommandHandlers.ResolveServerPlanOutputDirectory(
            ["--output", "json"]);

        Assert.Null(outputDirectory);
    }

    [Theory]
    [InlineData("--out")]
    [InlineData("--output-dir")]
    public void PlanOutputDirectory_AcceptsDocumentedOptions(string option)
    {
        var outputDirectory = PowerForge.Web.Cli.WebCliCommandHandlers.ResolveServerPlanOutputDirectory(
            [option, "recovery-plan"]);

        Assert.Equal("recovery-plan", outputDirectory);
    }

    [Fact]
    public void BuildPlan_CreatesAccountsBeforeOwnedPathsAndChecksRepositoryPrerequisites()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Accounts =
            [
                new PowerForge.Web.Cli.PowerForgeServerAccount
                {
                    Name = "example-service",
                    System = true,
                    Home = "/nonexistent",
                    Shell = "/usr/sbin/nologin"
                }
            ],
            Paths =
            [
                new PowerForge.Web.Cli.PowerForgeServerPath
                {
                    Id = "service-state",
                    Path = "/var/lib/example-service",
                    Kind = "directory",
                    Owner = "example-service",
                    Group = "example-service",
                    Mode = "750"
                }
            ],
            Repositories =
            [
                new PowerForge.Web.Cli.PowerForgeServerRepository
                {
                    Role = "private-application",
                    Url = "git@example.test:owner/repository.git",
                    Path = "/srv/example",
                    Branch = "main",
                    Ref = "0123456789abcdef0123456789abcdef01234567",
                    BootstrapRequiredFiles = ["/etc/example/id_ed25519", "/etc/example/known_hosts"],
                    SshIdentityFile = "/etc/example/id_ed25519",
                    SshKnownHostsFile = "/etc/example/known_hosts"
                }
            ]
        };

        var warnings = new List<string>();
        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, warnings);

        var account = Assert.Single(steps, step => step.Category == "accounts");
        var path = Assert.Single(steps, step => step.Category == "filesystem");
        var repositorySteps = steps.Where(step => step.Category == "repositories").ToArray();
        Assert.True(account.Order < path.Order);
        Assert.Equal(2, repositorySteps.Length);
        Assert.True(repositorySteps[0].Order < repositorySteps[1].Order);
        Assert.Contains("useradd --system --user-group --no-create-home", account.Command, StringComparison.Ordinal);
        Assert.Contains("test -s '/etc/example/id_ed25519'", repositorySteps[0].Command, StringComparison.Ordinal);
        Assert.Contains("test -s '/etc/example/known_hosts'", repositorySteps[0].Command, StringComparison.Ordinal);
        Assert.Contains("exit 3", repositorySteps[0].Command, StringComparison.Ordinal);
        Assert.Contains("fetch --all --tags --prune", repositorySteps[1].Command, StringComparison.Ordinal);
        Assert.Contains("git clone --branch 'main'", repositorySteps[1].Command, StringComparison.Ordinal);
        Assert.Equal(2, repositorySteps[1].Command!.Split("GIT_SSH_COMMAND=", StringSplitOptions.None).Length - 1);
        Assert.Contains("IdentitiesOnly=yes", repositorySteps[1].Command, StringComparison.Ordinal);
        Assert.Contains("StrictHostKeyChecking=yes", repositorySteps[1].Command, StringComparison.Ordinal);
        Assert.Contains("UserKnownHostsFile=", repositorySteps[1].Command, StringComparison.Ordinal);
        Assert.Contains("git -C '/srv/example' checkout --detach '0123456789abcdef0123456789abcdef01234567'", repositorySteps[1].Command, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    [Fact]
    public void BuildPlan_UsesRerunnableSecretPresenceGuards()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Secrets =
            [
                new PowerForge.Web.Cli.PowerForgeServerSecret { Id = "directory", Path = "/etc/example", RestoreMode = "directory" },
                new PowerForge.Web.Cli.PowerForgeServerSecret { Id = "file", Path = "/etc/example/token", RestoreMode = "file" },
                new PowerForge.Web.Cli.PowerForgeServerSecret { Id = "environment", Env = "EXAMPLE_TOKEN" },
                new PowerForge.Web.Cli.PowerForgeServerSecret { Id = "first-issue-certificate", Path = "/etc/example/certificate", RequiredDuringBootstrap = false }
            ]
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var secrets = steps.Where(step => step.Category == "secrets").ToArray();

        Assert.Equal(3, secrets.Length);
        Assert.DoesNotContain(secrets, step => step.Title.Contains("first-issue-certificate", StringComparison.Ordinal));
        Assert.Contains("test -d '/etc/example'", secrets[0].Command, StringComparison.Ordinal);
        Assert.Contains("-mindepth 1 -maxdepth 1", secrets[0].Command, StringComparison.Ordinal);
        Assert.Contains("test -s '/etc/example/token'", secrets[1].Command, StringComparison.Ordinal);
        Assert.Contains("printenv 'EXAMPLE_TOKEN'", secrets[2].Command, StringComparison.Ordinal);
        Assert.All(secrets, step =>
        {
            Assert.False(step.Manual);
            Assert.False(step.Sensitive);
            Assert.DoesNotContain("TODO", step.Command, StringComparison.Ordinal);
            Assert.Contains("exit 3", step.Command, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BuildPlan_FailsClosedUntilCapturedRepositoryRevisionIsHydrated()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Repositories =
            [
                new PowerForge.Web.Cli.PowerForgeServerRepository
                {
                    Role = "application",
                    Url = "https://github.com/ExampleOrg/ExampleSite.git",
                    Path = "/srv/example",
                    RefCaptureCommandId = "static-source-ref"
                }
            ]
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var guard = Assert.Single(steps, step => step.Title.Contains("captured revision", StringComparison.Ordinal));

        Assert.Contains("Use the captured recovery manifest", guard.Command, StringComparison.Ordinal);
        Assert.Contains("exit 3", guard.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_InstallsSourceManagedFilesAfterRepositories()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Repositories =
            [
                new PowerForge.Web.Cli.PowerForgeServerRepository
                {
                    Role = "application",
                    Url = "https://github.com/ExampleOrg/ExampleSite.git",
                    Path = "/srv/example",
                    Ref = "0123456789abcdef0123456789abcdef01234567"
                }
            ],
            Paths =
            [
                new PowerForge.Web.Cli.PowerForgeServerPath
                {
                    Id = "site-config",
                    Path = "/etc/example/site.env",
                    Source = "/srv/example/deploy/site.env",
                    Kind = "file",
                    Owner = "root",
                    Group = "example",
                    Mode = "0640"
                }
            ]
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var repository = Assert.Single(steps, step => step.Category == "repositories");
        var managedFile = Assert.Single(steps, step => step.Category == "managed-files");

        Assert.True(repository.Order < managedFile.Order);
        Assert.Equal(
            "install -o 'root' -g 'example' -m '0640' '/srv/example/deploy/site.env' '/etc/example/site.env'",
            managedFile.Command);
    }

    [Fact]
    public void RecoveryStages_IncludeDeclarativeBootstrapWorkWithoutLegacyCommands()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Paths =
            [
                new PowerForge.Web.Cli.PowerForgeServerPath
                {
                    Id = "site-config",
                    Path = "/etc/example/site.env",
                    Source = "/srv/example/deploy/site.env",
                    Kind = "file",
                    Owner = "root",
                    Group = "root",
                    Mode = "0640"
                }
            ]
        };

        var stages = PowerForge.Web.Cli.WebCliCommandHandlers.BuildServerRecoveryStages(manifest);

        Assert.Contains("bootstrap", stages);
        Assert.Null(manifest.Bootstrap);
    }

    [Fact]
    public void SudoersManagedFileUsesValidatedAtomicReplacementWithRollback()
    {
        var path = new PowerForge.Web.Cli.PowerForgeServerPath
        {
            Id = "sudoers",
            Path = "/etc/sudoers.d/powerforge-example",
            Source = "/srv/example/deploy/powerforge-example.sudoers",
            Kind = "file",
            Owner = "root",
            Group = "root",
            Mode = "0440",
            Validation = "sudoers"
        };

        var command = PowerForge.Web.Cli.WebCliCommandHandlers.BuildManagedFileInstallCommand(path);

        Assert.Contains("mktemp '/etc/sudoers.d/powerforge-example.powerforge.XXXXXX'", command, StringComparison.Ordinal);
        Assert.Contains("visudo -cf \"$powerforge_sudoers_temp\"", command, StringComparison.Ordinal);
        Assert.Contains("trap powerforge_sudoers_restore EXIT", command, StringComparison.Ordinal);
        Assert.Contains("trap 'exit 130' INT", command, StringComparison.Ordinal);
        Assert.Contains("powerforge_sudoers_replaced=1", command, StringComparison.Ordinal);
        Assert.Contains("powerforge_sudoers_had_previous", command, StringComparison.Ordinal);
        Assert.Contains("mv -f -- \"$powerforge_sudoers_backup\" '/etc/sudoers.d/powerforge-example'", command, StringComparison.Ordinal);
        Assert.Contains("visudo -c || powerforge_sudoers_status=1", command, StringComparison.Ordinal);
        Assert.Contains("trap - EXIT HUP INT TERM", command, StringComparison.Ordinal);
    }
}
