using PowerForge;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Category", "Integration")]
    public void GeneratedAccelerators_RemainUntilLastIdenticalTypeOwnerIsRemoved(bool desktop, bool collidingName)
    {
        if (desktop && !OperatingSystem.IsWindows()) return;
        var host = desktop
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            : FindExecutableOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        if (host is null) return;
        Assert.True(File.Exists(host));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-shared-accelerator-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var typeName = collidingName ? "SyncRoot" : "PowerForge.SharedAcceleratorFixture";
            var typeDeclaration = collidingName
                ? "public class SyncRoot {}"
                : "namespace PowerForge { public class SharedAcceleratorFixture {} }";
            ModuleLoaderAssets.Write(Path.Combine(root, ModuleLoaderAssets.PackagedFolderName));
            var block = ModuleBootstrapperGenerator.BuildModuleLoaderImportBlock("$PSScriptRoot, 'Loader'") + "\n" + (desktop
                ? ModuleBootstrapperGenerator.BuildDesktopTypeAcceleratorBlock(AssemblyTypeAcceleratorExportMode.AllowList,
                    [typeName], [], "$PSScriptRoot")
                : "$ModuleAssembly = [" + typeName + "].Assembly\n" +
                  ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(AssemblyTypeAcceleratorExportMode.AllowList,
                      [typeName], [], "$PSScriptRoot"));
            foreach (var name in new[] { "First", "Second" })
                File.WriteAllText(Path.Combine(root, name + ".psm1"), block);
            var script = Path.Combine(root, "Verify.ps1");
            File.WriteAllText(script, $$"""
                $ErrorActionPreference = 'Stop'
                Add-Type '{{typeDeclaration}}'
                $catalogType = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
                $get = $catalogType.GetProperty('Get', [Reflection.BindingFlags]'Static,Public,NonPublic')
                $name = '{{typeName}}'
                $type = [{{typeName}}]
                Import-Module (Join-Path $PSScriptRoot 'First.psm1')
                Import-Module (Join-Path $PSScriptRoot 'Second.psm1')
                Remove-Module First
                if (-not [object]::ReferenceEquals($get.GetValue($null)[$name], $type)) {
                    throw 'Removing the first owner removed a type still used by the second module.'
                }
                Remove-Module Second
                if ($get.GetValue($null).ContainsKey($name)) { throw 'The final owner left its accelerator registered.' }
                Import-Module (Join-Path $PSScriptRoot 'First.psm1') -WarningAction SilentlyContinue
                $catalogType.GetMethod('Remove').Invoke($null, @($name)) | Out-Null
                Import-Module (Join-Path $PSScriptRoot 'Second.psm1') -WarningAction SilentlyContinue
                Remove-Module Second
                if (-not [object]::ReferenceEquals($get.GetValue($null)[$name], $type)) {
                    throw 'Re-registration lost the first module ownership claim.'
                }
                Remove-Module First
                if ($get.GetValue($null).ContainsKey($name)) { throw 'The re-registered accelerator outlived its final owner.' }
                $catalogType.GetMethod('Add').Invoke($null, @($name, [Uri])) | Out-Null
                Import-Module (Join-Path $PSScriptRoot 'First.psm1') -WarningAction SilentlyContinue
                Import-Module (Join-Path $PSScriptRoot 'Second.psm1') -WarningAction SilentlyContinue
                Remove-Module First,Second
                if (-not [object]::ReferenceEquals($get.GetValue($null)[$name], [Uri])) {
                    throw 'Module ownership replaced or removed an external accelerator.'
                }
                $catalogType.GetMethod('Remove').Invoke($null, @($name)) | Out-Null
                'SHARED_ACCELERATORS_OK'
                """);
            var result = RunProcess(host!, $"-NoLogo -NoProfile -NonInteractive -File \"{script}\"", root, 20000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("SHARED_ACCELERATORS_OK", result.StandardOutput);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
