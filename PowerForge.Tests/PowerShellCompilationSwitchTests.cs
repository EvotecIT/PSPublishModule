using System.Reflection;
using System.Runtime.Loader;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_TypedLibraryPreservesScalarSwitchMultipleMatchBreakAndDefaultSemantics()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-SwitchValue {
                param([string] $Value)
                [int] $result = 0
                switch ($Value) {
                    'One' { $result += 1 }
                    'ONE' { $result += 10; break }
                    default { $result = -1 }
                }
                return $result
            }
            """);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SwitchProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        using var stream = File.OpenRead(result.ArtifactPath!);
        var context = new AssemblyLoadContext("PowerForgeSwitchProof", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var method = assembly.GetType("PowerForge.Compiled.PowerForge_SwitchProofMethods", throwOnError: true)!
                .GetMethod("Get_SwitchValue", BindingFlags.Public | BindingFlags.Static)!;
            Assert.Equal(11, method.Invoke(null, new object[] { "one" }));
            Assert.Equal(-1, method.Invoke(null, new object[] { "missing" }));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Analyze_RejectsWildcardSwitchAsRuntimeMatching()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SwitchValue { param([string] $Value) switch -Wildcard ($Value) { 'A*' { return 1 } default { return 0 } } }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("switch flags", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_ScalarSwitchContinuePreservesPowerShellSwitchScopeInsideAndOutsideLoops()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TopLevelContinue { [int] $Value = 0; switch (1) { 1 { $Value = 4; continue; $Value = 9 } }; return $Value } " +
            "function Get-NestedContinue { [int] $Sum = 0; for ([int] $Index = 0; $Index -lt 3; $Index++) { switch ($Index) { 1 { continue } }; $Sum += 10; $Sum += $Index }; return $Sum }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));
        var diagnostics = plan.Files.SelectMany(static file => file.Units).SelectMany(static unit => unit.Diagnostics).ToArray();
        Assert.True(plan.CompilableUnits == 2, string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Empty(diagnostics);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SwitchContinueProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled.PowerForge_SwitchContinueProofMethods", throwOnError: true)!;
        Assert.Equal(4, type.GetMethod("Get_TopLevelContinue")!.Invoke(null, null));
        Assert.Equal(33, type.GetMethod("Get_NestedContinue")!.Invoke(null, null));
    }
}
