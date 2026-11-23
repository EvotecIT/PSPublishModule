using PowerForge;
using PowerForge.Tests;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [WindowsTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopAcceleratorsPreserveForwardersAndShareLegacyOwnership(
        bool newFirst, bool removeOwnerFirst, bool enumsOnly)
    {
        var host = DesktopBindingHost();
        Assert.True(File.Exists(host), "Missing Windows PowerShell: " + host);
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
                    : ModuleBootstrapperGenerator.BuildModuleLoaderImportBlock("$PSScriptRoot, '..', 'Loader'") +
                      "\nAdd-Type -Path (Join-Path $PSScriptRoot 'Lib/OtherTypes.dll')";
                var mode = legacy
                    ? AssemblyTypeAcceleratorExportMode.AllowList
                    : (enumsOnly ? AssemblyTypeAcceleratorExportMode.Enums : AssemblyTypeAcceleratorExportMode.Assembly);
                File.WriteAllText(Path.Combine(root, name, name + ".psm1"),
                    "$PowerForgeModuleName = '" + name + "'\n" + import + "\n" +
                    "$LoadedDirectories = @((Join-Path $PSScriptRoot 'Lib'))\n" +
                    ModuleBootstrapperGenerator.BuildDesktopTypeAcceleratorBlock(mode,
                        ["Fixture.Shared"], legacy ? [] : ["Facade"], "(Join-Path $PSScriptRoot 'Lib')"));
            }
            var script = Path.Combine(root, "Test-MixedAccelerators.ps1");
            File.WriteAllText(script, """
param([bool] $RemoveOwnerFirst, [string] $CurrentModule, [bool] $EnumsOnly)
$ErrorActionPreference = 'Stop'
foreach ($name in 'First', 'Second') {
    $source = "namespace Fixture { public class Shared { public string Owner { get { return `"$name`"; } } } }"
    Add-Type -TypeDefinition $source -OutputAssembly (Join-Path $PSScriptRoot "$name/Lib/$($name)Types.dll")
    if ($name -eq $CurrentModule) {
        $target = Join-Path $PSScriptRoot "$name/Lib/ForwardedTypes.dll"
        Add-Type -TypeDefinition 'namespace Fixture { public class Moved {} public enum Choice { First } }' -OutputAssembly $target
        Add-Type -TypeDefinition 'namespace Fixture { public class Choice {} public class Unrelated {} }' -OutputAssembly (Join-Path $PSScriptRoot "$name/Lib/OtherTypes.dll")
        Add-Type -TypeDefinition @'
using System.Reflection;
using System.Runtime.CompilerServices;
[assembly: AssemblyMetadata("PowerForge.TypeForwarderAnchor", "Fixture.Moved")]
[assembly: TypeForwardedTo(typeof(Fixture.Moved))]
[assembly: TypeForwardedTo(typeof(Fixture.Choice))]
'@ -ReferencedAssemblies $target -OutputAssembly (Join-Path $PSScriptRoot "$name/Lib/Facade.dll")
    }
}
$null = Import-Module (Join-Path $PSScriptRoot 'First/First.psm1') -PassThru
$null = Import-Module (Join-Path $PSScriptRoot 'Second/Second.psm1') -PassThru -WarningAction SilentlyContinue
$first = & (Get-Module First) { $script:PowerForgeTypeAcceleratorRegistrations[0] }
$second = & (Get-Module Second) { $script:PowerForgeTypeAcceleratorRegistrations[0] }
if (-not [object]::ReferenceEquals($first.GetType(), $second.GetType())) { throw 'The modules selected separate registrar types.' }
$catalog = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')::Get
if (-not $catalog['Fixture.Choice'].IsEnum -or $catalog['Fixture.Choice'].Assembly.GetName().Name -ne 'ForwardedTypes') {
    throw 'The forwarded enum was missing or resolved to the wrong assembly.'
}
if ($EnumsOnly) {
    if ($catalog.ContainsKey('Fixture.Moved')) { throw 'Enum mode registered a forwarded class.' }
} elseif ($catalog['Fixture.Moved'].Assembly.GetName().Name -ne 'ForwardedTypes') {
    throw 'The forwarded class was missing or resolved to the wrong assembly.'
}
if ($catalog.ContainsKey('Fixture.Unrelated')) { throw 'The facade exposed an unrelated type.' }
$remove = if ($RemoveOwnerFirst) { 'First' } else { 'Second' }
$remaining = if ($RemoveOwnerFirst) { 'Second' } else { 'First' }
Remove-Module $remove
if (-not $catalog.ContainsKey('Fixture.Shared') -or $catalog['Fixture.Shared'].Assembly.GetName().Name -ne "$($remaining)Types") { throw 'The remaining module lost its accelerator.' }
Remove-Module $remaining
if ($catalog.ContainsKey('Fixture.Shared') -or $catalog.ContainsKey('Fixture.Moved') -or $catalog.ContainsKey('Fixture.Choice')) {
    throw 'An accelerator outlived its last claim.'
}
'MIXED_ACCELERATOR_HANDOVER_OK'
""");
            var currentModule = newFirst ? "First" : "Second";
            var result = RunProcess(host,
                $"-NoLogo -NoProfile -NonInteractive -Command \"& '{script}' -RemoveOwnerFirst:${removeOwnerFirst.ToString().ToLowerInvariant()} -CurrentModule {currentModule} -EnumsOnly:${enumsOnly.ToString().ToLowerInvariant()}\"",
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
