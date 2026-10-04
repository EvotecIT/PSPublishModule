using PowerForge;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilerGate")]
public sealed class PowerShellCompilationStableScalarForEachTests
{
    [Theory]
    [InlineData("[int]", "global::System.Array.Empty<int>()", false)]
    [InlineData("[Nullable[int]]", "global::System.Array.Empty<global::System.Nullable<int>>()", true)]
    [InlineData("[uri]", "global::System.Array.Empty<global::System.Uri>()", true)]
    public void StableScalarForEachPreservesNullAndSingleRecordShape(
        string parameterType,
        string emptyArraySource,
        bool canBeNull)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Get-Count {{ param({parameterType} $Value) [int] $Count = 0; foreach ($Item in $Value) {{ $Count += 1 }}; return $Count }}",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "StableScalarForEach", parameterType + ".ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var loop = Assert.IsType<PowerShellBoundForEachStatement>(Assert.Single(
            Assert.Single(result.Analyzed.Functions).Body.Statements,
            static statement => statement is PowerShellBoundForEachStatement));
        Assert.Equal(PowerShellForEachEnumerationKind.StableScalar, loop.EnumerationKind);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Equal(canBeNull, source.Contains(" is null ? ", StringComparison.Ordinal));
        if (canBeNull)
            Assert.Contains(emptyArraySource, source, StringComparison.Ordinal);
        else
            Assert.DoesNotContain("global::System.Array.Empty", source, StringComparison.Ordinal);
    }
}
