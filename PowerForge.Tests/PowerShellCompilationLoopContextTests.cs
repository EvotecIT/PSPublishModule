namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LoopContexts_PropagateThroughLocalCallsAndNativeHandlers(bool hosted, bool handler)
    {
        var source = """
            function Get-Inner { param([double]$Limit)
                $value=0.0
                while($value -lt $Limit) { $value += 1.0 }
                return $value
            }
            """ + (handler
            ? "function Get-Outer { param([double]$Limit) try { return Get-Inner -Limit $Limit } catch { return -1.0 } }"
            : "function Get-Outer { param([double]$Limit) return Get-Inner -Limit $Limit }");
        var document = PowerShellSourceParser.Parse(source, TestPath("loop-callback-calls.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0",
            hosted ? PowerShellCompilationCapabilities.BinaryModule : PowerShellCompilationCapability.None);
        Assert.Empty(result.Emitted.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        Assert.Equal(2, result.Emitted.Methods.Length);
        var inner = result.Emitted.Methods.Single(method => method.GeneratedName == "Get_Inner");
        var outer = result.Emitted.Methods.Single(method => method.GeneratedName == "Get_Outer");
        Assert.Equal(hosted, inner.RequiresPowerShellStopping);
        Assert.Equal(hosted, outer.RequiresPowerShellStopping);
        Assert.False(inner.RequiresPowerShellStatementErrors);
        Assert.Equal(hosted && handler, outer.RequiresPowerShellStatementErrors);
        if (hosted) {
            Assert.Contains("__checkLoopInterrupts()", inner.Source, StringComparison.Ordinal);
            Assert.Contains("__checkLoopInterrupts", outer.Source, StringComparison.Ordinal);
            if (handler) Assert.Contains("__statementErrors.EnterHandler()", outer.Source, StringComparison.Ordinal);
        } else {
            Assert.DoesNotContain("__checkLoopInterrupts", inner.Source + outer.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("__statementErrors", inner.Source + outer.Source, StringComparison.Ordinal);
        }
    }
}
