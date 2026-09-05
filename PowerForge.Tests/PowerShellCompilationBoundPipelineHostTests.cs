namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void StreamWritesAndTheirLocalCallAbiAreLoweredFromBoundNodes()
    {
        var document = PowerShellSourceParser.Parse(
            "function Write-Inner { Microsoft.PowerShell.Utility\\Write-Verbose 'inner' } function Write-Outer { Write-Inner }",
            TestPath("stream-write-ir.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net8.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var inner = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Write-Inner");
        Assert.IsType<PowerShellBoundStreamWriteStatement>(Assert.Single(inner.Body.Statements));
        Assert.True(inner.Effects.HasFlag(PowerShellSemanticEffect.NonSuccessStream));
        var loweredInner = Assert.Single(result.Lowered.Functions, static function => function.Symbol.Name == "Write-Inner");
        Assert.True(loweredInner.RequiresPowerShellStreams);
        Assert.IsType<PowerShellLoweredStreamWriteStatement>(Assert.Single(loweredInner.Statements));
        var loweredOuter = Assert.Single(result.Lowered.Functions, static function => function.Symbol.Name == "Write-Outer");
        Assert.True(loweredOuter.RequiresPowerShellStreams);
        var call = Assert.IsType<PowerShellLoweredInvocationExpression>(
            Assert.IsType<PowerShellLoweredReturnStatement>(Assert.Single(loweredOuter.Statements)).Expression);
        Assert.True(call.RequiresPowerShellStreams);
        var outerSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Write_Outer").Source;
        Assert.Contains("Write_Inner(__writeOutput, __writeVerbose, __writeDebug, __writeWarning, __writeInformation, __writeHost, __writeError);", outerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRegionsAndTypedCapturesAreOwnedByBoundAndLoweredIr()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-RegionValue { param([string] $Name) [int] $count = 1; Get-Date -Format o; [string] $captured = Get-Date -Format o; $count += 1; return $captured }",
            TestPath("command-region-ir.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net8.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        Assert.Contains(function.Body.Statements, static statement => statement is PowerShellBoundCommandRegionStatement);
        var capture = Assert.IsType<PowerShellBoundCommandCaptureStatement>(
            Assert.Single(function.Body.Statements, static statement => statement is PowerShellBoundCommandCaptureStatement));
        Assert.Equal("captured", capture.Target.Name);
        var lowered = Assert.Single(result.Lowered.Functions);
        Assert.True(lowered.RequiresPowerShellCommandRegions);
        Assert.Contains(lowered.Statements, static statement => statement is PowerShellLoweredCommandRegionStatement);
        Assert.Contains(lowered.Statements, static statement => statement is PowerShellLoweredCommandCaptureStatement);
        var method = Assert.Single(result.Emitted.Methods);
        Assert.True(method.RequiresPowerShellCommandRegions);
        Assert.Contains("__invokePowerShellRegion", method.Source, StringComparison.Ordinal);
        Assert.Contains("__invokePowerShellCapture", method.Source, StringComparison.Ordinal);
        Assert.Contains("string captured =", method.Source, StringComparison.Ordinal);
        Assert.Contains("count = checked((int)(count + 1))", method.Source, StringComparison.Ordinal);
    }
}
