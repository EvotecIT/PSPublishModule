namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Theory]
    [InlineData("return 'ready'")]
    [InlineData("return \"value=$Value\"")]
    [InlineData("$copy=@('first'; 'second'); return \"value=$copy\"")]
    public void NativeFunctionBinding_RemainsVisibleInBoundAndLoweredCapabilities(string body)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Native { param([ValidatePattern('.*')][string]$Value) " + body + " }",
            TestPath("native-capabilities.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Diagnostics);
        var bound = Assert.Single(result.Analyzed.Functions);
        Assert.NotNull(bound.NativeFunctionBinding);
        Assert.True(bound.Capabilities.HasFlag(PowerShellRequiredCapability.NativeFunctionBinding));
        var snapshots = PowerShellCompilationIrSnapshotBuilder.Create(result);
        Assert.Contains("NativeFunctionBinding", Assert.Single(snapshots.Bound, unit => unit.UnitId == bound.Symbol.StableKey).Capabilities);
        Assert.Contains("NativeFunctionBinding", Assert.Single(snapshots.Lowered, unit => unit.UnitId == bound.Symbol.StableKey).Capabilities);

        // Reusing the same bound program cannot smuggle native storage into a runtime-free target.
        var strict = new PowerShellTypedLowerer().Lower(result.Analyzed, PowerShellCompilationCapability.None);
        Assert.Empty(strict.Functions);
        Assert.Contains(strict.Diagnostics, static diagnostic => diagnostic.Code == "PSL1013");
    }

    [Fact]
    public void NativeFunctionBinding_DoesNotDetachVariableReadsIntoTerminalRegions()
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Native { param([ValidatePattern('.*')][string]$Value) & $Value; return \"value=$Value\" }",
            TestPath("native-region-storage.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.PromotedRegions);
    }

    [Theory]
    [InlineData("foreach ($index in 1,2) { }")]
    public void NativeFunctionBinding_DoesNotUseClrLoopStorageForNativeReads(string loop)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Native { param([ValidateRange(1,9)][int]$Value) " + loop + " return \"index=$index\" }",
            TestPath("native-loop-storage.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "PSL1014");
    }

    [Theory]
    [InlineData("trap { continue }; 'value'")]
    public void NativeFunctionBinding_RetainsUnbridgedCollectionStatementEffects(string body)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Native { param([ValidateRange(1,9)][int]$Value) $copy=@(" + body + "); return \"value=$copy\" }",
            TestPath("native-array-statement-effects.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "PSB2501");
    }
}
