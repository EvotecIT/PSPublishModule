using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;

namespace PowerForge.Tests;

[Trait("Category", "DotNetPublishPrGate")]
public sealed class ServiceAdoptionStateProcessTests
{
    [Theory]
    [InlineData("Running", false)]
    [InlineData("Running", true)]
    [InlineData("Stopped", false)]
    [InlineData("Stopped", true)]
    public async Task RollbackStopsTheReplacementAndRestoresOriginalAvailability(string original, bool replacementStarted)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backup = Path.Combine(root, "b");
            var status = Path.Combine(root, "status.txt");
            var actions = Path.Combine(root, "actions.txt");
            File.WriteAllText(status, original);
            File.WriteAllText(backup, "inert original executable command");
            var harness = Path.Combine(root, "Harness.ps1");
            File.WriteAllText(harness, """
                $ErrorActionPreference='Stop'
                function Get-Service {
                    param($Name)
                    if($Name -ne 'Fixture'){throw 'Unexpected service.'}
                    $s=[pscustomobject]@{Status=(Get-Content -LiteralPath $env:FIXTURE_STATUS -Raw)}
                    $s|Add-Member ScriptMethod WaitForStatus { param($state,$timeout) if((Get-Content -LiteralPath $env:FIXTURE_STATUS -Raw)-ne $state){throw 'Stop did not finish.'} }
                    $s
                }
                function Stop-Service { param($InputObject) 'stop'|Add-Content -LiteralPath $env:FIXTURE_ACTIONS; [IO.File]::WriteAllText($env:FIXTURE_STATUS,'Stopped') }
                function Start-Service { param($Name) if($Name-ne'Fixture'){throw 'Unexpected service.'}; 'start'|Add-Content -LiteralPath $env:FIXTURE_ACTIONS; [IO.File]::WriteAllText($env:FIXTURE_STATUS,'Running') }
                function New-Service {throw 'Rollback must not recreate the service or change its account.'}
                function Set-Service {throw 'Rollback must not change service identity or startup mode.'}
                """);
            await Run("BuildStateBackupCommand", backup);
            Assert.Equal(original, File.ReadAllText(backup + ".s"));
            File.WriteAllText(status, replacementStarted ? "Running" : "Stopped");
            await Run("BuildRollbackStopCommand", backup);
            Assert.Equal("Stopped", File.ReadAllText(status));
            // A failed ImagePath restoration must never start the replacement using stale registration.
            await Run("BuildStateRestoreCommand", backup, expectedExit: 1);
            Assert.True(File.Exists(backup + ".s"));
            Assert.Equal("Stopped", File.ReadAllText(status));
            File.Delete(backup); // The independently tested ImagePath restoration has now succeeded.
            await Run("BuildStateRestoreCommand", backup);
            Assert.Equal(original, File.ReadAllText(status));
            Assert.False(File.Exists(backup + ".s"));
            var calls = File.Exists(actions) ? File.ReadAllLines(actions) : Array.Empty<string>();
            Assert.Equal(replacementStarted ? 1 : 0, calls.Count(call => call == "stop"));
            Assert.Equal(original == "Running" ? 1 : 0, calls.Count(call => call == "start"));

