using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void StatementDiscardsPreserveTypedOperandEvaluationThroughLowering()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-DiscardedValue { param([int] $Value) [void] $Value; $null = $Value; return $Value }",
            TestPath("statement-discard.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        var boundDiscards = function.Body.Statements.Take(2)
            .Select(static statement => Assert.IsType<PowerShellBoundExpressionStatement>(statement))
            .ToArray();
        Assert.All(boundDiscards, static statement =>
        {
            Assert.False(statement.EmitsOutput);
            Assert.IsType<PowerShellBoundVariableExpression>(statement.Expression);
        });
        var loweredDiscards = Assert.Single(result.Lowered.Functions).Statements.Take(2)
            .Select(static statement => Assert.IsType<PowerShellLoweredExpressionStatement>(statement))
            .ToArray();
        Assert.All(loweredDiscards, static statement => Assert.True(statement.DiscardValue));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("static void __discardValue_0<T>(T value) { }", source, StringComparison.Ordinal);
        Assert.Equal(2, source.Split("__discardValue_0<int>(Value);", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void StatementDiscardsInsideNestedBlocksUseTheSameBackendOwner()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-DiscardedValue { param([int] $Value) " +
            "if ($Value -gt 0) { [void] $Value }; " +
            "foreach ($Item in [int[]] @(1)) { [void] $Item }; " +
            "switch ($Value) { 1 { [void] $Value } default { [void] $Value } }; " +
            "try { $null = $Value } finally { [void] $Value }; return $Value }",
            TestPath("nested-statement-discard.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("static void __discardValue_0<T>(T value) { }", source, StringComparison.Ordinal);
        Assert.Equal(5, source.Split("__discardValue_0<int>(Value);", StringSplitOptions.None).Length - 1);
        Assert.Contains("__discardValue_0<int>(Item);", source, StringComparison.Ordinal);
    }
}

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Analyze_AcceptsVoidConversionAsStandaloneStatement()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Value { param([int] $Value) [void] $Value; return $Value }");

        var unit = Assert.Single(Assert.Single(
            new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath)).Files).Units);

        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static item => item.Message)));
        Assert.DoesNotContain(unit.Diagnostics, static diagnostic =>
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.Conversion);
    }

    [Theory]
    [InlineData("return [void] $Value")]
    [InlineData("[int] $Other = [void] $Value; return $Other")]
    [InlineData("[void] $Value | ForEach-Object { $_ }; return $Value")]
    public void Analyze_RejectsVoidConversionOutsideStandaloneStatement(string body)
    {
        using var fixture = ArtifactFixture.Create($"function Get-Value {{ param([int] $Value) {body} }}");

        var unit = Assert.Single(Assert.Single(
            new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath)).Files).Units);

        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.Conversion);
    }

    [Theory]
    [InlineData("$([void] $Value); return $Value")]
    [InlineData("@([void] $Value); return $Value")]
    [InlineData("$(if ($true) { [void] $Value }; $Value); return $Value")]
    [InlineData("@(if ($true) { [void] $Value }; $Value); return $Value")]
    public void Analyze_PackageRejectsVoidConversionInsideExpressionContainers(string body)
    {
        using var fixture = ArtifactFixture.Create($"function Get-Value {{ param([int] $Value) {body} }}");
        var document = PowerShellSourceParser.Parse(File.ReadAllText(fixture.ScriptPath), fixture.ScriptPath);
        var conversion = Assert.Single(document.SyntaxRoot.FindAll(
            static node => node is System.Management.Automation.Language.ConvertExpressionAst,
            searchNestedScriptBlocks: true).Cast<System.Management.Automation.Language.ConvertExpressionAst>());

        var unit = Assert.Single(Assert.Single(
            new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
                fixture.ScriptPath,
                PowerShellCompilationMode.Package)).Files).Units);

        Assert.False(PowerShellCompilationConversionPolicy.CanLower(
            conversion,
            "net10.0",
            PowerShellCompilationCapability.None));
        Assert.False(unit.IsCompilable);
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void Build_StrictLibraryEvaluatesAndSuppressesStatementDiscardOperands(string targetFramework)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-DiscardedValue { param([int] $Value) " +
            "[Text.StringBuilder] $Trace = [Text.StringBuilder]::new(); " +
            "[void] $Value; $null = $Value; " +
            "[void] $Trace.Append('A'); $null = $Trace.Append('B'); return $Trace.ToString() }; " +
            "function Get-VoidOperandCount { " +
            "[Collections.Generic.List[string]] $Values = [Collections.Generic.List[string]]::new(); " +
            "[void] $Values.Add('item'); return $Values.Count }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StatementDiscard" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods())
            .Single(static candidate => candidate.Name == "Get_DiscardedValue");
        var voidOperandMethod = assembly.GetTypes().SelectMany(static type => type.GetMethods())
            .Single(static candidate => candidate.Name == "Get_VoidOperandCount");
        Assert.Equal("AB", method.Invoke(null, new object[] { 42 }));
        Assert.Equal(1, voidOperandMethod.Invoke(null, null));
    }
}
