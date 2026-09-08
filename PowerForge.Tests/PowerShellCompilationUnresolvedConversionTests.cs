using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeConversions_RetainUnresolvedTypesForRuntimeResolution(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-FutureConversion { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); return [FutureNativeConversionType]$Value }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.UnresolvedConversion", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        const string probe = """
            try { Read-FutureConversion -Value @{Value=42} -ErrorAction Stop } catch { $_.FullyQualifiedErrorId }
            Add-Type -TypeDefinition 'public sealed class FutureNativeConversionType { public int Value { get; set; } }'
            $value=Read-FutureConversion -Value @{Value=42}
            $value.GetType().FullName+':'+$value.Value
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-unresolved-conversion");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-unresolved-conversion");
        Assert.True(original.ExitCode == 0 && compiled.ExitCode == 0, original.StandardError + compiled.StandardError);
        Assert.Contains("TypeNotFound", original.StandardOutput);
        Assert.Contains("FutureNativeConversionType:42", original.StandardOutput);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
