namespace PowerForge.Tests;

public sealed class ServerRecoveryBootstrapReconciliationTests
{
    [Fact]
    public void ExistingBranchRepository_FastForwardsOnlyFromCleanExpectedBranch()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Repositories =
            [
                new PowerForge.Web.Cli.PowerForgeServerRepository
                {
                    Role = "application",
                    Url = "git@example.test:owner/application.git",
                    Path = "/srv/application",
                    Branch = "main"
                }
            ]
        };

        var step = Assert.Single(PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []),
            item => item.Title == "Clone or update application repository");
        var command = Assert.IsType<string>(step.Command);

        Assert.Contains("remote get-url origin", command, StringComparison.Ordinal);
        Assert.Contains("symbolic-ref --quiet --short HEAD", command, StringComparison.Ordinal);
        Assert.Contains("merge-base --is-ancestor HEAD 'refs/remotes/origin/main'", command, StringComparison.Ordinal);
        Assert.Contains("merge --ff-only 'refs/remotes/origin/main'", command, StringComparison.Ordinal);
        Assert.True(command.IndexOf("status --porcelain", StringComparison.Ordinal) <
                    command.IndexOf("fetch --all", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitlyDisabledUnit_StopsBeforeRepositoryOrDeployWork()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            SchemaVersion = 2,
            Target = new PowerForge.Web.Cli.PowerForgeServerTarget { SshAlias = "example" },
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Timers =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.timer",
                        Enabled = false,
                        EnforceDisabled = true
                    }
                ]
            },
            Repositories =
            [
                new PowerForge.Web.Cli.PowerForgeServerRepository
                {
                    Role = "application",
                    Url = "https://example.test/application.git",
                    Path = "/srv/application"
                }
            ]
        };

        Assert.Empty(PowerForge.Web.Cli.WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest));
        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var stop = Assert.Single(steps, item => item.Title == "Stop and disable private.timer");
        var clone = Assert.Single(steps, item => item.Title == "Clone or update application repository");
        Assert.True(stop.Order < clone.Order);
        Assert.Contains("systemctl show --property=LoadState --value -- 'private.timer'", stop.Command, StringComparison.Ordinal);
        Assert.Contains("Cannot inspect systemd unit before bootstrap", stop.Command, StringComparison.Ordinal);
        Assert.Contains("not-found) ;; loaded|masked)", stop.Command, StringComparison.Ordinal);
        Assert.Contains("systemctl disable --now -- 'private.timer'", stop.Command, StringComparison.Ordinal);
        Assert.DoesNotContain(steps, item => item.Title == "Enable private.timer");
    }

    [Fact]
    public void EnabledUnit_CannotAlsoEnforceDisabled()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            SchemaVersion = 2,
            Target = new PowerForge.Web.Cli.PowerForgeServerTarget { SshAlias = "example" },
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Timers =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.timer",
                        Enabled = true,
                        EnforceDisabled = true
                    }
                ]
            }
        };

        Assert.Contains(PowerForge.Web.Cli.WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest),
            error => error.Contains("cannot be enabled and enforceDisabled", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-found", false, 0, false)]
    [InlineData("loaded", false, 0, true)]
    [InlineData("loaded", true, 3, false)]
    public void DisabledUnit_PreflightDistinguishesAbsentFromFailedInspection(
        string loadState, bool queryFails, int expectedExitCode, bool expectedDisable)
    {
        if (!OperatingSystem.IsLinux()) return;

        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Timers =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.timer",
                        EnforceDisabled = true
                    }
                ]
            }
        };
        var step = Assert.Single(PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []),
            item => item.Title == "Stop and disable private.timer");
        var script = "set -Eeuo pipefail\n" +
                     "systemctl() { if [ \"$1\" = show ]; then " +
                     (queryFails ? "return 5; " : $"printf '%s\\n' '{loadState}'; ") +
                     "else printf '%s\\n' DISABLED; fi; }\n" + step.Command;
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "/bin/bash";
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(expectedExitCode, process.ExitCode);
        Assert.Equal(expectedDisable, output.Contains("DISABLED", StringComparison.Ordinal));
        if (queryFails)
            Assert.Contains("Cannot inspect systemd unit before bootstrap", error, StringComparison.Ordinal);
    }
}
