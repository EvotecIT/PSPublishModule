using PowerForge;
using System.Collections;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    private readonly struct EnumerableValue : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private const string ShortCircuitReferenceFunctions = """
function Test-AndNullRefinement {
    param([System.Version] $Value)
    return ($null -ne $Value) -and ($Value.Major -gt 0)
}
function Test-AndReversedNullRefinement {
    param([System.Version] $Value)
    return ($Value -ne $null) -and ($Value.Major -gt 0)
}
function Test-OrNullRefinement {
    param([System.Version] $Value)
    return ($null -eq $Value) -or ($Value.Major -gt 0)
}
function Test-AndOppositePredicate {
    param([System.Version] $Value)
    return ($null -eq $Value) -and ($Value.Major -gt 0)
}
function Test-OrOppositePredicate {
    param([System.Version] $Value)
    return ($null -ne $Value) -or ($Value.Major -gt 0)
}
""";

    [Fact]
    public void BoundShortCircuitNullPredicatesUseIdentityAndRefineOnlyTheRightOperand()
    {
        var document = PowerShellSourceParser.Parse(ShortCircuitReferenceFunctions, "short-circuit-null-refinement.ps1");
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        Assert.Equal(5, result.Emitted.Methods.Length);
        Assert.All(result.Emitted.Methods, static method =>
            Assert.Contains("global::System.Object.ReferenceEquals", method.Source, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeShortCircuitNullRefinementExecutesAcrossTargets(string targetFramework)
    {
        using var fixture = OracleFixture.Create(ShortCircuitReferenceFunctions);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "ShortCircuitNullRefinement" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(build.ArtifactPath!);
        var methods = assembly.GetTypes()
            .SelectMany(static type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            .ToDictionary(static method => method.Name, StringComparer.Ordinal);
        var one = new Version(1, 0);
        var zero = new Version(0, 0);

        Assert.Equal(false, methods["Test_AndNullRefinement"].Invoke(null, new object?[] { null }));
        Assert.Equal(true, methods["Test_AndNullRefinement"].Invoke(null, new object?[] { one }));
        Assert.Equal(false, methods["Test_AndNullRefinement"].Invoke(null, new object?[] { zero }));
        Assert.Equal(true, methods["Test_AndReversedNullRefinement"].Invoke(null, new object?[] { one }));
        Assert.Equal(true, methods["Test_OrNullRefinement"].Invoke(null, new object?[] { null }));
        Assert.Equal(false, methods["Test_OrNullRefinement"].Invoke(null, new object?[] { zero }));
        Assert.Equal(false, methods["Test_AndOppositePredicate"].Invoke(null, new object?[] { null }));
        Assert.Equal(false, methods["Test_AndOppositePredicate"].Invoke(null, new object?[] { one }));
        Assert.Equal(false, methods["Test_OrOppositePredicate"].Invoke(null, new object?[] { null }));
        Assert.Equal(true, methods["Test_OrOppositePredicate"].Invoke(null, new object?[] { one }));
        Assert.Equal(true, methods["Test_OrOppositePredicate"].Invoke(null, new object?[] { zero }));
    }

    [Theory]
    [InlineData("System.Collections.Generic.List[int]", "Count")]
    [InlineData("System.IO.Stream", "Position")]
    public void LeftOperandWithoutSealedScalarProofDoesNotRefineNullFlow(string typeName, string memberName)
    {
        using var fixture = OracleFixture.Create(
            $"function Test-Value {{ param([{typeName}] $Value) " +
            $"return ($Value -ne $null) -and ($Value.{memberName} -gt 0) }}");
        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("not proven scalar", StringComparison.Ordinal));
    }

    [Fact]
    public void NullComparisonScalarPolicyRejectsEnumerableValueAndNullableValueShapes()
    {
        Assert.True(PowerShellNullComparisonSemanticPolicy.IsScalar(typeof(int), comparedValueIsLeft: true));
        Assert.True(PowerShellNullComparisonSemanticPolicy.IsScalar(typeof(int?), comparedValueIsLeft: true));
        Assert.True(PowerShellNullComparisonSemanticPolicy.IsScalar(typeof(Version), comparedValueIsLeft: true));
        Assert.True(PowerShellNullComparisonSemanticPolicy.IsScalar(typeof(string), comparedValueIsLeft: true));
        Assert.False(PowerShellNullComparisonSemanticPolicy.IsScalar(typeof(EnumerableValue), comparedValueIsLeft: true));
        Assert.False(PowerShellNullComparisonSemanticPolicy.IsScalar(typeof(EnumerableValue?), comparedValueIsLeft: true));
        Assert.False(PowerShellNullComparisonSemanticPolicy.IsScalar(typeof(Stream), comparedValueIsLeft: true));
        Assert.True(PowerShellNullComparisonSemanticPolicy.IsScalar(typeof(Stream), comparedValueIsLeft: false));
    }
}
