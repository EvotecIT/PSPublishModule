namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeDelegateCasts_PreserveArgumentsDynamicScopeOutputAndEscape(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-DelegateCasts {
                [CmdletBinding()] param([int[]]$Values,[int]$Threshold)
                $trace=[Collections.Generic.List[string]]::new()
                $action=[Action[int]] { param($value) $trace.Add('action:'+ $value); 'discarded' }
                foreach($value in $Values) { $action.Invoke($value) }
                $predicate=[System.Predicate[int]] { param($value) $value -gt $Threshold }
                $first=@(); foreach($value in $Values) { $first+= $predicate.Invoke($value) }
                $Threshold=5
                $second=@(); foreach($value in $Values) { $second+= $predicate.Invoke($value) }
                $implicit=[System.Action[string]] { $trace.Add('args:'+ $args[0]) }
                $implicit.Invoke('implicit')
                $scalar=[System.Func[int]] { '12' }
                $many=[System.Func[object]] { 'one'; $null; 'two' }
                $failure=[Action] { try { throw 'delegate failed' } finally { $trace.Add('finally') } }
                try { $failure.Invoke() } catch { $trace.Add($_.FullyQualifiedErrorId); $trace.Add($_.Exception.Message) }
                [pscustomobject]@{trace=@($trace);first=$first;second=$second;scalar=$scalar.Invoke();many=$many.Invoke()}
            }
            function New-EscapedAction {
                [System.Action[string]] { param($value) $script:escaped.Add($value); 'discarded' }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeDelegateCasts", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach(var name in new[] { "Invoke-DelegateCasts", "New-EscapedAction" })
        {
            var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit=>unit.Name==name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        }
        const string probe="""
            foreach($values in @(@(),@(1),@(1,3,9),@(1,3,9))) {
                Invoke-DelegateCasts -Values $values -Threshold 2 | ConvertTo-Json -Depth 8 -Compress
            }
            $owner=(Get-Command New-EscapedAction).Module
            & $owner { $script:escaped=[Collections.Generic.List[string]]::new() }
            $escaped=New-EscapedAction
            $escaped.Invoke('first'); $escaped.Invoke('second')
            & $owner { [pscustomobject]@{escaped=@($script:escaped)} | ConvertTo-Json -Compress }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.True(original==generated,"Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("\"scalar\":12",generated);
        Assert.Contains("delegate failed",generated);
        Assert.Contains("\"escaped\":[\"first\",\"second\"]",generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeDelegateCasts_PreserveOfflineTraceFallbackForUnavailableHostTypes(string framework, string host)
    {
        var source=FindCompleteConversionWorkflow("PSScriptTools","TraceMessage","Trace.ps1");
        using var fixture=ArtifactFixture.Create(File.ReadAllText(source),".psm1");
        var built=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.TraceAppend",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true) { TargetFramework=framework });
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,unit=>unit.Name=="Trace-Message");
        Assert.False(unit.EmittedClrMethod);
        Assert.True(unit.RetainedHostedSource);
        Assert.Contains(unit.DiagnosticChain!, diagnostic => diagnostic.Message.Contains("RunspaceFactory", StringComparison.Ordinal));
        const string probe="""
            & (Get-Command Trace-Message).Module { function script:Get-Date { [datetime]'2020-01-02T03:04:05' } }
            $box=[pscustomobject]@{Text='';Dispatcher=$null}
            $box | Add-Member -MemberType ScriptMethod -Name AppendText -Value { param([string]$value) $this.Text += $value }
            $dispatcher=[pscustomobject]@{Calls=[Collections.Generic.List[string]]::new();Failure=$false}
            $dispatcher | Add-Member -MemberType ScriptMethod -Name Invoke -Value {
                param([Action]$action,[object]$priority)
                $this.Calls.Add([string]$priority)
                if($this.Failure) { throw 'offline dispatcher failed' }
                $action.Invoke()
            }
            $box.Dispatcher=$dispatcher
            $global:traceSynchHash=@{TextBox=$box}
            foreach($enabled in $false,$true,$true) {
                $global:TraceEnabled=$enabled
                $records=@('one','two' | Trace-Message -Verbose 4>&1)
                Trace-Message -Message 'three'
                [pscustomobject]@{enabled=$enabled;text=$box.Text;calls=@($dispatcher.Calls);verbose=@($records | ForEach-Object {$_.ToString()})} | ConvertTo-Json -Depth 6 -Compress
            }
            $dispatcher.Failure=$true
            try { Trace-Message -Message 'failed' -ErrorAction Stop } catch {
                [pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} | ConvertTo-Json -Compress
            }
            $dispatcher.Failure=$false
            Trace-Message -Message 'after'
            [pscustomobject]@{text=$box.Text;calls=@($dispatcher.Calls);helperVisible=[bool](Get-Command _SetTraceMessage -ErrorAction SilentlyContinue)} | ConvertTo-Json -Depth 6 -Compress
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.True(original==generated,"Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("03:04:05 - one",generated);
        Assert.Contains("offline dispatcher failed",generated);
        Assert.Contains("\"helperVisible\":false",generated);
    }
}

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Theory]
    [InlineData("trap { continue }; 'value'")]
    [InlineData("dynamicparam { } end { 'value' }")]
    [InlineData("param($__writeOutput) $__writeOutput")]
    public void NativeDelegateCasts_RetainOwnerWhenChildCannotCompile(string body)
    {
        var document=PowerShellSourceParser.Parse("function Read-Owner { $action=[Action] { "+body+" }; $action.Invoke() }",
            TestPath("delegate-cast-fallback.psm1"));
        var result=new PowerShellSemanticCompilationPipeline().Compile(new[] {document},"net10.0",PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeDelegateCasts_RejectRuntimeFreeDynamicCallbacks(string framework)
    {
        var document=PowerShellSourceParser.Parse("function Read-Owner { $action=[Action] { 'value' }; $action.Invoke() }",
            TestPath("delegate-cast-strict.psm1"));
        var result=new PowerShellSemanticCompilationPipeline().Compile(new[] {document},framework,PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }
}
