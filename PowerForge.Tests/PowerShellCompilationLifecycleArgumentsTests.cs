namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilerGate")]
public sealed class PowerShellCompilationLifecycleArgumentsTests
{
    [Theory]
    [InlineData("[Parameter(ValueFromPipeline,Position=0)][int]$Value,[Parameter(Position=1)][switch]$Compact", "$true")]
    [InlineData("[switch]$Compact,[Parameter(ValueFromPipeline)][int]$Value", "$true")]
    [InlineData("[switch]$Compact,[Parameter(ValueFromPipeline)][int]$Value,[switch]$Duplicate", "-Duplicate $true")]
    public void LifecycleRetainsPositionalBindingBeforePipelineInput(string parameters, string arguments)
    {
        var source = "function Format-Records { [CmdletBinding()] [OutputType([string])] param(" + parameters + ") " +
            "begin { } process { \"$Value\" } } function Invoke-Records { 1,2 | Format-Records " + arguments + " }";
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "lifecycle-positional-arguments.ps1")) },
            "net10.0", PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Format_Records");
        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Records");
        Assert.Contains(result.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "PSB2923");
    }

    [Theory]
    [InlineData("-Value 3")]
    [InlineData("-Compact -Compact")]
    [InlineData("-Unknown")]
    [InlineData("$true")]
    public void LifecycleRetainsCallsWithConflictingOrUnknownArguments(string arguments)
    {
        var source = "function Format-Records { [CmdletBinding()] [OutputType([string])] " +
            "param([Parameter(ValueFromPipeline)][int]$Value,[switch]$Compact) " +
            "begin { } process { \"$Value\" } } " +
            "function Invoke-Records { 1,2 | Format-Records " + arguments + " }";
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "lifecycle-conflicting-arguments.ps1")) },
            "net10.0", PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Format_Records");
        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Records");
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Theory]
    [InlineData("")]
    [InlineData("end { }")]
    [InlineData("end { 'done' }")]
    public void LifecyclePreservesAdditionalSwitchesAndOptionalEnd(string endBlock)
    {
        var source = "function Format-Records { [CmdletBinding()] [OutputType([string])] " +
            "param([switch]$Compact,[Parameter(ValueFromPipeline)][int]$Value,[switch]$Duplicate) " +
            "begin { if($Compact){$Prefix='v'}else{$Prefix='value:'} } " +
            "process { \"$Prefix$Value\"; if($Duplicate){\"$Prefix$Value\"} } " + endBlock + " } " +
            "function Invoke-Records { param([int[]]$Values,[bool]$Compact,[bool]$Duplicate) " +
            "$Values | Format-Records -Compact:$Compact -Duplicate:$Duplicate }";
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "lifecycle-arguments.ps1")) },
            "net10.0", PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lifecycle = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Format-Records");
        Assert.Equal(new[] { typeof(bool), typeof(int[]), typeof(bool) }, lifecycle.Parameters.Select(static parameter => parameter.Type.ClrType));
        Assert.Equal(2, result.Emitted.Methods.Length);
        Assert.All(result.Emitted.Methods, static method => Assert.Equal(typeof(string[]), method.ReturnType));
    }
}
