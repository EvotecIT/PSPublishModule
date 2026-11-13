using PowerForge;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    // Pester's BeforeAll runs Import-Module -Force in a child scope. On that repeat import the inner binary module's
    // commands and aliases were missing from the generated module, so calling one autoloaded another installed copy.
    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedModule_ReimportedWithForceFromChildScope_KeepsBinaryAliases()
    {
        var host = FindExecutableOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        if (host is null) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-alias-reimport-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var module = BuildAliasFixtureModule(root);
            ModuleBootstrapperGenerator.Generate(module, "AliasModule",
                new ExportSet(Array.Empty<string>(), new[] { "Get-AliasFixture" }, new[] { "gaf" }),
                new[] { "AliasFixture.dll" }, handleRuntimes: false, useAssemblyLoadContext: true);
            File.WriteAllText(Path.Combine(module, "AliasModule.psd1"),
                "@{ RootModule = 'AliasModule.psm1'; ModuleVersion = '1.0.0'; GUID = '0b3a51f3-6d38-4c55-a5e5-6a2d6d1c2b77'; CmdletsToExport = @('Get-AliasFixture'); AliasesToExport = @('gaf'); FunctionsToExport = @() }");

            var script = Path.Combine(root, "Test-Reimport.ps1");
            File.WriteAllText(script, """
$ErrorActionPreference = 'Stop'
$manifest = Join-Path $PSScriptRoot 'AliasModule/AliasModule.psd1'
Import-Module $manifest
Remove-Module AliasModule
Import-Module $manifest
if ((gaf) -ne 'alias-fixture') { throw 'Commands are missing after Remove-Module and import.' }
$leaked = & (Get-Module AliasModule) { Get-Variable -Scope Script -Name Default, Core, Framework, Library, LibRoot, LibFolder, ImportModule, ModuleAssembly, InnerModule, BinaryModules -ErrorAction SilentlyContinue }
if ($leaked) { throw "Loader variables stayed in the module: $($leaked.Name -join ', ')" }
$context = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext((Get-Command Get-AliasFixture).ImplementingType.Assembly)
if ($context -eq [System.Runtime.Loader.AssemblyLoadContext]::Default) { throw 'The repeat import left the module context.' }
& {
    Import-Module $manifest -Force
    $module = Get-Module AliasModule
    if (-not $module.ExportedAliases.ContainsKey('gaf')) { throw "Alias lost after -Force re-import: $($module.ExportedAliases.Keys -join ',')" }
    if ((gaf) -ne 'alias-fixture') { throw 'Alias does not reach the re-imported module command.' }
    if ([System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext((Get-Command Get-AliasFixture).ImplementingType.Assembly) -eq [System.Runtime.Loader.AssemblyLoadContext]::Default) { throw 'The repeat import left the module context.' }
}
'ALIAS_REIMPORT_OK'
""");
            var result = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -File \"{script}\"", root, timeoutMilliseconds: 60000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("ALIAS_REIMPORT_OK", result.StandardOutput);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string BuildAliasFixtureModule(string root)
    {
        var project = Directory.CreateDirectory(Path.Combine(root, "Fixture")).FullName;
        File.WriteAllText(Path.Combine(project, "AliasFixture.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>AliasFixture</AssemblyName>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="PowerShellStandard.Library" Version="5.1.1" PrivateAssets="all" /></ItemGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(project, "Command.cs"), """
using System.Management.Automation;
namespace AliasFixture {
    [Cmdlet(VerbsCommon.Get, "AliasFixture")]
    [Alias("gaf")]
    public sealed class GetAliasFixtureCommand : PSCmdlet {
        protected override void ProcessRecord() { WriteObject("alias-fixture"); }
    }
}
""");
        var module = Path.Combine(root, "AliasModule");
        var built = RunProcess("dotnet", $"build \"{project}\" -c Release -o \"{Path.Combine(module, "Lib", "Core")}\" -nologo -v:q", root, timeoutMilliseconds: 300000);
        Assert.True(built.ExitCode == 0, built.StandardOutput + Environment.NewLine + built.StandardError);
        return module;
    }

    // A module that fails after registering accelerators never reaches OnRemove, so the bootstrapper must release them.
    // Here the failure is the registration's own warning under $WarningPreference = 'Stop'.
    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedModule_FailingImport_ReleasesItsTypeAccelerators()
    {
        var host = FindExecutableOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        if (host is null) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-failed-import-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var module = BuildAliasFixtureModule(root);
            ModuleBootstrapperGenerator.Generate(module, "AliasModule",
                new ExportSet(Array.Empty<string>(), new[] { "Get-AliasFixture" }, new[] { "gaf" }),
                new[] { "AliasFixture.dll" }, handleRuntimes: false, useAssemblyLoadContext: true,
                assemblyTypeAcceleratorMode: AssemblyTypeAcceleratorExportMode.AllowList,
                assemblyTypeAccelerators: new[] { "AliasFixture.GetAliasFixtureCommand", "AliasFixture.MissingType" });
            File.WriteAllText(Path.Combine(module, "AliasModule.psd1"),
                "@{ RootModule = 'AliasModule.psm1'; ModuleVersion = '1.0.0'; GUID = '0b3a51f3-6d38-4c55-a5e5-6a2d6d1c2b77'; CmdletsToExport = @('Get-AliasFixture'); AliasesToExport = @('gaf'); FunctionsToExport = @() }");
            var script = Path.Combine(root, "Test-FailedImport.ps1");
            File.WriteAllText(script, """
$catalog = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')::Get
$WarningPreference = 'Stop'
try { Import-Module (Join-Path $PSScriptRoot 'AliasModule/AliasModule.psd1') -ErrorAction Stop; throw 'The failing import succeeded.' } catch { if ("$_" -notlike '*AliasFixture.MissingType*') { throw } }
if ($catalog.ContainsKey('AliasFixture.GetAliasFixtureCommand')) { throw 'A failed import left its accelerator registered.' }
'FAILED_IMPORT_RELEASED_OK'
""");
            var result = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -File \"{script}\"", root, timeoutMilliseconds: 60000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("FAILED_IMPORT_RELEASED_OK", result.StandardOutput);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
