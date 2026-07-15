namespace PowerForge.Tests;

public sealed class ServerRecoveryBootstrapPlanTests
{
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
                    BootstrapRequiredFiles = ["/etc/example/id_ed25519", "/etc/example/known_hosts"]
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
                new PowerForge.Web.Cli.PowerForgeServerSecret { Id = "environment", Env = "EXAMPLE_TOKEN" }
            ]
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var secrets = steps.Where(step => step.Category == "secrets").ToArray();

        Assert.Equal(3, secrets.Length);
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
}
