using System.Diagnostics;

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

        Assert.Contains("config --get-all remote.origin.url", command, StringComparison.Ordinal);
        Assert.Contains("symbolic-ref --quiet --short HEAD", command, StringComparison.Ordinal);
        Assert.Contains("merge-base --is-ancestor HEAD 'refs/remotes/origin/main'", command, StringComparison.Ordinal);
        Assert.Contains("merge --ff-only 'refs/remotes/origin/main'", command, StringComparison.Ordinal);
        Assert.Contains("fetch --no-tags origin 'refs/heads/main:refs/remotes/origin/main'", command, StringComparison.Ordinal);
        Assert.Contains("ls-files -z --others --ignored --exclude-standard", command, StringComparison.Ordinal);
        Assert.True(command.IndexOf("status --porcelain", StringComparison.Ordinal) <
                    command.IndexOf("fetch --no-tags origin", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingRepository_AcceptsTheDeclaredUrlBeforeGitInsteadOfExpansion()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-rewrite-" + Guid.NewGuid().ToString("N"));
        var origin = Path.Combine(root, "origin.git");
        var checkout = Path.Combine(root, "checkout");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, RunProcess("git", root, "init", "--bare", "--initial-branch=main", origin));
            Assert.Equal(0, RunProcess("git", root, "clone", origin, checkout));
            Assert.Equal(0, RunProcess("git", checkout, "config", "user.name", "Bootstrap Test"));
            Assert.Equal(0, RunProcess("git", checkout, "config", "user.email", "bootstrap@example.test"));
            File.WriteAllText(Path.Combine(checkout, "file.txt"), "initial\n");
            Assert.Equal(0, RunProcess("git", checkout, "add", "file.txt"));
            Assert.Equal(0, RunProcess("git", checkout, "commit", "-m", "initial"));
            Assert.Equal(0, RunProcess("git", checkout, "push", "-u", "origin", "main"));
            Assert.Equal(0, RunProcess("git", checkout, "remote", "set-url", "origin", "bootstrap-alias:origin.git"));
            Assert.Equal(0, RunProcess("git", checkout, "config", "url." + root + "/.insteadOf", "bootstrap-alias:"));

            var repository = new PowerForge.Web.Cli.PowerForgeServerRepository
            {
                Role = "application", Url = "bootstrap-alias:origin.git", Path = checkout, Branch = "main"
            };
            var step = Assert.Single(PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(
                new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest { Repositories = [repository] }, []),
                item => item.Title == "Clone or update application repository");
            var script = "set -Eeuo pipefail\npowerforge_assert_root_controlled_path() { :; }\n" + step.Command;

            Assert.Equal(0, RunProcess("/bin/bash", root, "-c", script));
            Assert.Equal(0, RunProcess("git", checkout, "remote", "set-url", "--add", "origin", "another-alias:origin.git"));
            Assert.Equal(3, RunProcess("/bin/bash", root, "-c", script));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
                Services =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.service",
                        EnforceDisabled = true
                    }
                ],
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
        var stopService = Assert.Single(steps, item => item.Title == "Stop and disable private.service");
        var clone = Assert.Single(steps, item => item.Title == "Clone or update application repository");
        Assert.True(stop.Order < stopService.Order);
        Assert.True(stop.Order < clone.Order);
        Assert.Contains("systemctl show --property=LoadState --value -- 'private.timer'", stop.Command, StringComparison.Ordinal);
        Assert.Contains("Cannot inspect systemd unit before bootstrap", stop.Command, StringComparison.Ordinal);
        Assert.Contains("not-found) powerforge_unit_roots=$(systemd-analyze unit-paths)", stop.Command, StringComparison.Ordinal);
        Assert.Contains("systemctl disable --runtime --now -- 'private.timer'", stop.Command, StringComparison.Ordinal);
        Assert.Contains("systemctl disable --now -- 'private.timer'", stop.Command, StringComparison.Ordinal);
        Assert.Contains("--property=UnitFileState", stop.Command, StringComparison.Ordinal);
        Assert.Contains("--property=ActiveState", stop.Command, StringComparison.Ordinal);
        Assert.DoesNotContain(steps, item => item.Title == "Enable private.timer");
        Assert.DoesNotContain(steps, item => item.Title.Contains("after packages", StringComparison.Ordinal));
        var finalTimer = Assert.Single(steps, item => item.Title == "Reassert stopped and disabled before verification private.timer");
        Assert.True(clone.Order < finalTimer.Order);
    }

    [Fact]
    public void PackageInstallation_ReassertsDisabledTimersAndServicesBeforeRepositoryMutation()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Packages = new PowerForge.Web.Cli.PowerForgeServerPackages
            {
                Apt = ["git"],
                DotnetSdks = ["10.0"],
                Powershell = true
            },
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Timers = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                {
                    Name = "private.timer", EnforceDisabled = true
                }],
                Services = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                {
                    Name = "private.service", EnforceDisabled = true
                }]
            },
            Repositories = [new PowerForge.Web.Cli.PowerForgeServerRepository
            {
                Role = "application",
                Url = "https://example.test/application.git",
                Path = "/srv/application"
            }],
            Deploy = new PowerForge.Web.Cli.PowerForgeServerDeploy
            {
                Commands = [new PowerForge.Web.Cli.PowerForgeServerNamedCommand
                {
                    Id = "deploy", Command = "true"
                }]
            }
        };

        var steps = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        var firstTimer = Assert.Single(steps, item => item.Title == "Stop and disable private.timer");
        var firstService = Assert.Single(steps, item => item.Title == "Stop and disable private.service");
        var lastPackage = Assert.Single(steps, item => item.Title == "Install PowerShell prerequisite");
        var secondTimer = Assert.Single(steps, item => item.Title == "Reassert stopped and disabled after packages private.timer");
        var secondService = Assert.Single(steps, item => item.Title == "Reassert stopped and disabled after packages private.service");
        var repository = Assert.Single(steps, item => item.Title == "Clone or update application repository");
        var deploy = Assert.Single(steps, item => item.Title == "deploy");
        var finalTimer = Assert.Single(steps, item => item.Title == "Reassert stopped and disabled before verification private.timer");
        var finalService = Assert.Single(steps, item => item.Title == "Reassert stopped and disabled before verification private.service");

        Assert.True(firstTimer.Order < firstService.Order);
        Assert.True(firstService.Order < lastPackage.Order);
        Assert.True(lastPackage.Order < secondTimer.Order);
        Assert.True(secondTimer.Order < secondService.Order);
        Assert.True(secondService.Order < repository.Order);
        Assert.True(repository.Order < deploy.Order);
        Assert.True(deploy.Order < finalTimer.Order);
        Assert.True(finalTimer.Order < finalService.Order);
        Assert.Equal(firstTimer.Command, secondTimer.Command);
        Assert.Equal(firstService.Command, secondService.Command);
        Assert.Equal(firstTimer.Command, finalTimer.Command);
        Assert.Contains("not-found) powerforge_unit_roots=$(systemd-analyze unit-paths)", firstTimer.Command, StringComparison.Ordinal);
        var apt = Assert.Single(steps, item => item.Title == "Install apt prerequisites");
        Assert.Contains("bash -Eeuo pipefail -c", apt.Command, StringComparison.Ordinal);
        Assert.Contains("powerforge_package_status=$?", apt.Command, StringComparison.Ordinal);
        Assert.Contains("systemctl disable --now -- 'private.timer'", apt.Command, StringComparison.Ordinal);
        Assert.Contains("systemctl disable --now -- 'private.service'", apt.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedPackageInstallation_StopsAUnitStartedBeforeThePackageFailure()
    {
        if (!OperatingSystem.IsLinux()) return;

        var statePath = Path.Combine(Path.GetTempPath(), "powerforge-package-state-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(statePath, "inactive");
        try
        {
            var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
            {
                Packages = new PowerForge.Web.Cli.PowerForgeServerPackages { Apt = ["example-package"] },
                Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
                {
                    Services = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.service", EnforceDisabled = true
                    }]
                }
            };
            var step = Assert.Single(PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []),
                item => item.Title == "Install apt prerequisites");
            var script = "set -Eeuo pipefail\n" +
                         "export POWERFORGE_TEST_STATE='" + statePath + "'\n" +
                         "apt-get() { if [ \"$1\" = update ]; then return 0; fi; " +
                         "printf active > \"$POWERFORGE_TEST_STATE\"; return 7; }\n" +
                         "export -f apt-get\n" +
                         "systemctl() { if [ \"$1\" = show ]; then case \"$2\" in " +
                         "--property=LoadState) printf '%s\\n' loaded;; " +
                         "--property=UnitFileState) printf '%s\\n' disabled;; " +
                         "--property=ActiveState) cat \"$POWERFORGE_TEST_STATE\";; esac; " +
                         "else printf inactive > \"$POWERFORGE_TEST_STATE\"; fi; }\n" +
                         step.Command;

            Assert.Equal(7, RunProcess("/bin/bash", Path.GetTempPath(), "-c", script));
            Assert.Equal("inactive", File.ReadAllText(statePath));
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    [Fact]
    public void PackageReconciliation_AttemptsOtherUnitsWhenOneQueryFails()
    {
        if (!OperatingSystem.IsLinux()) return;

        var logPath = Path.Combine(Path.GetTempPath(), "powerforge-reconcile-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
            {
                Packages = new PowerForge.Web.Cli.PowerForgeServerPackages { Apt = ["example-package"] },
                Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
                {
                    Timers = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit { Name = "private.timer", EnforceDisabled = true }],
                    Services = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit { Name = "private.service", EnforceDisabled = true }]
                }
            };
            var step = Assert.Single(PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []),
                item => item.Title == "Install apt prerequisites");
            var script = "set -Eeuo pipefail\n" +
                         "export POWERFORGE_TEST_LOG='" + logPath + "'\n" +
                         "apt-get() { return 0; }\nexport -f apt-get\n" +
                         "systemctl() { case \"$*\" in *private.timer*) return 3;; esac; " +
                         "if [ \"$1\" = show ]; then case \"$2\" in " +
                         "--property=LoadState) printf '%s\\n' loaded;; " +
                         "--property=UnitFileState) printf '%s\\n' disabled;; " +
                         "--property=ActiveState) printf '%s\\n' inactive;; esac; " +
                         "else printf 'service\\n' >> \"$POWERFORGE_TEST_LOG\"; fi; }\n" +
                         step.Command;

            Assert.Equal(3, RunProcess("/bin/bash", Path.GetTempPath(), "-c", script));
            Assert.Equal(2, File.ReadAllLines(logPath).Length);
        }
        finally
        {
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }

    [Fact]
    public void FailedBootstrapStep_ReconcilesDisabledUnitAndPreservesOriginalExitCode()
    {
        if (!OperatingSystem.IsLinux()) return;

        var statePath = Path.Combine(Path.GetTempPath(), "powerforge-bootstrap-exit-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(statePath, "inactive");
        try
        {
            var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
            {
                Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
                {
                    Services = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.service", EnforceDisabled = true
                    }]
                }
            };
            var mockSystemctl = "export POWERFORGE_TEST_STATE='" + statePath + "'\n" +
                                "systemctl() { if [ \"$1\" = show ]; then case \"$2\" in " +
                                "--property=LoadState) printf '%s\\n' loaded;; " +
                                "--property=UnitFileState) printf '%s\\n' disabled;; " +
                                "--property=ActiveState) cat \"$POWERFORGE_TEST_STATE\";; esac; " +
                                "else printf inactive > \"$POWERFORGE_TEST_STATE\"; fi; }";
            // The real systemctl binary is visible to both the outer cleanup shell and
            // the inner step shell. Define the test double in the outer shell likewise.
            var script = mockSystemctl + "\n" + PowerForge.Web.Cli.WebCliCommandHandlers.RenderBootstrapPlanScript([
                new() { Order = 1, Command = ":" },
                // Operation-lock, sudoers, and Apache steps use their own EXIT traps.
                new() { Order = 2, Command = "trap ':' EXIT; trap - EXIT" },
                new() { Order = 3, Command = "printf active > \"$POWERFORGE_TEST_STATE\"; exit 7" }
            ], manifest);

            Assert.Contains("trap powerforge_reconcile_disabled_on_exit EXIT", script, StringComparison.Ordinal);
            Assert.Contains("trap - EXIT", PowerForge.Web.Cli.WebCliCommandHandlers.BuildOperationLockInstallCommand(
                "/var/lock/powerforge-test.lock"), StringComparison.Ordinal);
            Assert.Equal(7, RunProcess("/bin/bash", Path.GetTempPath(), "-c", script));
            Assert.Equal("inactive", File.ReadAllText(statePath));
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    [Fact]
    public void FailedBootstrapStep_HoldsOperationLockUntilDisabledUnitReconciliationCompletes()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-bootstrap-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var lockPath = Path.Combine(root, "operation.lock");
        var probePath = Path.Combine(root, "probe.txt");
        File.WriteAllText(lockPath, string.Empty);
        try
        {
            var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
            {
                OperationLocks = [lockPath],
                Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
                {
                    Services = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.service", EnforceDisabled = true
                    }]
                }
            };
            var acquire = PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapOperationLockAcquireCommand([lockPath]);
            var script = "stat() { printf '%s\\n' 'root:root 644'; }\n" +
                         "export POWERFORGE_TEST_LOCK='" + lockPath + "'\n" +
                         "export POWERFORGE_TEST_PROBE='" + probePath + "'\n" +
                         "systemctl() { if [ \"$1\" = show ]; then case \"$2\" in " +
                         "--property=LoadState) printf '%s\\n' loaded;; " +
                         "--property=UnitFileState) printf '%s\\n' disabled;; " +
                         "--property=ActiveState) printf '%s\\n' inactive;; esac; " +
                         "else if flock -n \"$POWERFORGE_TEST_LOCK\" -c true; then " +
                         "printf unlocked > \"$POWERFORGE_TEST_PROBE\"; else " +
                         "printf locked > \"$POWERFORGE_TEST_PROBE\"; fi; fi; }\n" +
                         PowerForge.Web.Cli.WebCliCommandHandlers.RenderBootstrapPlanScript([
                             new() { Order = 1, Command = acquire },
                             new() { Order = 2, Command = "trap ':' EXIT; trap - EXIT; exit 7" }
                         ], manifest);

            Assert.Equal(7, RunProcess("/bin/bash", root, "-c", script));
            Assert.Equal("locked", File.ReadAllText(probePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DisabledUnitBootstrap_RejectsCommandOwnedDeployLocks()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            OperationLocks = ["/var/lock/powerforge-example.lock"],
            Deploy = new PowerForge.Web.Cli.PowerForgeServerDeploy { OperationLockOwner = "command" },
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Services = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                {
                    Name = "private.service", EnforceDisabled = true
                }]
            }
        };

        var failure = Assert.Throws<InvalidOperationException>(() =>
            PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []));
        Assert.Contains("engine-owned locks", failure.Message, StringComparison.Ordinal);
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

    [Fact]
    public void DuplicateUnit_CannotReactivateAnEnforcedDisabledService()
    {
        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Services =
                [
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.service", EnforceDisabled = true
                    },
                    new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.service", Enabled = true,
                        Activation = PowerForge.Web.Cli.PowerForgeServerSystemdActivation.AfterDeploy
                    }
                ]
            }
        };

        Assert.Contains(PowerForge.Web.Cli.WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest),
            error => error.Contains("duplicates systemd unit 'private.service'", StringComparison.Ordinal));
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
                     (queryFails ? "return 5; " :
                         "case \"$2\" in --property=LoadState) printf '%s\\n' '" + loadState +
                         "';; --property=UnitFileState) printf '%s\\n' disabled;; " +
                         "--property=ActiveState) printf '%s\\n' inactive;; esac; ") +
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
        if (expectedDisable)
            Assert.Equal(2, output.Split("DISABLED", StringSplitOptions.None).Length - 1);
        if (queryFails)
            Assert.Contains("Cannot inspect systemd unit before bootstrap", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AbsentUnit_RejectsDanglingEnablementBeforeLaterInstallation()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-dangling-unit-" + Guid.NewGuid().ToString("N"));
        var wants = Path.Combine(root, "multi-user.target.wants");
        Directory.CreateDirectory(wants);
        try
        {
            var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
            {
                Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
                {
                    Services = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                    {
                        Name = "private.service", EnforceDisabled = true
                    }]
                }
            };
            var step = Assert.Single(PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []),
                item => item.Title == "Stop and disable private.service");
            var script = "set -Eeuo pipefail\n" +
                         "export POWERFORGE_TEST_UNIT_ROOT='" + root + "'\n" +
                         "systemd-analyze() { printf '%s\\n' \"$POWERFORGE_TEST_UNIT_ROOT\"; }\n" +
                         "systemctl() { printf '%s\\n' not-found; }\n" + step.Command;
            var link = Path.Combine(wants, "private.service");
            File.CreateSymbolicLink(link, "../private.service");

            Assert.Equal(3, RunProcess("/bin/bash", root, "-c", script));
            File.Delete(link);
            Assert.Equal(0, RunProcess("/bin/bash", root, "-c", script));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("enabled-runtime", "inactive")]
    [InlineData("disabled", "activating")]
    [InlineData("static", "inactive")]
    public void DisabledUnit_RejectsResidualEnablementOrActivity(string fileState, string activeState)
    {
        if (!OperatingSystem.IsLinux()) return;

        var manifest = new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest
        {
            Systemd = new PowerForge.Web.Cli.PowerForgeServerSystemd
            {
                Timers = [new PowerForge.Web.Cli.PowerForgeServerSystemdUnit
                {
                    Name = "private.timer", EnforceDisabled = true
                }]
            }
        };
        var step = Assert.Single(PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []),
            item => item.Title == "Stop and disable private.timer");
        var script = "set -Eeuo pipefail\n" +
                     "systemctl() { if [ \"$1\" = show ]; then case \"$2\" in " +
                     "--property=LoadState) printf '%s\\n' loaded;; " +
                     "--property=UnitFileState) printf '%s\\n' '" + fileState + "';; " +
                     "--property=ActiveState) printf '%s\\n' '" + activeState + "';; esac; " +
                     "else :; fi; }\n" + step.Command;
        Assert.Equal(3, RunProcess("/bin/bash", Path.GetTempPath(), "-c", script));
    }

    [Fact]
    public void ExistingRepository_FetchesDeclaredBranchAndPreservesIgnoredSecretOnIncomingCollision()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.Combine(Path.GetTempPath(), "powerforge-reconcile-" + Guid.NewGuid().ToString("N"));
        var origin = Path.Combine(root, "origin.git");
        var checkout = Path.Combine(root, "checkout");
        var writer = Path.Combine(root, "writer");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, RunProcess("git", root, "init", "--bare", "--initial-branch=main", origin));
            Assert.Equal(0, RunProcess("git", root, "clone", origin, checkout));
            Assert.Equal(0, RunProcess("git", checkout, "config", "user.name", "Bootstrap Test"));
            Assert.Equal(0, RunProcess("git", checkout, "config", "user.email", "bootstrap@example.test"));
            File.WriteAllText(Path.Combine(checkout, ".gitignore"), "secret.env\n");
            File.WriteAllText(Path.Combine(checkout, "public.txt"), "initial\n");
            Directory.CreateDirectory(Path.Combine(checkout, "config"));
            File.WriteAllText(Path.Combine(checkout, "config", "appsettings.json"), "{}\n");
            Assert.Equal(0, RunProcess("git", checkout, "add", ".gitignore", "public.txt", "config/appsettings.json"));
            Assert.Equal(0, RunProcess("git", checkout, "commit", "-m", "initial"));
            Assert.Equal(0, RunProcess("git", checkout, "push", "-u", "origin", "main"));
            Assert.Equal(0, RunProcess("git", root, "clone", origin, writer));
            Assert.Equal(0, RunProcess("git", writer, "config", "user.name", "Bootstrap Test"));
            Assert.Equal(0, RunProcess("git", writer, "config", "user.email", "bootstrap@example.test"));

            File.WriteAllText(Path.Combine(writer, "public.txt"), "updated\n");
            Assert.Equal(0, RunProcess("git", writer, "commit", "-am", "public update"));
            Assert.Equal(0, RunProcess("git", writer, "push", "origin", "main"));
            Assert.Equal(0, RunProcess("git", checkout, "config", "remote.origin.skipDefaultUpdate", "true"));
            var nestedSecretPath = Path.Combine(checkout, "config", "secret.env");
            File.WriteAllText(nestedSecretPath, "nested-local-secret\n");

            var repository = new PowerForge.Web.Cli.PowerForgeServerRepository
            {
                Role = "application", Url = origin, Path = checkout, Branch = "main"
            };
            var step = Assert.Single(PowerForge.Web.Cli.WebCliCommandHandlers.BuildBootstrapPlanSteps(
                new PowerForge.Web.Cli.PowerForgeServerRecoveryManifest { Repositories = [repository] }, []),
                item => item.Title == "Clone or update application repository");
            var script = "set -Eeuo pipefail\npowerforge_assert_root_controlled_path() { :; }\n" + step.Command;
            Assert.Equal(0, RunProcess("/bin/bash", root, "-c", script));
            Assert.Equal("updated\n", File.ReadAllText(Path.Combine(checkout, "public.txt")));
            Assert.Equal("nested-local-secret\n", File.ReadAllText(nestedSecretPath));

            var secretPath = Path.Combine(checkout, "secret.env");
            File.WriteAllText(secretPath, "local-secret-must-survive\n");
            File.WriteAllText(Path.Combine(writer, "secret.env"), "remote-content\n");
            Assert.Equal(0, RunProcess("git", writer, "add", "-f", "secret.env"));
            Assert.Equal(0, RunProcess("git", writer, "commit", "-m", "new tracked path"));
            Assert.Equal(0, RunProcess("git", writer, "push", "origin", "main"));

            Assert.Equal(3, RunProcess("/bin/bash", root, "-c", script));
            Assert.Equal("local-secret-must-survive\n", File.ReadAllText(secretPath));
            Assert.Equal("updated\n", File.ReadAllText(Path.Combine(checkout, "public.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int RunProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 || fileName == "/bin/bash",
            $"{fileName} {string.Join(' ', arguments.Take(3))} failed: {stdout} {stderr}");
        return process.ExitCode;
    }
}