            async Task Run(string name, string path, int expectedExit = 0)
            {
                var emitter = typeof(DotNetPublishPipelineRunner).Assembly.GetType("PowerForge.PowerForgeWixInstallerServiceScriptEmitter")!;
                var method = emitter.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
                var service = new PowerForgeInstallerServiceComponent { Id = "Fixture", ServiceName = "Fixture" };
                var parameters = name == "BuildRollbackStopCommand" ? new object[] { service } : new object[] { service, path };
                var command = (string)method.Invoke(null, parameters)!;
                const string prefix = "\"powershell.exe\" ";
                Assert.StartsWith(prefix, command);
                Assert.True(command.Length <= 255);
                var prelude = ". '" + harness.Replace("'", "''") + "';";
                var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    Arguments = command.Substring(prefix.Length).Replace("-c \"", "-c \"" + prelude) };
                start.Environment["FIXTURE_STATUS"] = status; start.Environment["FIXTURE_ACTIONS"] = actions;
                using var process = Process.Start(start)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
                Assert.True(process.ExitCode == expectedExit, await stdout + Environment.NewLine + await stderr);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InstallerStopsInTheTransactionAndRegistersReverseFileAndServiceRecoveryOrder()
    {
        var definition = new PowerForgeInstallerDefinition();
        definition.Product.Name = "Fixture"; definition.Product.Manufacturer = "Fixture";
        definition.Product.Version = "1.0.0"; definition.Product.UpgradeCode = Guid.NewGuid().ToString("B");
        definition.Product.MajorUpgradeSchedule = PowerForgeInstallerMajorUpgradeSchedule.AfterInstallExecute;
        definition.Components.Add(new PowerForgeInstallerServiceComponent {
            Id = "Fixture", DirectoryRefId = "INSTALLFOLDER", FileId = "FixtureExe", Source = "fixture.exe", ServiceName = "Fixture", DisplayName = "Fixture",
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall { Command = "install", UpgradeCommand = "upgrade", UninstallCommand = "uninstall",
                BackupExistingImagePath = true, StopServiceForUpgrade = true, SuppressServiceControl = true,
                BackupPath = "[WindowsFolder]Installer\\[ProductCode]-tb.pfb" }
        });
        var doc = XDocument.Parse(new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        XNamespace wix = "http://wixtoolset.org/schemas/v4/wxs";
        var rows = doc.Descendants(wix + "InstallExecuteSequence").Elements(wix + "Custom").ToDictionary(x => (string)x.Attribute("Action")!);
        Assert.Equal("Fixture.BackupImagePathState", (string?)rows["Fixture.SetBackupCommandState"].Attribute("Before"));
        Assert.Equal("Fixture.SetRollbackCommandState", (string?)rows["Fixture.BackupImagePathState"].Attribute("Before"));
        Assert.Equal("Fixture.SetRollbackCommand", (string?)rows["Fixture.RollbackImagePathState"].Attribute("Before"));
        Assert.Equal("Fixture.SetStopService", (string?)rows["Fixture.RollbackImagePath"].Attribute("Before"));
        Assert.Equal("InstallFiles", (string?)rows["Fixture.StopService"].Attribute("Before"));
        Assert.Equal("InstallFiles", (string?)rows["Fixture.SetRollbackCommandStop"].Attribute("After"));
        var stop = doc.Descendants(wix + "CustomAction").Single(x => (string?)x.Attribute("Id") == "Fixture.StopService");
        Assert.Equal("deferred", (string?)stop.Attribute("Execute"));
        Assert.Equal("no", (string?)stop.Attribute("Impersonate"));
        var setter = doc.Descendants(wix + "CustomAction").Single(x => (string?)x.Attribute("Id") == "Fixture.SetStopService");
        Assert.Equal("Fixture.StopService", (string?)setter.Attribute("Property"));
    }

    [Fact]
    public void LegacyStopWithoutBackupRemainsImmediateBeforeEarlyMajorUpgrade()
    {
        var service = new PowerForgeInstallerServiceComponent { Id = "Fixture", ServiceName = "Fixture",
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall { Command = "install", StopServiceForUpgrade = true } };
        var emitter = typeof(DotNetPublishPipelineRunner).Assembly.GetType("PowerForge.PowerForgeWixInstallerServiceScriptEmitter")!;
        var definition = new PowerForgeInstallerDefinition(); definition.Components.Add(service);
        var emit = emitter.GetMethod("EmitScriptInstallActions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var elements = ((IEnumerable<XElement>)emit.Invoke(null, new object[] { definition })!).ToArray();
        var stop = elements.Single(x => (string?)x.Attribute("Id") == "Fixture.StopService");
        Assert.Equal("immediate", (string?)stop.Attribute("Execute"));
        var setter = elements.Single(x => (string?)x.Attribute("Id") == "Fixture.SetStopService");
        Assert.Equal("WixQuietExecCmdLine", (string?)setter.Attribute("Property"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyOriginalStateRecoveryRequiresAnExistingRegistration(bool hasUpgradeCommand)
    {
        var service = new PowerForgeInstallerServiceComponent { Id = "Fixture", ServiceName = "Fixture",
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall { Command = "install", UpgradeCommand = hasUpgradeCommand ? "upgrade" : null,
                Condition = "NOT Installed", BackupExistingImagePath = true, StopServiceForUpgrade = true } };
        var definition = new PowerForgeInstallerDefinition(); definition.Components.Add(service);
        definition.Product.MajorUpgradeSchedule = PowerForgeInstallerMajorUpgradeSchedule.AfterInstallExecute;
        var emitter = typeof(DotNetPublishPipelineRunner).Assembly.GetType("PowerForge.PowerForgeWixInstallerServiceScriptEmitter")!;
        var emit = emitter.GetMethod("EmitScriptInstallActions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var elements = ((IEnumerable<XElement>)emit.Invoke(null, new object[] { definition })!).ToArray();
        XNamespace wix = "http://wixtoolset.org/schemas/v4/wxs";
        var search = elements.Single(x => x.Name == wix + "Property" && x.Element(wix + "RegistrySearch") != null);
        string registration = (string)search.Attribute("Id")!;
        var recovery = elements.Where(x => x.Name == wix + "InstallExecuteSequence").Elements(wix + "Custom")
            .Where(x => ((string)x.Attribute("Action")!).EndsWith("State", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, recovery.Length);
        Assert.All(recovery, row => Assert.EndsWith("AND (" + registration + ")", (string)row.Attribute("Condition")!));
        var rollbackStops = elements.Where(x => x.Name == wix + "InstallExecuteSequence").Elements(wix + "Custom")
            .Where(x => ((string)x.Attribute("Action")!).EndsWith("Stop", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, rollbackStops.Length);
        Assert.All(rollbackStops, row => Assert.Equal("(NOT Installed) AND (NOT REMOVE=\"ALL\")", (string)row.Attribute("Condition")!));
    }

    [Fact]
    public async Task MissingImagePathFailsBeforeRecordingOriginalAvailability()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backup = Path.Combine(root, "b");
            var harness = Path.Combine(root, "Harness.ps1");
            File.WriteAllText(backup, "stale command from a prior attempt");
            File.WriteAllText(harness, "function Get-WmiObject { [pscustomobject]@{Name='Fixture';PathName=$null} }");
            var emitter = typeof(DotNetPublishPipelineRunner).Assembly.GetType("PowerForge.PowerForgeWixInstallerServiceScriptEmitter")!;
            var method = emitter.GetMethod("BuildBackupCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
            var command = (string)method.Invoke(null, new object[] { new PowerForgeInstallerServiceComponent { ServiceName = "Fixture" }, backup })!;
            const string prefix = "\"powershell.exe\" ";
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                Arguments = command.Substring(prefix.Length).Replace("-c \"", "-c \". '" + harness.Replace("'", "''") + "';") };
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode != 0, await stdout + Environment.NewLine + await stderr);
            Assert.False(File.Exists(backup));
            Assert.False(File.Exists(backup + ".s"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
