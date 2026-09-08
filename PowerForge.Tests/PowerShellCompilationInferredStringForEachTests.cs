using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_RuntimeFreeInferredStringForEachPreservesNullAndScalarValues(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-CastCount { $Value = [string]'abc'; [int]$Count = 0; foreach ($Item in $Value) { $Count += 1 }; return $Count }
            function Get-EmptyStringCount { $Value = [string]''; [int]$Count = 0; foreach ($Item in $Value) { $Count += 1 }; return $Count }
            function Get-NullStringCount { $Value = [string]'abc'; $Value = $null; [int]$Count = 0; foreach ($Item in $Value) { $Count += 1 }; return $Count }
            function Get-ConstrainedStringCount { [string]$Value = 'abc'; $Value = $null; [int]$Count = 0; foreach ($Item in $Value) { $Count += 1 }; return $Count }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.InferredStringLoops",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.CompiledMethods);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        const string probe = "Get-CastCount; Get-EmptyStringCount; Get-NullStringCount; Get-ConstrainedStringCount";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "$assembly=[Reflection.Assembly]::LoadFrom('" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'); " +
            "foreach($name in 'Get_CastCount','Get_EmptyStringCount','Get_NullStringCount','Get_ConstrainedStringCount') { " +
            "$method=$assembly.GetTypes().GetMethods() | Where-Object Name -eq $name; $method.Invoke($null,@()) }");
        Assert.Equal((0, "1|1|0|1", string.Empty),
            (original.ExitCode, string.Join("|", original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)), original.StandardError.Trim()));
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }
}
