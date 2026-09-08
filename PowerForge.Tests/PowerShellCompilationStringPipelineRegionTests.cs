using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void StringPipelineBinding_PreservesNativeHeaderWithCompiledBody()
    {
        using var fixture = ArtifactFixture.Create("""
            function Format-PipelineText {
                [CmdletBinding()] param([Parameter(ValueFromPipelineByPropertyName)][string]$Text)
                return "text=$Text"
            }
            """, ".psm1");
        var transpiler = new PowerShellTypedCompilationTranspiler();
        var hybrid = transpiler.TranspileForBinaryModule(new[] { fixture.ScriptPath }, "Generated.StringPipeline", "Methods",
            "net10.0", PowerShellCompilationCapabilities.HybridModule);
        var method = Assert.Single(hybrid.Methods);
        Assert.NotNull(method.NativeFunctionBinding);
        Assert.Empty(hybrid.PromotedRegions);
        Assert.Equal("Format-PipelineText", method.SourceName);
        var parameter = Assert.Single(method.Parameters);
        Assert.Equal("Text", parameter.Name);
        Assert.True(parameter.AcceptsPipelineInput);
        var strict = transpiler.TranspileForBinaryModule(new[] { fixture.ScriptPath }, "Generated.StringPipeline", "Methods", "net10.0");
        Assert.Empty(strict.Methods);
        Assert.Empty(strict.PromotedRegions);
        Assert.Contains(strict.Diagnostics, static diagnostic => diagnostic.Message.Contains("conversion-pass ordering", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StringPipelineBinding_CompiledBodyPreservesNativeConversionPasses(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Format-PipelineText {
                [CmdletBinding()] param(
                    [Parameter(ValueFromPipelineByPropertyName)][string]$First,
                    [Parameter(ValueFromPipelineByPropertyName)][string]$Value)
                return "first=$First;value=$Value"
            }
            function New-PipelineValue {
                param([bool]$Mutate)
                $item=[pscustomobject]@{Mutate=$Mutate}
                Add-Member -InputObject $item -MemberType ScriptMethod -Name ToString -Force -Value {
                    $global:BindingTrace += "seen=$First"
                    if($this.Mutate) { Set-Variable -Scope 1 -Name First -Value 'changed' }
                    "seen=$First"
                }
                $item
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StringPipelineRegion", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.PromotedTypedRegions);
        Assert.True(result.Manifest.RuntimeFallbackUnits > 0);
        var unit = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, static item => item.Name == "Format-PipelineText");
        Assert.True(unit.EmittedClrMethod);
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);
        const string probe = """
            $global:BindingTrace=@()
            @(
                [pscustomobject]@{First='one';Value=(New-PipelineValue -Mutate $false)}
                [pscustomobject]@{First='two';Value=(New-PipelineValue -Mutate $true)}
                [pscustomobject]@{First='two';Value=(New-PipelineValue -Mutate $false)}
            ) | Format-PipelineText
            Format-PipelineText -First 'fresh' -Value (New-PipelineValue -Mutate $true)
            $global:BindingTrace
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-string-pipeline-region");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-string-pipeline-region");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
