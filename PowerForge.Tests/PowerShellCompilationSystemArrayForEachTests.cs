using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string SystemArrayForEachSource =
        "function Measure-SystemArray { [CmdletBinding()] " +
        "param([Parameter(Position=0)][AllowNull()][AllowEmptyCollection()][array] $Values) " +
        "[int] $Count = 0; foreach ($Value in $Values) { $Count += 1 }; return $Count }";

    [Fact]
    public void Analyze_BinaryModuleBindsSystemArrayForEachItemsAsHostObjects()
    {
        using var fixture = ArtifactFixture.Create(SystemArrayForEachSource, ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Equal(typeof(Array).FullName, Assert.Single(unit.Parameters).TypeName);

        var document = PowerShellSourceParser.ParseFile(fixture.ScriptPath);
        var semantic = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);
        Assert.Empty(semantic.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var loop = Assert.IsType<PowerShellLoweredForEachStatement>(
            Assert.Single(Assert.Single(semantic.Lowered.Functions).Statements, static statement => statement is PowerShellLoweredForEachStatement));
        Assert.True(loop.SystemArray);
        Assert.Equal(typeof(object), loop.ElementType);
        Assert.Equal(typeof(Array), loop.Collection.ClrType);
    }

    [Fact]
    public void Analyze_TypedExecutableKeepsSystemArrayForEachOutsideRuntimeFreeContract()
    {
        using var fixture = ArtifactFixture.Create(SystemArrayForEachSource);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterType ||
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.ForSyntax("ForEachStatementAst"));
    }

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesSystemArrayForEachCardinality(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(SystemArrayForEachSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SystemArrayForEach",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true,
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string proof =
            "$matrix = New-Object 'int[,]' 2,2; " +
            "Measure-SystemArray -Values $null; " +
            "Measure-SystemArray -Values @(); " +
            "Measure-SystemArray -Values 42; " +
            "Measure-SystemArray -Values @('a', 2, $null); " +
            "Measure-SystemArray -Values $matrix";
        var original = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(original, compiled);
        Assert.Equal(new[] { "0", "0", "1", "3", "4" }, compiled.Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("foreach (object?", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Array.Empty<object>()", generated, StringComparison.Ordinal);
    }
}
