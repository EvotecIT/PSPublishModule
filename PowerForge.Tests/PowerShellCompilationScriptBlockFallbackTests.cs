namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Theory]
    [InlineData("function Read-Child($Value) { $Value }")]
    [InlineData("filter Read-Child { $_ }")]
    [InlineData("function Read-Child { dynamicparam { } end { 'value' } }")]
    [InlineData("function Read-Child { trap { continue }; 'value' }")]
    [InlineData("function Read-Child { param($__writeOutput) $__writeOutput }")]
    public void NestedFunctions_RetainOwnerWhenDeclarationOrChildCannotCompile(string declaration)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Owner { " + declaration + "; Read-Child }", TestPath("nested-function-fallback.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NestedFunctions_RejectRuntimeFreeDynamicDeclarations(string framework)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Owner { param([string]$Value) function Read-Child { $Value }; Read-Child }",
            TestPath("nested-function-strict.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, framework, PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Theory]
    [InlineData("dynamicparam { } end { 'value' }")]
    [InlineData("trap { continue }; 'value'")]
    [InlineData("param($__writeOutput) $__writeOutput")]
    public void MethodScriptBlocks_RetainOwnerWhenChildCannotCompile(string body)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Filtered { param([object[]]$Values) $Values.Where({ " + body + " }) }",
            TestPath("method-scriptblock-fallback.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void MethodScriptBlocks_RejectRuntimeFreeDynamicScope(string framework)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Filtered { param([object[]]$Values,[int]$Minimum) $Values.Where({ $_ -gt $Minimum }) }",
            TestPath("method-scriptblock-strict.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, framework, PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Theory]
    [InlineData("param($__writeOutput) $__writeOutput")]
    [InlineData("$nested={ param($__writeOutput) $__writeOutput }; $nested")]
    public void NativeScriptBlocks_RetainOwnersAfterChildLoweringFailure(string body)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Block { $block={ " + body + " }; $block }",
            TestPath("scriptblock-lowering-fallback.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Emitted.Diagnostics, diagnostic => diagnostic.Code == "PSL1009");
        Assert.Contains(result.Emitted.Diagnostics, diagnostic => diagnostic.Code == "PSL1015");
    }

    [Fact]
    public void Lowering_RetainsLocalCallersAfterCalleeLoweringFailure()
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Child { param([string]$__writeOutput) $__writeOutput; 'after' } " +
            "function Read-Owner { Read-Child 'ready'; 'last' }", TestPath("call-lowering-fallback.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains("PSL1009", result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code));
        Assert.Contains("PSL1015", result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Theory]
    [InlineData("dynamicparam { } end { 'value' }")]
    [InlineData("trap { continue }; 'value'")]
    [InlineData("$nested={ dynamicparam { } end { 'value' } }; & $nested")]
    public void NativeScriptBlocks_RetainOwnersWithUnsupportedChildBodies(string body)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Block { $block={ " + body + " }; & $block }",
            TestPath("scriptblock-fallback.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Fact]
    public void NativeScriptBlocks_RejectRuntimeFreeLoweringOfNativeClosures()
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Block { $block={ param($Value) $Value }; & $block 'ready' }",
            TestPath("scriptblock-capabilities.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Diagnostics);
        Assert.Equal(2, result.Emitted.Methods.Length);
        var strict = new PowerShellTypedLowerer().Lower(result.Analyzed, PowerShellCompilationCapability.None);
        Assert.Empty(strict.Functions);
        Assert.Contains(strict.Diagnostics, diagnostic => diagnostic.Code == "PSL1013");
    }
}
