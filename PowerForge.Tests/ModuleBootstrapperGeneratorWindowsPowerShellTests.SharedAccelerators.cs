using PowerForge;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public void GeneratedAccelerators_RemainUntilLastIdenticalTypeOwnerIsRemoved(bool desktop)
    {
        if (!OperatingSystem.IsWindows()) return;
        var host = desktop
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            : FindExecutableOnPath("pwsh.exe");
        Assert.NotNull(host);
        Assert.True(File.Exists(host));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-shared-accelerator-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var block = desktop
                ? ModuleBootstrapperGenerator.BuildDesktopTypeAcceleratorBlock(AssemblyTypeAcceleratorExportMode.AllowList,
                    ["PowerForge.SharedAcceleratorFixture"], [], "$PSScriptRoot")
                : "$ModuleAssembly = [PowerForge.SharedAcceleratorFixture].Assembly\n$LibFolder = $PSScriptRoot\n" +
                  ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(AssemblyTypeAcceleratorExportMode.AllowList,
                      ["PowerForge.SharedAcceleratorFixture"], []);
            foreach (var name in new[] { "First", "Second" })
                File.WriteAllText(Path.Combine(root, name + ".psm1"), block);
            var script = Path.Combine(root, "Verify.ps1");
            File.WriteAllText(script, """
                $ErrorActionPreference = 'Stop'
                Add-Type 'namespace PowerForge { public class SharedAcceleratorFixture {} }'
                $catalogType = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
                $get = $catalogType.GetProperty('Get', [Reflection.BindingFlags]'Static,Public,NonPublic')
                $name = 'PowerForge.SharedAcceleratorFixture'
                $type = [PowerForge.SharedAcceleratorFixture]
                Import-Module (Join-Path $PSScriptRoot 'First.psm1')
                Import-Module (Join-Path $PSScriptRoot 'Second.psm1')
                Remove-Module First
                if (-not [object]::ReferenceEquals($get.GetValue($null)[$name], $type)) {
                    throw 'Removing the first owner removed a type still used by the second module.'
                }
                Remove-Module Second
                if ($get.GetValue($null).ContainsKey($name)) { throw 'The final owner left its accelerator registered.' }
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
