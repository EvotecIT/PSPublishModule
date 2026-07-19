namespace PowerForge.Tests;

public sealed class ServerRecoverySystemdPlanTests
{
    [Fact]
    public void BuildPlan_ReacquiresCommandOwnedLocksForAfterDeployActivation()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            OperationLocks = ["/var/lock/powerforge-example.lock"],
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
                        Enabled = true,
                        Activation = PowerForge.Web.Cli.PowerForgeServerSystemdActivation.AfterDeploy
                    }
                ]
            },
            Deploy = new PowerForge.Web.Cli.PowerForgeServerDeploy
            {
                OperationLockOwner = "command",
                Commands =
                [
                    new PowerForge.Web.Cli.PowerForgeServerNamedCommand
                    {
                        Id = "deploy-example",
                        Command = "/usr/local/sbin/deploy-example",
                        Required = true
                    }
                ]
            }
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var secret = Assert.Single(steps, step => step.Title == "Confirm restored secret service-token");
        var release = Assert.Single(steps, step => step.Title == "Release shared operation locks for command-owned deployment");
        var deploy = Assert.Single(steps, step => step.Title == "deploy-example");
        var reacquire = Assert.Single(steps, step => step.Title == "Reacquire shared operation locks after command-owned deployment");
        var enable = Assert.Single(steps, step => step.Title == "Enable example.service");
        var start = Assert.Single(steps, step => step.Title == "Start example.service");

        Assert.True(secret.Order < release.Order);
        Assert.True(release.Order < deploy.Order);
        Assert.True(deploy.Order < reacquire.Order);
        Assert.True(reacquire.Order < enable.Order);
        Assert.True(enable.Order < start.Order);
        Assert.Contains("flock -n \"$powerforge_operation_lock_fd_1\"", reacquire.Command, StringComparison.Ordinal);
        Assert.Equal("systemctl enable -- 'example.service'", enable.Command);
        Assert.Equal("systemctl start -- 'example.service'", start.Command);
    }

    [Fact]
    public void BuildPlan_ActivatesBeforeDeployPrerequisitesAfterSecrets()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Secrets =
            [
                new PowerForge.Web.Cli.PowerForgeServerSecret
                {
                    Id = "timer-token",
                    Path = "/etc/example/timer-token",
                    RestoreMode = "file"
                }
            ],
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Timers =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "example.timer",
                        Enabled = true,
                        Activation = PowerForge.Web.Cli.PowerForgeServerSystemdActivation.BeforeDeploy
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
        var secret = Assert.Single(steps, step => step.Title == "Confirm restored secret timer-token");
        var enable = Assert.Single(steps, step => step.Title == "Enable example.timer");
        var start = Assert.Single(steps, step => step.Title == "Start example.timer");
        var deploy = Assert.Single(steps, step => step.Title == "deploy-example");

        Assert.True(secret.Order < enable.Order);
        Assert.True(enable.Order < start.Order);
        Assert.True(start.Order < deploy.Order);
    }

    [Fact]
    public void BuildPlan_PreservesLegacyEnableOnlyBehaviorWithoutActivation()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Timers =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "legacy.timer",
                        Enabled = true
                    }
                ]
            }
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);

        Assert.Single(steps, step => step.Title == "Enable legacy.timer");
        Assert.DoesNotContain(steps, step => step.Title == "Start legacy.timer");
    }

    [Fact]
    public void BootstrapScript_FailsClosedBeforeActivationForRequiredSensitiveDeploy()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Services =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "example.service",
                        Enabled = true,
                        Activation = PowerForge.Web.Cli.PowerForgeServerSystemdActivation.AfterDeploy
                    }
                ]
            },
            Deploy = new PowerForge.Web.Cli.PowerForgeServerDeploy
            {
                Commands =
                [
                    new PowerForge.Web.Cli.PowerForgeServerNamedCommand
                    {
                        Id = "sensitive-deploy",
                        Command = "/usr/local/sbin/deploy-example --token secret-env-reference",
                        Required = true,
                        Sensitive = true
                    }
                ]
            }
        };
        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var deploy = Assert.Single(steps, step => step.Title == "sensitive-deploy");
        var scriptPath = Path.GetTempFileName();
        try
        {
            PowerForge.Web.Cli.WebCliCommandHandlers.WriteBootstrapPlanScript(scriptPath, steps);
            var script = File.ReadAllText(scriptPath);
            var manualStop = script.IndexOf("Manual bootstrap step required: sensitive-deploy", StringComparison.Ordinal);
            var activation = script.IndexOf("systemctl start -- 'example.service'", StringComparison.Ordinal);

            Assert.True(deploy.Manual);
            Assert.True(manualStop >= 0);
            Assert.True(activation > manualStop);
            Assert.Contains("exit 3", script[manualStop..activation], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public void Validation_RejectsUnsupportedOrNonEnabledActivation()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Target = new PowerForge.Web.Cli.PowerForgeServerTarget { Host = "example.test" },
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Services =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "invalid.service",
                        Enabled = true,
                        Activation = "duringDeploy"
                    },
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "disabled.service",
                        Activation = PowerForge.Web.Cli.PowerForgeServerSystemdActivation.AfterDeploy
                    },
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "--help.service"
                    },
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "wrong.timer"
                    },
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "transient.service",
                        ExpectedState = PowerForge.Web.Cli.PowerForgeServerSystemdState.Active
                    },
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "unknown-state.service",
                        Enabled = true,
                        Activation = PowerForge.Web.Cli.PowerForgeServerSystemdActivation.AfterDeploy,
                        ExpectedState = "failed"
                    }
                ]
            }
        };

        var errors = PowerForge.Web.Cli.WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("unsupported activation phase 'duringDeploy'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'disabled.service' must be enabled", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("safe .service unit name", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'transient.service' must declare activation", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unsupported expected state 'failed'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_RejectsWhitespaceCommandsAndPlannerFailsClosed()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Target = new PowerForge.Web.Cli.PowerForgeServerTarget { Host = "example.test" },
            Deploy = new PowerForge.Web.Cli.PowerForgeServerDeploy
            {
                Commands =
                [
                    new PowerForge.Web.Cli.PowerForgeServerNamedCommand
                    {
                        Id = "missing-deploy",
                        Command = "   ",
                        Required = true,
                        Sensitive = true
                    }
                ]
            }
        };

        var errors = PowerForge.Web.Cli.WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("deploy.commands[0].command must contain a non-whitespace command", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() =>
            PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []));
    }

    [Fact]
    public void BuildPlan_DoesNotDeduplicateRequiredSensitiveBarrier()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Deploy = new PowerForge.Web.Cli.PowerForgeServerDeploy
            {
                Commands =
                [
                    new PowerForge.Web.Cli.PowerForgeServerNamedCommand
                    {
                        Id = "preflight",
                        Command = "/usr/local/sbin/deploy-example"
                    },
                    new PowerForge.Web.Cli.PowerForgeServerNamedCommand
                    {
                        Id = "operator-deploy",
                        Command = "/usr/local/sbin/deploy-example",
                        Required = true,
                        Sensitive = true
                    }
                ]
            }
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);

        Assert.Single(steps, step => step.Title == "preflight");
        var barrier = Assert.Single(steps, step => step.Title == "operator-deploy");
        Assert.True(barrier.Manual);
    }
}
