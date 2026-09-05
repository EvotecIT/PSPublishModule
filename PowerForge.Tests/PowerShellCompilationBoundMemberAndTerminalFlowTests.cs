namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void NullableClrValueMemberFlowsThroughNeutralInteropNodes()
    {
        var document = PowerShellSourceParser.Parse(
            "function Test-TypeToken { param([string] $Name, [int] $Expected) return [Type]::GetType($Name).MetadataToken -eq $Expected }",
            TestPath("nullable-clr-member.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net8.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var comparison = Assert.IsType<PowerShellBoundBinaryExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements)).Expression);
        var member = Assert.IsType<PowerShellBoundClrMemberExpression>(comparison.Left);
        Assert.Equal(typeof(int?), member.Type.ClrType);
        Assert.Equal(PowerShellClrReceiverBehavior.PropagateNull, member.ReceiverBehavior);
        Assert.Contains("?.MetadataToken", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTryExpressionOutputLowersToBranchReturn()
    {
        var document = PowerShellSourceParser.Parse(
            "function Test-Value { param([int] $Value) try { $result = $Value -gt 0; $result } catch { return $false } }",
            TestPath("terminal-try-output.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net8.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var tryStatement = Assert.IsType<PowerShellBoundTryStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements));
        Assert.IsType<PowerShellBoundExpressionStatement>(tryStatement.Body.Statements[^1]);
        var loweredTry = Assert.Single(Assert.Single(result.Lowered.Functions).Statements.OfType<PowerShellLoweredTryStatement>());
        Assert.IsType<PowerShellLoweredReturnStatement>(loweredTry.Statements[^1]);
    }
}
