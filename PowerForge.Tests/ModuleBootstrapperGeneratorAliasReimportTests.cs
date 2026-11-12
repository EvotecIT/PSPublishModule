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
}
