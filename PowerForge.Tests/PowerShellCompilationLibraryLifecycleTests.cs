namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilerGate")]
public sealed class PowerShellCompilationLibraryLifecycleTests
{
    [Theory]
    [InlineData("System.Boolean", "$Value", "System.Boolean")]
    [InlineData("System.TimeSpan", "$Value.Ticks", "System.Int64")]
    [InlineData("System.DateTime", "$Value.Ticks", "System.Int64")]
    [InlineData("System.Guid", "$Value.ToString()", "System.String")]
    public void LibraryLifecycleDeclaresRequiredNonNullCollections(string inputType, string output, string outputType)
    {
        var source = "function Read-Value { [CmdletBinding()] [OutputType([" + outputType + "])] " +
            "param([Parameter(ValueFromPipeline)][" + inputType + "]$Value) begin { } process { [" + outputType + "]$Result=" + output + ";$Result } }";
        var result = Compile(source);
        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        var parameter = Assert.Single(function.Parameters);
        Assert.True(parameter.Contract.IsMandatory);
        Assert.True(parameter.Contract.AllowEmptyCollection);
        Assert.False(parameter.Contract.AllowNull);
        Assert.Equal(Type.GetType(inputType)!.MakeArrayType(), parameter.Type.ClrType);
        Assert.Equal(Type.GetType(outputType)!.MakeArrayType(), Assert.Single(result.Emitted.Methods).ReturnType);
    }

    [Theory]
    [InlineData("[TimeSpan]::Zero,[TimeSpan]::FromTicks(1)", true)]
    [InlineData("$Values", false)]
    public void LibraryLifecycleRequiresNonNullProofAtAuthoredPipelineCalls(string input, bool accepted)
    {
        var source = "function Read-Ticks { [CmdletBinding()] [OutputType([long])] " +
            "param([Parameter(ValueFromPipeline)][TimeSpan]$Value) begin { } process { [long]$Ticks=$Value.Ticks;$Ticks } } " +
            "function Invoke-Ticks { param([TimeSpan[]]$Values) " + input + " | Read-Ticks }";
        var result = Compile(source);
        Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Read_Ticks");
        if (accepted) Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Ticks");
        else
        {
            Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Ticks");
            Assert.Contains(result.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "PSB2927");
        }
    }

    private static PowerShellSemanticCompilationResult Compile(string source)
        => new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "library-lifecycle.ps1")) },
            "net10.0", PowerShellCompilationBuildSpec.GetCapabilities(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict));
}
