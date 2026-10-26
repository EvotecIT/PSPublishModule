using PowerForge;
using PowerForge.Tests;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [WindowsFact]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopBindings_ValidPolicy_ResolvesDeclaredReferencesAndUnregisters()
    {
        using var fixture = CreateDesktopBindingFixture();
        string script = """
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force
$module = Get-Module DemoModule
$state = & $module { $PowerForgeDesktopAssemblyResolverState }
$resolver = & $module { $PowerForgeDesktopAssemblyResolver }
if (-not $state.Registered -or $state.BootstrapActive) { throw 'Resolver did not enter scoped runtime mode.' }
if ([DemoModule.Initialize]::Count([byte[]](1,2,3)) -ne 3) { throw 'Deferred Memory execution failed.' }
$owner = [DemoModule.Initialize].Assembly
$name = 'System.Memory, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'
$resolved = $resolver.Invoke($null, [ResolveEventArgs]::new($name, $owner))
if ($null -eq $resolved -or $resolved.GetName().Version -ne [Version]'4.0.2.0') { throw 'Declared module request was not redirected.' }
if ($null -eq $resolver.Invoke($null, [ResolveEventArgs]::new($name))) { throw 'Declared null-caller request was not served.' }
$undeclared = 'System.Buffers, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($undeclared))) { throw 'Undeclared null-caller request escaped module scope.' }
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($name, [System.Management.Automation.PSCmdlet].Assembly))) { throw 'Unrelated assembly was served.' }
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($name.Replace('cc7b13ffcd2ddd51','0000000000000000'), $owner))) { throw 'Foreign token was accepted.' }
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($name.Replace('4.0.5.0','4.0.6.0'), $owner))) { throw 'Unqualified version was accepted.' }
Remove-Module DemoModule
if ($state.Registered) { throw 'Resolver remained registered after removal.' }
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($name, $owner))) { throw 'Removed resolver still served requests.' }
Set-Content (Join-Path $PSScriptRoot 'Public/Payload.ps1') '$global:FailedFacadeState = $PowerForgeDesktopAssemblyResolverState; throw "facade-late-failure"'
try {
    Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force -ErrorAction Stop
    throw 'Late failure was ignored.'
} catch {
    if ($_.Exception.Message -notlike '*facade-late-failure*') { throw }
}
if ($null -eq $global:FailedFacadeState -or $global:FailedFacadeState.Registered) { throw 'Failed import retained its resolver.' }
Remove-Variable FailedFacadeState -Scope Global
'DESKTOP_BINDING_SCOPE_AND_LIFETIME_OK'
""";

        AssertDesktopBindingProcess(fixture, script, "DESKTOP_BINDING_SCOPE_AND_LIFETIME_OK");
    }

    [WindowsTheory]
    [InlineData("policy-hash")]
    [InlineData("foreign-token")]
    [InlineData("unqualified-version")]
    [InlineData("tampered-asset")]
    [InlineData("field-count")]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopBindings_InvalidPolicyOrAsset_RejectsImport(string fault)
    {
        using var fixture = CreateDesktopBindingFixture();
        string[] lines = File.ReadAllLines(fixture.PolicyPath);
        int memoryIndex = Array.FindIndex(lines, line => line.StartsWith("System.Memory|", StringComparison.Ordinal));
        Assert.True(memoryIndex >= 0);
        string[] fields = lines[memoryIndex].Split('|');
        switch (fault)
        {
            case "policy-hash": fields[4] = new string('0', 64); break;
            case "foreign-token": fields[3] = "0000000000000000"; break;
            case "unqualified-version": fields[1] = "4.0.4.0"; break;
            case "tampered-asset": File.AppendAllText(fixture.PayloadAsset, "changed-asset"); break;
            case "field-count": fields = fields.Take(4).ToArray(); break;
        }
        lines[memoryIndex] = string.Join('|', fields);
        File.WriteAllLines(fixture.PolicyPath, lines);

        AssertDesktopBindingProcess(fixture, """
$ErrorActionPreference = 'Stop'
$rejected = $false
try { Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force -ErrorAction Stop }
catch {
    if ($_.Exception.ToString() -notlike '*Desktop facade binding*') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'Invalid facade policy or asset was accepted.' }
if (Get-Module DemoModule) { throw 'Rejected module remained imported.' }
'DESKTOP_BINDING_REJECTED'
""", "DESKTOP_BINDING_REJECTED");
    }

    [WindowsFact]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopBindings_WithoutPolicy_DoesNotDowngradeSelectedFacade()
    {
        using var fixture = CreateDesktopBindingFixture();
        File.Delete(fixture.PolicyPath);

        AssertDesktopBindingProcess(fixture, """
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force
$module = Get-Module DemoModule
$resolver = & $module { $PowerForgeDesktopAssemblyResolver }
$owner = [DemoModule.Initialize].Assembly
$name = 'System.Memory, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'
if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($name, $owner))) { throw 'Missing policy allowed a downgrade.' }
$rejected = $false
try { [DemoModule.Initialize]::Count([byte[]](1,2,3)) | Out-Null }
catch {
    if ($_.Exception.ToString() -notlike '*System.Memory*4.0.5.0*') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'Deferred higher-version execution unexpectedly succeeded without policy.' }
Remove-Module DemoModule
'DESKTOP_BINDING_POLICY_REQUIRED'
""", "DESKTOP_BINDING_POLICY_REQUIRED");
    }

    private static DesktopAssetFixture CreateDesktopBindingFixture()
    {
        var fixture = new DesktopAssetFixture();
        try
        {
            // A second valid policy entry is deliberately not referenced by DemoModule.
            // This distinguishes declared null-callers from indiscriminate policy lookup.
            using (var buffers = new DesktopAssetFixture("System.Buffers", "4.6.1"))
            {
                buffers.CopyPackageAssets(Path.Combine(fixture.PackageRoot, "system.buffers", "4.6.1"));
                File.Copy(buffers.OriginalAsset, Path.Combine(fixture.Payload, "System.Buffers.dll"));
                fixture.Target[buffers.LibraryKey] = buffers.Target[buffers.LibraryKey]!.DeepClone();
                fixture.Document["libraries"]![buffers.LibraryKey] = buffers.Library.DeepClone();
                fixture.Mapping.Add("System.Buffers", "netstandard2.0");
                fixture.WriteAssets();
            }
            CompileFacadeReferencingDesktopAssembly(fixture);
            fixture.Normalize();
            foreach (var dependency in new[]
            {
                ("System.Runtime.CompilerServices.Unsafe", "6.1.2"),
                ("System.Numerics.Vectors", "4.6.1")
            })
            {
                string package = DesktopAssetFixture.FindCachedPackage(dependency.Item1, dependency.Item2);
                File.Copy(Path.Combine(package, "lib", "netstandard2.0", dependency.Item1 + ".dll"),
                    Path.Combine(fixture.Payload, dependency.Item1 + ".dll"));
            }
            Directory.CreateDirectory(Path.Combine(fixture.Root, "Public"));
            File.WriteAllText(Path.Combine(fixture.Root, "Public", "Payload.ps1"), "# Valid fixture payload.");
            ModuleBootstrapperGenerator.Generate(fixture.Root, "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" }, handleRuntimes: false);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    private static void CompileFacadeReferencingDesktopAssembly(DesktopAssetFixture fixture)
    {
        string script = Path.Combine(fixture.Root, "Compile-FacadeReference.ps1");
        File.WriteAllText(script, """
param([string] $Reference,[string] $Output)
$ErrorActionPreference = 'Stop'
$source = @'
namespace DemoModule {
    public static class Initialize {
        public static int Count(byte[] value) { return new System.ReadOnlyMemory<byte>(value).Length; }
    }
}
'@
Add-Type -TypeDefinition $source -ReferencedAssemblies $Reference -OutputAssembly $Output -ErrorAction Stop
""");
        var result = RunProcess(DesktopBindingHost(),
            $"-NoLogo -NoProfile -NonInteractive -File \"{script}\" -Reference \"{fixture.OriginalAsset}\" -Output \"{Path.Combine(fixture.Payload, "DemoModule.dll")}\"",
            fixture.Root, timeoutMilliseconds: 30000);
        Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
        Assert.True(File.Exists(Path.Combine(fixture.Payload, "DemoModule.dll")));
    }

    private static void AssertDesktopBindingProcess(DesktopAssetFixture fixture, string content, string marker)
    {
        string script = Path.Combine(fixture.Root, "Validate-DesktopBindings.ps1");
        File.WriteAllText(script, content);
        var result = RunProcess(DesktopBindingHost(),
            $"-NoLogo -NoProfile -NonInteractive -File \"{script}\"", fixture.Root, timeoutMilliseconds: 30000);
        Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
        Assert.Contains(marker, result.StandardOutput);
    }

    private static string DesktopBindingHost() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
}
