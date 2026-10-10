using PowerForge;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopModuleResolvesDeferredJsonAndUnregistersOnRemoval(bool useAssemblyLoadContext, bool useDevelopmentSelection)
    {
        if (!OperatingSystem.IsWindows()) return;
        var host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(host)) return;
        var root = Path.Combine(Path.GetTempPath(), "pf-desktop-deferred-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fixture = Directory.CreateDirectory(Path.Combine(root, "Fixture")).FullName;
            File.WriteAllText(Path.Combine(fixture, "DeferredJson.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>DemoModule</AssemblyName>
    <LangVersion>latest</LangVersion>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <JsonVersion Condition="'$(JsonVersion)' == ''">10.0.0</JsonVersion>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="System.Text.Json" Version="$(JsonVersion)" /></ItemGroup>
</Project>
""");
            File.WriteAllText(Path.Combine(fixture, "Initialize.cs"), """
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
namespace DemoModule {
    public static class Initialize {
        public static string ReadJson() {
            return JsonSerializer.Serialize(new Dictionary<string, string> { ["value"] = "deferred-json" },
                JsonContext.Default.DictionaryStringString);
        }
        public static string ReadEncoded() { return JavaScriptEncoder.Default.Encode("<deferred>"); }
        public static string ReadJsonOnWorker() {
            return Task.Run(() => ReadJson()).GetAwaiter().GetResult();
        }
        public static void ResolveUnrelatedOnWorkers() {
            Parallel.For(0, 8, i => {
                try {
                    Assembly.Load("PowerForgeUnrelatedMissingAssembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
                    throw new System.Exception("Unexpected assembly resolution.");
                } catch (FileNotFoundException) {}
            });
        }
    }
    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class JsonContext : JsonSerializerContext {}
}
""");
            var build = RunProcess("dotnet", "build DeferredJson.csproj -c Release -nologo --verbosity quiet",
                fixture, timeoutMilliseconds: 60000);
            Assert.True(build.ExitCode == 0, build.StandardOutput + Environment.NewLine + build.StandardError);
            var lib = Directory.CreateDirectory(Path.Combine(root, "Lib", "Default")).FullName;
            File.Copy(Path.Combine(fixture, "bin", "Release", "netstandard2.0", "DemoModule.dll"),
                Path.Combine(lib, "DemoModule.dll"));
            // A dependency patch can be selected by the consumer without rebuilding
            // this library against the new assembly revision.
            var runtimeBuild = RunProcess("dotnet", "build DeferredJson.csproj -c Release -nologo --verbosity quiet -p:JsonVersion=10.0.9",
                fixture, timeoutMilliseconds: 60000);
            Assert.True(runtimeBuild.ExitCode == 0, runtimeBuild.StandardOutput + Environment.NewLine + runtimeBuild.StandardError);
            foreach (var file in Directory.GetFiles(Path.Combine(fixture, "bin", "Release", "netstandard2.0"), "*.dll"))
                if (!string.Equals(Path.GetFileName(file), "DemoModule.dll", StringComparison.OrdinalIgnoreCase))
                    File.Copy(file, Path.Combine(lib, Path.GetFileName(file)));
            var publicScripts = Directory.CreateDirectory(Path.Combine(root, "Public")).FullName;
            File.WriteAllText(Path.Combine(publicScripts, "Payload.ps1"), "# Valid script payload.");
            ModuleBootstrapperGenerator.Generate(root, "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" }, handleRuntimes: false, useAssemblyLoadContext: useAssemblyLoadContext,
                developmentBinaries: useDevelopmentSelection ? new ModuleDevelopmentBinaryBootstrapperOptions(
                    ModuleDevelopmentBinaryMode.Environment, Path.Combine(root, "MissingDevelopmentBinaries"),
                    "PF_DEFERRED_JSON_DEV", "PF_DEFERRED_JSON_CONFIGURATION", new[] { "net8.0" }, new[] { "net472" }) : null);
            var script = Path.Combine(root, "Validate-DeferredJson.ps1");
            File.WriteAllText(script, """
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1')
[DemoModule.Initialize]::ResolveUnrelatedOnWorkers()
if (([DemoModule.Initialize]::ReadJsonOnWorker() | ConvertFrom-Json).value -ne 'deferred-json') { throw 'Deferred worker JSON failed.' }
if (([DemoModule.Initialize]::ReadJson() | ConvertFrom-Json).value -ne 'deferred-json') { throw 'Deferred JSON failed.' }
if ([DemoModule.Initialize]::ReadEncoded() -ne '\u003Cdeferred\u003E') { throw 'Direct encoder reference failed.' }
$module = Get-Module DemoModule
$state = & $module { $PowerForgeDesktopAssemblyResolverState }
if (-not $state.Registered -or $state.BootstrapActive) { throw 'The resolver is not in scoped runtime mode.' }
$resolver = & $module { $PowerForgeDesktopAssemblyResolver }
$name = 'System.Text.Encodings.Web, Version=10.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'
if ($null -eq $resolver.Invoke($null, [ResolveEventArgs]::new($name))) { throw 'Declared runtime reference was not served.' }
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new('DemoModule, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'))) { throw 'Undeclared runtime request was served.' }
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($name.Replace('cc7b13ffcd2ddd51', '0000000000000000')))) { throw 'Different reference identity was served.' }
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($name, [System.Management.Automation.PSCmdlet].Assembly))) { throw 'Unrelated runtime request was served.' }
try { Get-Item (Join-Path $PSScriptRoot 'missing-file') -ErrorAction Stop } catch [System.Management.Automation.ItemNotFoundException] {}
Remove-Module DemoModule
if ($state.Registered) { throw 'The resolver remained registered after removal.' }
Set-Content (Join-Path $PSScriptRoot 'Public/Payload.ps1') '$global:FailedResolverForProof = $PowerForgeDesktopAssemblyResolverState; throw "late-script-failure"'
try {
    Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force -ErrorAction Stop
    throw 'Late script failure was ignored.'
} catch {
    if ($_.Exception.Message -notlike '*late-script-failure*') { throw }
}
if ($null -eq $global:FailedResolverForProof -or $global:FailedResolverForProof.Registered) { throw 'Failed import retained its resolver.' }
Remove-Variable FailedResolverForProof -Scope Global
'DEFERRED_JSON_OK'
""");
            var result = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -File \"{script}\"", root, 30000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("DEFERRED_JSON_OK", result.StandardOutput);
            File.WriteAllText(script, """
try {
    Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force -ErrorAction Continue
} finally {
    if ($null -eq $global:FailedResolverForProof -or $global:FailedResolverForProof.Registered) {
        throw 'Failed Continue import retained its resolver.'
    }
    'CONTINUE_CLEANUP_OK'
}
""");
            var continued = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -File \"{script}\"", root, 30000);
            Assert.Contains("CONTINUE_CLEANUP_OK", continued.StandardOutput);
            Assert.DoesNotContain("retained its resolver", continued.StandardError);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
