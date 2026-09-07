using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("return [string]([Console]::WriteLine('probe'))", false)]
    [InlineData("return 'value'", true)]
    public void ValueConsumption_ProtectsTransitiveRegionCallClosure(string body, bool emitted)
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Leaf { [CmdletBinding()] param(); " + body + " }; " +
            "function Get-Middle { [CmdletBinding()] param(); Get-Leaf }; " +
            "function Get-Caller { [CmdletBinding()] param(); Get-Middle }; " +
            "function Get-Independent { [CmdletBinding()] param(); return 'safe' }", "void-call-closure.psm1");
        var capabilities = PowerShellCompilationCapabilities.HybridModule;
        var bound = new PowerShellSemanticBinder().BindWithRegionCandidates(new[] { document }, "net10.0", capabilities).Program;
        var optimized = new PowerShellBoundOptimizer().Optimize(bound).Program;
        var analyzed = new PowerShellSemanticAnalyzer().AnalyzeRegionOpportunities(optimized);
        var lowered = new PowerShellTypedLowerer().Lower(analyzed, capabilities);
        Assert.Equal(emitted ? 4 : 1, lowered.Functions.Length);
        Assert.Contains(lowered.Functions, function => function.Symbol.Name == "Get-Independent");
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0", capabilities);
        foreach (var caller in new[] { "Get-Middle", "Get-Caller" })
            Assert.Equal(emitted, result.RegionOpportunities.Any(opportunity => opportunity.SourceName == caller));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("return [string]([Console]::WriteLine('probe'))", false)]
    [InlineData("return 'value'", true)]
    public void ValueConsumption_AlsoProtectsRegionOpportunityLowering(string body, bool emitted)
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Value { [CmdletBinding()] param(); " + body + " }", "void-opportunity.psm1");
        var capabilities = PowerShellCompilationCapabilities.HybridModule;
        var bound = new PowerShellSemanticBinder().BindWithRegionCandidates(new[] { document }, "net10.0", capabilities).Program;
        var optimized = new PowerShellBoundOptimizer().Optimize(bound).Program;
        var analyzed = new PowerShellSemanticAnalyzer().AnalyzeRegionOpportunities(optimized);
        var lowered = new PowerShellTypedLowerer().Lower(analyzed, capabilities);
        Assert.Equal(emitted ? 1 : 0, lowered.Functions.Length);
        if (!emitted) Assert.Contains(lowered.Diagnostics, diagnostic => diagnostic.Code == "PST2301");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("return [object](Invoke-Empty)", false)]
    [InlineData("Invoke-Empty; return 1", true)]
    public void ValueConsumption_UsesTheSameRuleForRuntimeIndependentLibraries(string body, bool emitted)
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Empty { param(); $Value=1 }; function Get-Value { param(); " + body + " }");
        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: "net10.0");
        Assert.Equal(emitted, result.Methods.Any(method => method.SourceName == "Get-Value"));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ValueConsumption_PreservesSideEffectOnlyCallsAndReturns(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Clear-Local { [CmdletBinding()] param([Collections.ArrayList]$Trace); $Trace.Clear() }
            function Invoke-DirectClear { [CmdletBinding()] param([Collections.ArrayList]$Trace); $Trace.Clear(); 'after' }
            function Invoke-DiscardClear { [CmdletBinding()] param([Collections.ArrayList]$Trace); [void](Clear-Local $Trace); 'after' }
            function Invoke-ReturnClear { [CmdletBinding()] param([Collections.ArrayList]$Trace); return $Trace.Clear() }
            function Invoke-ReturnLocal { [CmdletBinding()] param([Collections.ArrayList]$Trace); return Clear-Local $Trace }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.VoidStatements", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(5, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            foreach ($command in 'Clear-Local','Invoke-DirectClear','Invoke-DiscardClear','Invoke-ReturnClear','Invoke-ReturnLocal') {
                $trace=[Collections.ArrayList]@('seed')
                $records=@(& $command -Trace $trace)
                [pscustomobject]@{command=$command;records=$records;count=$trace.Count} | ConvertTo-Json -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-void-statements");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-void-statements");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output ([object]($Trace.Clear())) -NoEnumerate")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output -InputObject ([string]($Trace.Clear())) -NoEnumerate")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output (($Trace.Clear()),1) -NoEnumerate")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output (,($Trace.Clear())) -NoEnumerate")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output (@{item=$Trace.Clear()}) -NoEnumerate")]
    [InlineData("return [object]($Trace.Clear())")]
    [InlineData("return [string]($Trace.Clear())")]
    [InlineData("$Value=$Trace.Clear(); return 1")]
    [InlineData("return [object]::ReferenceEquals(($Trace.Clear()),$null)")]
    [InlineData("return ($Trace.Clear()) -is [object]")]
    [InlineData("return ($Trace.Clear()) -eq $null")]
    [InlineData("if (($Trace.Clear())) { return 1 } else { return 2 }")]
    public void ValueConsumption_RetainsVoidOperandsAcrossValuePositions(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Value { [CmdletBinding()] param([Collections.ArrayList]$Trace); " + body + " }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "VoidValuePositions", "net10.0");
        Assert.Empty(result.Methods);
        Assert.NotEmpty(result.Diagnostics);
    }
}
