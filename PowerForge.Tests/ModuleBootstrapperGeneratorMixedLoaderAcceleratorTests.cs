using PowerForge;
using PowerForge.Tests;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [WindowsTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopAcceleratorsShareTheLegacyRegistrarAcrossLoaderVersions(bool newFirst, bool removeOwnerFirst)
    {
        var host = DesktopBindingHost();
        if (!File.Exists(host)) return;
        var repository = FindAcceleratorRepository();
        var configuration = typeof(ModuleBootstrapperGeneratorWindowsPowerShellTests).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
        var legacyLoader = Path.Combine(repository, "PowerForge.Tests", "Fixtures", "LegacyModuleLoader", "bin", configuration,
            "net472", "PowerForge.ModuleLoader.V1.dll");
        Assert.True(File.Exists(legacyLoader), "Missing legacy registrar fixture: " + legacyLoader);
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-mixed-loader-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            ModuleLoaderAssets.Write(Path.Combine(root, "Loader"));
            File.Copy(legacyLoader, Path.Combine(root, "PowerForge.ModuleLoader.V1.dll"));
            foreach (var name in new[] { "First", "Second" })
            {
                Directory.CreateDirectory(Path.Combine(root, name, "Lib"));
                var legacy = (name == "First") != newFirst;
                var import = legacy
                    ? "Add-Type -Path (Join-Path $PSScriptRoot '../PowerForge.ModuleLoader.V1.dll')"
                    : ModuleBootstrapperGenerator.BuildModuleLoaderImportBlock("$PSScriptRoot, '..', 'Loader'");
                File.WriteAllText(Path.Combine(root, name, name + ".psm1"),
                    "$PowerForgeModuleName = '" + name + "'\n" + import + "\n" +
                    "$LoadedDirectories = @((Join-Path $PSScriptRoot 'Lib'))\n" +
                    ModuleBootstrapperGenerator.BuildDesktopTypeAcceleratorBlock(AssemblyTypeAcceleratorExportMode.AllowList,
                        ["Fixture.Shared"], [], "(Join-Path $PSScriptRoot 'Lib')"));
            }
            var script = Path.Combine(root, "Test-MixedAccelerators.ps1");
            File.WriteAllText(script, """
param([bool] $RemoveOwnerFirst)
$ErrorActionPreference = 'Stop'
foreach ($name in 'First', 'Second') {
    Add-Type -TypeDefinition "namespace Fixture { public class Shared { public string Owner { get { return `"$name`"; } } } }" -OutputAssembly (Join-Path $PSScriptRoot "$name/Lib/$($name)Types.dll")
}
$null = Import-Module (Join-Path $PSScriptRoot 'First/First.psm1') -PassThru
$null = Import-Module (Join-Path $PSScriptRoot 'Second/Second.psm1') -PassThru -WarningAction SilentlyContinue
$first = & (Get-Module First) { $script:PowerForgeTypeAcceleratorRegistrations[0] }
$second = & (Get-Module Second) { $script:PowerForgeTypeAcceleratorRegistrations[0] }
if (-not [object]::ReferenceEquals($first.GetType(), $second.GetType())) { throw 'The modules selected separate registrar types.' }
$catalog = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')::Get
$remove = if ($RemoveOwnerFirst) { 'First' } else { 'Second' }
$remaining = if ($RemoveOwnerFirst) { 'Second' } else { 'First' }
Remove-Module $remove
if (-not $catalog.ContainsKey('Fixture.Shared') -or $catalog['Fixture.Shared'].Assembly.GetName().Name -ne "$($remaining)Types") { throw 'The remaining module lost its accelerator.' }
Remove-Module $remaining
if ($catalog.ContainsKey('Fixture.Shared')) { throw 'The accelerator outlived its last claim.' }
'MIXED_ACCELERATOR_HANDOVER_OK'
""");
            var result = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -Command \"& '{script}' -RemoveOwnerFirst:${removeOwnerFirst.ToString().ToLowerInvariant()}\"",
                root, timeoutMilliseconds: 60000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("MIXED_ACCELERATOR_HANDOVER_OK", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string FindAcceleratorRepository([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
        => Directory.GetParent(Path.GetDirectoryName(sourceFile)!)!.FullName;
}
