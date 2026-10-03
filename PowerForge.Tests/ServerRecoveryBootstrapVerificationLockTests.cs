using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoveryBootstrapVerificationLockTests
{
    [Fact]
    public void CallerHeldLockProgram_OmitsShellLockSetupButKeepsUnitReconciliation()
    {
        var manifest = CreateManifest("/var/lock/powerforge-bootstrap-verification-test.lock");

        var standalone = WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest);
        var callerHeld = WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest, operationLocksHeldByCaller: true);

        Assert.Contains("systemd-tmpfiles --create", standalone, StringComparison.Ordinal);
        Assert.Contains("flock -n", standalone, StringComparison.Ordinal);
        Assert.DoesNotContain("systemd-tmpfiles --create", callerHeld, StringComparison.Ordinal);
        Assert.DoesNotContain("flock -n", callerHeld, StringComparison.Ordinal);
        Assert.Contains("trap powerforge_reconcile_disabled_on_exit EXIT", callerHeld, StringComparison.Ordinal);
        Assert.Contains("Stop and disable private-bootstrap-verification.service", callerHeld, StringComparison.Ordinal);
    }

    [Fact]
    public void CallerHeldLockProgram_RequiresLocksButCanVerifyWithoutDisabledUnits()
    {
        var manifest = CreateManifest("/var/lock/powerforge-bootstrap-verification-test.lock");
        manifest.OperationLocks = [];
        Assert.Throws<InvalidOperationException>(() =>
            WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest, operationLocksHeldByCaller: true));

        manifest.OperationLocks = ["/var/lock/powerforge-bootstrap-verification-test.lock"];
        manifest.Systemd = null;
        var callerHeld = WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest, operationLocksHeldByCaller: true);
        Assert.DoesNotContain("flock -n", callerHeld, StringComparison.Ordinal);
        Assert.DoesNotContain("systemd-tmpfiles --create", callerHeld, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_HoldsDeclaredLockDuringVerificationAndReleasesItAfterward(bool enforceDisabled)
    {
        if (!OperatingSystem.IsLinux() ||
            WebCliCommandHandlers.RunLocalServerScript("id -u").Stdout.Trim() != "0" ||
            (enforceDisabled && !WebCliCommandHandlers.RunLocalServerScript(
                "test \"$(systemctl show --property=LoadState --value -- private-bootstrap-verification.service)\" = not-found").Success))
            return;

        var suffix = Guid.NewGuid().ToString("N");
        var root = "/root/powerforge-bootstrap-verification-" + suffix;
        var lockPath = "/var/lock/powerforge-bootstrap-verification-" + suffix + ".lock";
        var tmpfilesPath = WebCliCommandHandlers.GetOperationLockTmpfilesDefinition(lockPath).Path;
        Directory.CreateDirectory(root);
        try
        {
            var manifest = CreateManifest(lockPath);
            if (!enforceDisabled)
                manifest.Systemd = null;
            manifest.Verify!.Commands = [new PowerForgeServerNamedCommand
            {
                Id = "lock-still-held",
                Required = true,
                Command = $"if flock -n '{lockPath}' -c true; then exit 87; fi"
            }];
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, WebCliJson.Options));

            Assert.Equal(0, WebCliCommandHandlers.HandleServer(
                ["bootstrap", "--manifest", manifestPath, "--apply"], false, new(), 1));
            Assert.True(WebCliCommandHandlers.RunLocalServerScript($"flock -n '{lockPath}' -c true").Success);
        }
        finally
        {
            File.Delete(tmpfilesPath);
            File.Delete(lockPath);
            Directory.Delete(root, recursive: true);
        }
    }

    private static PowerForgeServerRecoveryManifest CreateManifest(string lockPath) => new()
    {
        SchemaVersion = 2,
        Target = new PowerForgeServerTarget { Host = "no-such-host.invalid" },
        OperationLocks = [lockPath],
        Systemd = new PowerForgeServerSystemd
        {
            Services = [new PowerForgeServerSystemdUnit
            {
                Name = "private-bootstrap-verification.service",
                EnforceDisabled = true
            }]
        },
        Verify = new PowerForgeServerVerify
        {
            Commands = [new PowerForgeServerNamedCommand { Id = "ready", Command = "true", Required = true }]
        }
    };
}
