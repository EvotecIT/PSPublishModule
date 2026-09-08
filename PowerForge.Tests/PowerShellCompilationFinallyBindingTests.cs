namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinallyPreservesStoppingOnlyForHostBackedMethods(bool hosted)
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Value { param([int]$Value) try { $Value=1 } finally { $Value=2 }; return $Value }",
            TestPath("finally-stopping.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net8.0",
            hosted ? PowerShellCompilationCapabilities.BinaryModule : PowerShellCompilationCapability.None);

        Assert.Empty(result.Emitted.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var source = Assert.Single(result.Emitted.Methods).Source;
        if (hosted) Assert.Contains("__statementErrors.EnterFinally()", source, StringComparison.Ordinal);
        else Assert.DoesNotContain("__statementErrors", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return 1")]
    [InlineData("break")]
    [InlineData("continue")]
    public void FinallyRejectsTransfersThatLeaveItsScope(string transfer)
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Value { param([int]$Value) while($Value -gt 0) { try { $Value=0 } finally { " + transfer + " } }; return $Value }",
            TestPath("finally-transfer.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net8.0");

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(document.Errors, error => error.ErrorId == "ControlLeavingFinally");
        Assert.Contains(result.Bound.Diagnostics, diagnostic => diagnostic.Code == "PSB0001");
    }
}
