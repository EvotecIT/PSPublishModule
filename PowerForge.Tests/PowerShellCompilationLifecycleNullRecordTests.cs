namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("string")]
    [InlineData("Uri")]
    [InlineData("Version")]
    [InlineData("Nullable[int]")]
    public void RuntimeFreeLifecycle_RetainsMandatoryNullableRecordValidation(string typeName)
    {
        using var fixture = ArtifactFixture.Create("function Read-Value { [CmdletBinding()] [OutputType([int])] " +
            "param([Parameter(Mandatory,ValueFromPipeline)][" + typeName + "]$Value) " +
            "begin{} process{42} end{99} }");
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.ParseFile(fixture.ScriptPath) }, "net10.0", PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeLifecycle_NormalizesNullStringRecordsBeforeProcess()
    {
        using var fixture = ArtifactFixture.Create("function Measure-Records { [CmdletBinding()] [OutputType([string])] " +
            "param([Parameter(ValueFromPipeline)][string]$Value) begin{} process{$Value} end{'done'} } " +
            "function Invoke-Records {param([string[]]$Values) $Values | Measure-Records} 'x','y' | Measure-Records");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NullStringRecords", PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        { TargetFramework = "net10.0", SingleFile = false });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var assemblyPath = Assert.Single(result.Manifest.Files, static file => file.Role == "GeneratedAssembly").Path;
        const string probe = """
            $cases=[Collections.Generic.List[object]]::new()
            $cases.Add($null);$cases.Add([string[]]@())
            $values=[string[]]::new(3);$values[1]='abc';$cases.Add($values)
            $values=[string[]]::new(3);$values[0]='';$values[2]='last';$cases.Add($values)
            foreach($index in 0..3) {
                $failure=$null
                try {$records=@(Invoke-Case $cases[$index])}catch{$records=@();$failure=$_.Exception.GetType().FullName}
                [pscustomobject]@{index=$index;values=$records;failure=$failure} | ConvertTo-Json -Compress
            }
            """;
        var original = RunStatementErrorProbe("pwsh", "$null=. '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; function Invoke-Case($values){Invoke-Records -Values $values}; " + probe, fixture.RootPath, "original-null-string-records");
        var compiled = RunStatementErrorProbe("pwsh", "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(assemblyPath) +
            "');$method=($assembly.GetTypes() | Where-Object {$null -ne $_.GetMethod('Invoke_Records')}).GetMethod('Invoke_Records');" +
            "function Invoke-Case($values){$arguments=[object[]]::new(1);$arguments[0]=$values;$method.Invoke($null,$arguments)}; " + probe,
            fixture.RootPath, "compiled-null-string-records");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Empty(compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
