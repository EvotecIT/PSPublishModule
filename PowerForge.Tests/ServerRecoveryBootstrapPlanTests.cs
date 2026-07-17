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
    public void BuildPlan_DoesNotInventUndeclaredSystemdOrFirewallWork()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest();

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);

        Assert.DoesNotContain(steps, step => step.Category == "systemd");
        Assert.DoesNotContain(steps, step => step.Category == "firewall");
        Assert.DoesNotContain(steps, step => step.Command?.Contains("ufw --force enable", StringComparison.Ordinal) == true);
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
        Assert.Contains("test ! -L '/srv/example/deploy/site.env'", managedFile.Command, StringComparison.Ordinal);
        Assert.Contains("realpath -e -- '/srv/example/deploy/site.env'", managedFile.Command, StringComparison.Ordinal);
        Assert.Contains("realpath -e -- '/srv/example'", managedFile.Command, StringComparison.Ordinal);
        Assert.EndsWith(
            "install -T -o 'root' -g 'example' -m '0640' '/srv/example/deploy/site.env' '/etc/example/site.env'",
            managedFile.Command,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_UsesManagedFileSafetyForApacheAndSystemd()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Repositories =
            [
                new PowerForge.Web.Cli.PowerForgeServerRepository
                {
                    Role = "application",
                    Url = "https://github.com/ExampleOrg/ExampleSite.git",
                    Path = "/srv/example"
                }
            ],
            Apache = new PowerForge.Web.Cli.PowerForgeServerApache
            {
                Sites =
                [
                    new PowerForge.Web.Cli.PowerForgeServerManagedFile
                    {
                        Source = "/srv/example/deploy/apache.conf",
                        Target = "/etc/apache2/sites-available/example.conf"
                    }
                ]
            },
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Services =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "example.service",
                        Source = "/srv/example/deploy/example.service",
                        Target = "/etc/systemd/system/example.service"
                    }
                ]
            }
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var apache = Assert.Single(steps, step => step.Title == "Install Apache file /etc/apache2/sites-available/example.conf");
        var systemd = Assert.Single(steps, step => step.Title == "Install systemd unit example.service");

        Assert.Contains("test ! -L '/srv/example/deploy/apache.conf'", apache.Command, StringComparison.Ordinal);
        Assert.EndsWith(
            "install -T -o 'root' -g 'root' -m '0644' '/srv/example/deploy/apache.conf' '/etc/apache2/sites-available/example.conf'",
            apache.Command,
            StringComparison.Ordinal);
        Assert.Contains("test ! -L '/srv/example/deploy/example.service'", systemd.Command, StringComparison.Ordinal);
        Assert.EndsWith(
            "install -T -o 'root' -g 'root' -m '0644' '/srv/example/deploy/example.service' '/etc/systemd/system/example.service'",
            systemd.Command,
            StringComparison.Ordinal);
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

        var runtimeOnly = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Packages = new PowerForge.Web.Cli.PowerForgeServerPackages
            {
                DotnetSdks = ["10.0"],
                Powershell = true
            },
            Paths =
            [
                new PowerForge.Web.Cli.PowerForgeServerPath
                {
                    Id = "current",
                    Path = "/var/www/example/current",
                    Kind = "symlink"
                }
            ]
        };

        Assert.Contains(
            "bootstrap",
            PowerForge.Web.Cli.WebCliCommandHandlers.BuildServerRecoveryStages(runtimeOnly));
    }

    [Fact]
    public void BuildPlan_InstallsDeclaredRuntimesAndFailsClosedOnHostMismatch()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Target = new PowerForge.Web.Cli.PowerForgeServerTarget
            {
                Os = "ubuntu-24.04",
                Architecture = "x64"
            },
            Packages = new PowerForge.Web.Cli.PowerForgeServerPackages
            {
                DotnetSdks = ["8", "10.0"],
                Powershell = true
            }
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var preflight = Assert.Single(steps, step => step.Category == "preflight");
        var runtimes = steps.Where(step => step.Category == "runtimes").ToArray();

        Assert.Equal(3, runtimes.Length);
        Assert.Equal(
            "test -r /etc/os-release && . /etc/os-release && test \"$ID\" = 'ubuntu' && " +
            "test \"$VERSION_ID\" = '24.04' && test \"$(uname -m)\" = 'x86_64'",
            preflight.Command);
        Assert.Equal("apt-get update && apt-get install -y 'dotnet-sdk-8.0' 'dotnet-sdk-10.0'", runtimes[0].Command);
        Assert.Contains("packages.microsoft.com/config/ubuntu/${VERSION_ID}/packages-microsoft-prod.deb", runtimes[1].Command, StringComparison.Ordinal);
        Assert.Contains("apt-get install -y ca-certificates curl", runtimes[1].Command, StringComparison.Ordinal);
        Assert.Contains("dpkg -i \"$powerforge_ms_repo\"", runtimes[1].Command, StringComparison.Ordinal);
        Assert.Contains("trap 'exit 130' INT", runtimes[1].Command, StringComparison.Ordinal);
        Assert.DoesNotContain("| dpkg", runtimes[1].Command, StringComparison.Ordinal);
        Assert.Equal("apt-get install -y powershell", runtimes[2].Command);
    }

    [Fact]
    public void BuildPlan_UsesUbuntuFeedForDotnetWithoutPowerShell()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Target = new PowerForge.Web.Cli.PowerForgeServerTarget
            {
                Os = "ubuntu-24.04",
                Architecture = "x64"
            },
            Packages = new PowerForge.Web.Cli.PowerForgeServerPackages
            {
                DotnetSdks = ["10.0"],
                Powershell = false
            }
        };

        var runtimes = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, [])
            .Where(step => step.Category == "runtimes")
            .ToArray();

        var runtime = Assert.Single(runtimes);
        Assert.Equal("apt-get update && apt-get install -y 'dotnet-sdk-10.0'", runtime.Command);
        Assert.DoesNotContain("packages.microsoft.com", runtime.Command, StringComparison.Ordinal);
        Assert.DoesNotContain(runtimes, step => step.Command?.Contains("apt-get install -y powershell", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("8", "8.0")]
    [InlineData("8.0", "8.0")]
    [InlineData("10", "10.0")]
    [InlineData("10.0", "10.0")]
    public void DotnetSdkVersionsNormalizeToAptPackageVersions(string value, string expected)
    {
        Assert.True(PowerForge.Web.Cli.WebCliCommandHandlers.TryNormalizeDotnetSdkVersion(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2.1")]
    [InlineData("3.1")]
    [InlineData("4")]
    [InlineData("4.0")]
    [InlineData("6.0")]
    [InlineData("8.1")]
    [InlineData("9.0")]
    [InlineData("10.99")]
    [InlineData("11.0")]
    [InlineData("100")]
    public void DotnetSdkVersionsRejectImpossibleAptPackageVersions(string value)
    {
        Assert.False(PowerForge.Web.Cli.WebCliCommandHandlers.TryNormalizeDotnetSdkVersion(value, out _));
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

        var command = PowerForge.Web.Cli.WebCliCommandHandlers.BuildManagedFileInstallCommand(path, "/srv/example");

        Assert.Contains("test ! -L '/srv/example/deploy/powerforge-example.sudoers'", command, StringComparison.Ordinal);
        Assert.Contains("realpath -e -- '/srv/example/deploy/powerforge-example.sudoers'", command, StringComparison.Ordinal);
        Assert.Contains("case \"$powerforge_managed_source_real\"", command, StringComparison.Ordinal);
        Assert.Contains("install -T", command, StringComparison.Ordinal);
        Assert.Contains("mktemp '/etc/sudoers.d/powerforge-example.powerforge.XXXXXX'", command, StringComparison.Ordinal);
        Assert.Contains("visudo -cf \"$powerforge_sudoers_temp\"", command, StringComparison.Ordinal);
        Assert.Contains("trap powerforge_sudoers_restore EXIT", command, StringComparison.Ordinal);
        Assert.Contains("trap 'exit 130' INT", command, StringComparison.Ordinal);
        Assert.Contains("powerforge_sudoers_replaced=1", command, StringComparison.Ordinal);
        Assert.Contains("powerforge_sudoers_had_previous", command, StringComparison.Ordinal);
        Assert.Contains("test -f '/etc/sudoers.d/powerforge-example' && test ! -L '/etc/sudoers.d/powerforge-example'", command, StringComparison.Ordinal);
        Assert.Contains("mv -fT -- \"$powerforge_sudoers_backup\" '/etc/sudoers.d/powerforge-example'", command, StringComparison.Ordinal);
        Assert.Contains("visudo -c || powerforge_sudoers_status=1", command, StringComparison.Ordinal);
        Assert.Contains("trap - EXIT HUP INT TERM", command, StringComparison.Ordinal);

        var lines = command.Split('\n');
        var rollbackArmed = Array.IndexOf(lines, "powerforge_sudoers_replaced=1");
        var replacement = Array.IndexOf(lines, "mv -fT -- \"$powerforge_sudoers_temp\" '/etc/sudoers.d/powerforge-example'");
        Assert.True(rollbackArmed >= 0 && rollbackArmed < replacement);
    }
}
