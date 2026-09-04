using System.Diagnostics;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationInteropCleanTargetTests
{
    [WindowsFact]
    public void HybridNet472ExecutesLegacyWmiOnlyInWindowsPowerShellHost()
    {
        using var fixture = ModuleFixture.Create("""
function Get-LegacyManagementCaption {
    [CmdletBinding()]
    param()
    (Get-WmiObject -Class Win32_OperatingSystem).Caption
}
Export-ModuleMember -Function Get-LegacyManagementCaption
""");
        var result = fixture.Build("LegacyManagement", "net472");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        var output = Run("powershell.exe", result.ArtifactPath!, "Get-LegacyManagementCaption");
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    [WindowsFact]
    public void HybridExecutesCimBackedCommandFromCleanPowerShell7Host()
    {
        using var fixture = ModuleFixture.Create("""
function Get-ModernManagementCaption {
    [CmdletBinding()]
    param()
    (Get-CimInstance -ClassName Win32_OperatingSystem).Caption
}
Export-ModuleMember -Function Get-ModernManagementCaption
""");
        var result = fixture.Build("ModernManagement", "net10.0");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = Run("pwsh", result.ArtifactPath!, "Get-ModernManagementCaption");
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    [WindowsFact]
    public void HybridPackagesCdxmlAndExecutesGeneratedManagementCommand()
    {
        using var fixture = ModuleFixture.Create("""
function Get-CdxmlManagementCaption {
    [CmdletBinding()]
    param()
    (Get-GenericOperatingSystem).Caption
}
Export-ModuleMember -Function Get-CdxmlManagementCaption
""");
        fixture.Write("GenericManagement.cdxml", """
<?xml version="1.0" encoding="utf-8"?>
<PowerShellMetadata xmlns="http://schemas.microsoft.com/cmdlets-over-objects/2009/11">
  <Class ClassName="root/cimv2/Win32_OperatingSystem" ClassVersion="1.0">
    <Version>1.0</Version>
    <DefaultNoun>GenericOperatingSystem</DefaultNoun>
    <InstanceCmdlets>
      <GetCmdletParameters />
      <GetCmdlet>
        <CmdletMetadata Verb="Get" />
      </GetCmdlet>
    </InstanceCmdlets>
  </Class>
</PowerShellMetadata>
""");
        fixture.WriteManifest("@{ RootModule='input.psm1'; ModuleVersion='1.0.0'; NestedModules=@('GenericManagement.cdxml'); FunctionsToExport=@('Get-CdxmlManagementCaption','Get-GenericOperatingSystem') }");
        var result = fixture.Build("CdxmlManagement", "net10.0");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(
            result.Manifest!.DependencyGraph!.Nodes.Any(static node => node.Kind == PowerShellCompilationDependencyNodeKind.CdxmlModule),
            string.Join(Environment.NewLine, result.Manifest.DependencyGraph.Nodes.Select(static node => $"{node.Kind}|{node.Identity.Name}|{node.Note}")));
        var output = Run("pwsh", result.ArtifactPath!, "(Get-GenericOperatingSystem).Caption");
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    [WindowsFact]
    public void HybridExecutesExternalProcessAndPreservesExitAndOutput()
    {
        using var fixture = ModuleFixture.Create("""
function Invoke-GenericProcess {
    [CmdletBinding()]
    param()
    & $env:ComSpec /d /s /c "echo process-proof"
    if ($LASTEXITCODE -ne 0) { throw "process failed: $LASTEXITCODE" }
}
Export-ModuleMember -Function Invoke-GenericProcess
""");
        var result = fixture.Build("ExternalProcess", "net10.0");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("process-proof", Run("pwsh", result.ArtifactPath!, "Invoke-GenericProcess"));
    }

    [WindowsFact]
    public void HybridExecutesNativeInteropAndCleansGeneratedTypeScopeOnProcessExit()
    {
        using var fixture = ModuleFixture.Create("""
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GenericNativeClock {
    [DllImport("kernel32.dll")]
    public static extern ulong GetTickCount64();
}
'@
function Get-GenericNativeTick {
    [CmdletBinding()]
    param()
    [GenericNativeClock]::GetTickCount64()
}
Export-ModuleMember -Function Get-GenericNativeTick
""");
        var result = fixture.Build("NativeInterop", "net10.0");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(ulong.TryParse(Run("pwsh", result.ArtifactPath!, "Get-GenericNativeTick"), out var ticks));
        Assert.True(ticks > 0);
    }

    [WindowsFact]
    public void HybridNet472ExecutesComAndReleasesRuntimeCallableWrapper()
    {
        using var fixture = ModuleFixture.Create("""
function Get-GenericComTemporaryName {
    [CmdletBinding()]
    param()
    $instance = New-Object -ComObject Scripting.FileSystemObject
    try { $instance.GetTempName() }
    finally { [void] [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($instance) }
}
Export-ModuleMember -Function Get-GenericComTemporaryName
""");
        var result = fixture.Build("ComInterop", "net472");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = Run("powershell.exe", result.ArtifactPath!, "Get-GenericComTemporaryName");
        Assert.EndsWith(".tmp", output, StringComparison.OrdinalIgnoreCase);
    }

    private static string Run(string host, string modulePath, string command)
    {
        var escapedPath = modulePath.Replace("'", "''", StringComparison.Ordinal);
        var start = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$ErrorActionPreference='Stop'; Import-Module -Name '{escapedPath}' -Force; {command}");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), $"Clean-target host '{host}' timed out.");
        Assert.True(process.ExitCode == 0, error + Environment.NewLine + output);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        return string.Join(Environment.NewLine, output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private sealed class ModuleFixture : IDisposable
    {
        private ModuleFixture(string root, string scriptPath, string outputPath, string manifestPath)
        {
            Root = root;
            ScriptPath = scriptPath;
            OutputPath = outputPath;
            ManifestPath = manifestPath;
        }

        public string Root { get; }
        public string ScriptPath { get; }
        public string OutputPath { get; }
        public string ManifestPath { get; }

        public static ModuleFixture Create(string source)
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeInteropCleanTargetTests", Guid.NewGuid().ToString("N"));
            var output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            var script = Path.Combine(root, "input.psm1");
            var manifest = Path.Combine(root, "input.psd1");
            File.WriteAllText(script, source);
            File.WriteAllText(manifest, "@{ RootModule='input.psm1'; ModuleVersion='1.0.0'; FunctionsToExport='*' }");
            return new ModuleFixture(root, script, output, manifest);
        }

        public void Write(string relativePath, string content) => File.WriteAllText(Path.Combine(Root, relativePath), content);

        public void WriteManifest(string content) => File.WriteAllText(ManifestPath, content);

        public PowerShellCompilationBuildResult Build(string artifactName, string targetFramework)
            => new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                ScriptPath,
                OutputPath,
                artifactName,
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = targetFramework,
                ModuleManifestPath = ManifestPath
            });

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
