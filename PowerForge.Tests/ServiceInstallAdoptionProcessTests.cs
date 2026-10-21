using System.Diagnostics;
using System.Reflection;

namespace PowerForge.Tests;

public sealed class ServiceInstallAdoptionProcessTests
{
    [WindowsFact]
    public Task RollbackRestoresQuotedImagePathThroughNativePowerShell()
        => VerifyNativeRollback(missing: false);

    [WindowsFact]
    public Task RollbackKeepsBackupWhenNativeServiceRegistrationIsMissing()
        => VerifyNativeRollback(missing: true);

    private static async Task VerifyNativeRollback(bool missing)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string imagePath = "\"C:\\Program Files\\Fixture\\service.exe\" --config \"D:\\Custom Config\\settings.json\" --root \"D:\\Directory Path\\\\\" --suffix D:\\plain\\";
            var emitter = typeof(DotNetPublishPipelineRunner).Assembly.GetType("PowerForge.PowerForgeWixInstallerServiceScriptEmitter")!;
            var method = emitter.GetMethod("BuildRollbackCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
            var service = new PowerForgeInstallerServiceComponent { Id = "Fixture", ServiceName = "Fixture" };
            const string backupName = "backup file.txt";
            var command = (string)method.Invoke(null, new object[] { service, backupName })!;
            var backupCommand = (string)emitter.GetMethod("BuildBackupCommand", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { service, backupName })!;
            var cleanupCommand = (string)emitter.GetMethod("BuildCleanupCommand", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { service, backupName })!;
            const string prefix = "\"powershell.exe\" ";
            Assert.StartsWith(prefix, command);
            var backup = Path.Combine(root.FullName, backupName);
            var setup = Path.Combine(root.FullName, "Setup.ps1");
            File.WriteAllText(setup, """
                $ErrorActionPreference = 'Stop'
                Add-Type -OutputType ConsoleApplication -OutputAssembly (Join-Path $PSScriptRoot 'sc.exe') -TypeDefinition @'
                public static class NativeServiceBoundary {
                    public static int Main(string[] args) {
                        System.IO.File.WriteAllLines(System.Environment.GetEnvironmentVariable("POWERFORGE_TEST_SERVICE_ARGUMENTS"), args);
                        return System.Environment.GetEnvironmentVariable("POWERFORGE_TEST_SERVICE_MISSING") == "1" ? 1060 : 0;
                    }
                }
                '@
                """);
            var compile = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", setup }) compile.ArgumentList.Add(argument);
            await RequireProcessExit(compile, 0);

            var argumentsPath = Path.Combine(root.FullName, "arguments.txt");
            var expectedExe = Path.Combine(root.FullName, "sc.exe");
            var guard = "if((gcm sc.exe).Source-ne'" + expectedExe.Replace("'", "''") + "'){exit 9};";
            var start = new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = root.FullName,
                Arguments = command.Substring(prefix.Length).Replace("-c \"", "-c \"" + guard),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Environment["PATH"] = root.FullName + Path.PathSeparator + start.Environment["PATH"];
            start.Environment["POWERFORGE_TEST_SERVICE_ARGUMENTS"] = argumentsPath;
            start.Environment["POWERFORGE_TEST_SERVICE_MISSING"] = missing ? "1" : "0";
            start.Environment["POWERFORGE_TEST_IMAGE_PATH"] = imagePath;
            // Prevent accidental use of the real service-control executable if fixture resolution changes.
            Assert.True(File.Exists(expectedExe));
            var backupPrelude = "function Get-WmiObject{[pscustomobject]@{Name='Fixture';PathName=$env:POWERFORGE_TEST_IMAGE_PATH}};";
            start.Arguments = backupCommand.Substring(prefix.Length).Replace("-c \"", "-c \"" + guard + backupPrelude);
            await RequireProcessExit(start, 0);
            Assert.Equal(imagePath, File.ReadAllText(backup));
            if (!missing)
            {
                start.Arguments = cleanupCommand.Substring(prefix.Length).Replace("-c \"", "-c \"" + guard);
                await RequireProcessExit(start, 0);
                Assert.False(File.Exists(backup));
                start.Arguments = backupCommand.Substring(prefix.Length).Replace("-c \"", "-c \"" + guard + backupPrelude);
                await RequireProcessExit(start, 0);
            }
            start.Arguments = command.Substring(prefix.Length).Replace("-c \"", "-c \"" + guard);
            await RequireProcessExit(start, missing ? 1 : 0);
            Assert.Equal(new[] { "config", "Fixture", "binPath=", imagePath }, File.ReadAllLines(argumentsPath));
            Assert.Equal(missing, File.Exists(backup));
            if (missing) Assert.Equal(imagePath, File.ReadAllText(backup));
        }
        finally { root.Delete(recursive: true); }
    }

    private static async Task RequireProcessExit(ProcessStartInfo start, int expectedExitCode)
    {
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == expectedExitCode, await output + Environment.NewLine + await error);
    }

    [Theory]
    [InlineData("pwsh", "preserve")]
    [InlineData("powershell.exe", "preserve")]
    [InlineData("pwsh", "new-gmsa")]
    [InlineData("powershell.exe", "new-gmsa")]
    [InlineData("pwsh", "change-gmsa")]
    [InlineData("powershell.exe", "change-gmsa")]
    [InlineData("pwsh", "disabled")]
    [InlineData("powershell.exe", "disabled")]
    [InlineData("pwsh", "preserve-equals")]
    [InlineData("powershell.exe", "preserve-equals")]
    [InlineData("pwsh", "preserve-binpath")]
    [InlineData("powershell.exe", "preserve-binpath")]
    [InlineData("pwsh", "preserve-binpath-bare")]
    [InlineData("powershell.exe", "preserve-binpath-bare")]
    [InlineData("pwsh", "missing-binpath")]
    [InlineData("powershell.exe", "missing-binpath")]
    [InlineData("pwsh", "new-gmsa-literal")]
    [InlineData("powershell.exe", "new-gmsa-literal")]
    [InlineData("pwsh", "preserve-quoted-option")]
    [InlineData("powershell.exe", "preserve-quoted-option")]
    [InlineData("pwsh", "preserve-config-decoy")]
    [InlineData("powershell.exe", "preserve-config-decoy")]
    [InlineData("pwsh", "preserve-whole-equals")]
    [InlineData("powershell.exe", "preserve-whole-equals")]
    [InlineData("pwsh", "preserve-whole-space")]
    [InlineData("powershell.exe", "preserve-whole-space")]
    public async Task RegistrationPreservesExistingIdentityOrUsesPasswordlessManagedAccount(string executable, string mode)
    {
        if (executable == "powershell.exe" && !OperatingSystem.IsWindows()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var renderer = typeof(DotNetPublishPipelineRunner).GetMethod("BuildInstallServiceScript", BindingFlags.Static | BindingFlags.NonPublic)!;
            var script = (string)renderer.Invoke(null, new object?[] { "AdoptionFixture", "Fixture", "Fixture", "fixture.exe", "--default value --config=default.json", null })!;
            File.WriteAllText(Path.Combine(root.FullName, "Install.ps1"), script);
            File.WriteAllText(Path.Combine(root.FullName, "fixture.exe"), "inert payload; process never executed");
            File.WriteAllText(Path.Combine(root.FullName, "Verify.ps1"), """
                $ErrorActionPreference = 'Stop'
                $mode = $args[0]
                $global:changes = $null
                $previousCommand='"C:\Legacy Files\Old.exe" --config="D:\Private Config\active.json" --custom retained'
                if($mode -eq 'preserve-equals'){$previousCommand+=' --service-name=Previous'}
                if($mode -eq 'preserve-quoted-option'){$previousCommand='"C:\Legacy Files\Old.exe" --config="D:\Configs\active --service-name=Previous.json" --label "escaped \" --service-name=Decoy" --root "D:\Directory Path\\" --custom retained'}
                if($mode -eq 'preserve-config-decoy'){$previousCommand='"C:\Legacy Files\Old.exe" --label "text --config=fake.json" --custom retained'}
                if($mode -eq 'preserve-whole-equals'){$previousCommand='"C:\Legacy Files\Old.exe" "--config=D:\Private Config\active.json" "--service-name=Previous" --custom retained'}
                if($mode -eq 'preserve-whole-space'){$previousCommand='"C:\Legacy Files\Old.exe" "--config" "D:\Private Config\active.json" "--service-name" "Previous" --custom retained'}
                function Get-Service { param($Name,$ErrorAction) if($mode -notlike 'new-gmsa*' -and $mode -ne 'missing-binpath'){ [pscustomobject]@{Name=$Name;Status='Stopped'} } }
                function Get-CimInstance { param($ClassName,$Filter) [pscustomobject]@{PathName=$previousCommand;StartName='DOMAIN\ServiceAccount';StartMode=$(if($mode -eq 'disabled'){'Disabled'}else{'Manual'})} }
                function Get-Credential { throw 'Existing credentials must not be requested.' }
                function Stop-Service { throw 'Stopped service must not be stopped again.' }
                function Start-Service { throw 'Disabled service must not be started during an update.' }
                function New-Service { throw 'Existing service must not be recreated.' }
                function Invoke-CimMethod {
                    param($InputObject,$ClassName,$MethodName,$Arguments)
                    if($MethodName -eq 'Create' -and ($Arguments.ServiceType -isnot [byte] -or $Arguments.ErrorControl -isnot [byte])){throw 'Win32_Service.Create requires UInt8 service type and error control.'}
                    $global:changes=$Arguments; [pscustomobject]@{ReturnValue=0}
                }
                function Set-Service { param($Name,$DisplayName,$Description,$StartupType) if($PSBoundParameters.ContainsKey('StartupType')){throw 'Existing startup mode must be preserved.'} }
                $install = @{}
                if($mode -notlike 'new-gmsa*'){$install.UpgradeMode=$true;$install.PreserveExistingServiceArguments=$true}
                if($mode -like '*binpath*'){
                    $install.Remove('PreserveExistingServiceArguments')
                    $install.PreserveExistingServiceBinPath=$true
                    $install.BackupPath=Join-Path $PSScriptRoot 'backup.txt'
                    $preserved='"C:\Protected Backup\Original.exe"'
                    if($mode -ne 'preserve-binpath-bare'){$preserved+=' --config="D:\Original Config\active.json" --service-name=Original --custom retained'}
                    Set-Content -LiteralPath $install.BackupPath -Value $preserved
                }
                if($mode -like 'new-gmsa*' -or $mode -eq 'change-gmsa'){$install.Account='DOMAIN\ManagedService$'}
                if($mode -eq 'disabled'){$install.Start=$true}
                if($mode -eq 'missing-binpath'){
                    try{& (Join-Path $PSScriptRoot 'Install.ps1') @install;throw 'Missing registration was accepted.'}
                    catch{if($_.Exception.Message -notlike '*registration is missing*'){throw}}
                    if($null -ne $global:changes){throw 'A missing service must not be recreated with a fallback identity.'}
                    'ADOPTION_OK';return
                }
                $configPath=if($mode -eq 'new-gmsa-literal'){Join-Path $PSScriptRoot '$1\$&\config.json'}else{Join-Path $PSScriptRoot 'New Default\config.json'}
                & (Join-Path $PSScriptRoot 'Install.ps1') @install -ConfigPath $configPath | Out-Null
                if($null -eq $global:changes){throw 'No in-place update was performed.'}
                if($mode -like 'new-gmsa*' -or $mode -eq 'change-gmsa'){
                    if($global:changes.StartName -ne 'DOMAIN\ManagedService$' -or $global:changes.StartPassword -ne ''){throw 'Managed account was not registered passwordlessly.'}
                    if($mode -eq 'new-gmsa-literal'){
                        $expected='"'+(Join-Path $PSScriptRoot 'fixture.exe')+'" --default value --config "'+$configPath+'" --service-name "AdoptionFixture"'
                        if($global:changes.PathName -cne $expected){throw 'Literal config path characters were interpreted as regex substitutions.'}
                    }
                    'ADOPTION_OK'; return
                }
                if($global:changes.ContainsKey('StartName') -or $global:changes.ContainsKey('StartPassword') -or $global:changes.ContainsKey('StartMode')){throw 'Existing identity or startup was replaced.'}
                if($mode -like 'preserve-binpath*'){
                    if($global:changes.PathName -cne $preserved){throw 'The complete protected command line was modified.'}
                    'ADOPTION_OK';return
                }
                $expected='"'+(Join-Path $PSScriptRoot 'fixture.exe')+'" --config="D:\Private Config\active.json" --custom retained --service-name "AdoptionFixture"'
                if($mode -eq 'preserve-quoted-option'){$expected='"'+(Join-Path $PSScriptRoot 'fixture.exe')+'" --config="D:\Configs\active --service-name=Previous.json" --label "escaped \" --service-name=Decoy" --root "D:\Directory Path\\" --custom retained --service-name "AdoptionFixture"'}
                if($mode -eq 'preserve-config-decoy'){$expected='"'+(Join-Path $PSScriptRoot 'fixture.exe')+'" --label "text --config=fake.json" --custom retained --config "'+$configPath+'" --service-name "AdoptionFixture"'}
                if($mode -eq 'preserve-whole-equals'){$expected='"'+(Join-Path $PSScriptRoot 'fixture.exe')+'" "--config=D:\Private Config\active.json" --service-name "AdoptionFixture" --custom retained'}
                if($mode -eq 'preserve-whole-space'){$expected='"'+(Join-Path $PSScriptRoot 'fixture.exe')+'" "--config" "D:\Private Config\active.json" --service-name "AdoptionFixture" --custom retained'}
                if($global:changes.PathName -ne $expected){throw 'The new executable did not retain the active config and arguments.'}
                'ADOPTION_OK'
                """);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root.FullName, "Verify.ps1"), mode }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await output + Environment.NewLine + await error);
            Assert.Contains("ADOPTION_OK", await output);
        }
        finally { root.Delete(recursive: true); }
    }
}
