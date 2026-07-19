namespace PowerForge.Tests;

public sealed class ServerRecoverySystemdPlanTests
{
    [Fact]
    public void BuildPlan_StartsEnabledUnitsAfterRequiredSecretsAndDeploy()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Secrets =
            [
                new PowerForge.Web.Cli.PowerForgeServerSecret
                {
                    Id = "service-token",
                    Path = "/etc/example/service-token",
                    RestoreMode = "file"
                }
            ],
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Services =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "example.service",
                        Enabled = true
                    },
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "manual.service"
                    }
                ],
                Timers =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "example.timer",
                        Enabled = true
                    }
                ]
            },
            Deploy = new PowerForge.Web.Cli.PowerForgeServerDeploy
            {
                Commands =
                [
                    new PowerForge.Web.Cli.PowerForgeServerNamedCommand
                    {
                        Id = "deploy-example",
                        Command = "/usr/local/sbin/deploy-example"
                    }
                ]
            }
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var secret = Assert.Single(steps, step => step.Title == "Confirm restored secret service-token");
        var deploy = Assert.Single(steps, step => step.Title == "deploy-example");
        var enableService = Assert.Single(steps, step => step.Title == "Enable example.service");
        var enableTimer = Assert.Single(steps, step => step.Title == "Enable example.timer");
        var startService = Assert.Single(steps, step => step.Title == "Start example.service");
        var startTimer = Assert.Single(steps, step => step.Title == "Start example.timer");

        Assert.True(secret.Order < deploy.Order);
        Assert.True(deploy.Order < enableService.Order);
        Assert.True(enableService.Order < startService.Order);
        Assert.True(deploy.Order < enableTimer.Order);
        Assert.True(enableTimer.Order < startTimer.Order);
        Assert.Equal("systemctl enable 'example.service'", enableService.Command);
        Assert.Equal("systemctl enable 'example.timer'", enableTimer.Command);
        Assert.Equal("systemctl start 'example.service'", startService.Command);
        Assert.Equal("systemctl start 'example.timer'", startTimer.Command);
        Assert.DoesNotContain(steps, step => step.Title == "Enable manual.service");
        Assert.DoesNotContain(steps, step => step.Title == "Start manual.service");
    }
}
