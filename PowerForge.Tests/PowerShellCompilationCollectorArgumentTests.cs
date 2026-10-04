using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CollectorArguments_PreserveCardinalityErrorsLocalTransfersAndFinally(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-CollectorArgumentResult {
                [CmdletBinding()]param([string]$Kind,$Values,[int]$Mode,$Trace)
                try {
                    switch($Kind){
                        'switch' {
                            [Convert]::ToBase64String($(
                                switch($Values){default {
                                    $Trace.Add('item')
                                    if($Mode -eq 1){continue}
                                    $_
                                    if($Mode -eq 2){break}
                                }}
                            ))
                        }
                        'try' {
                            [Convert]::ToBase64String($(
                                try { $Values; if($Mode -eq 1){throw 'capture'} }
                                catch { $Trace.Add('caught'); [byte]9 }
                                finally { $Trace.Add('inner-finally'); if($Mode -eq 2){[byte]7} }
                            ))
                        }
                        'foreach' {
                            [Convert]::ToBase64String($(
                                foreach($value in $Values){
                                    $Trace.Add('item')
                                    if($Mode -eq 1){continue}
                                    $value
                                    if($Mode -eq 2){break}
                                }
                            ))
                        }
                        'for' {
                            [Convert]::ToBase64String($(
                                for($i=0;$i -lt $Values.Count;$i++){
                                    $Trace.Add('item')
                                    if($Mode -eq 1){continue}
                                    $Values[$i]
                                    if($Mode -eq 2){break}
                                }
                            ))
                        }
                        'while' {
                            $i=0
                            [Convert]::ToBase64String($(
                                while($i -lt $Values.Count){
                                    $value=$Values[$i]; $i++
                                    $Trace.Add('item')
                                    if($Mode -eq 1){continue}
                                    $value
                                    if($Mode -eq 2){break}
                                }
                            ))
                        }
                        'do-while' {
                            $i=0
                            [Convert]::ToBase64String($(
                                do {
                                    $value=$Values[$i]; $i++
                                    $Trace.Add('item')
                                    if($Mode -eq 1){continue}
                                    $value
                                    if($Mode -eq 2){break}
                                }while($i -lt $Values.Count)
                            ))
                        }
                        'do-until' {
                            $i=0
                            [Convert]::ToBase64String($(
                                do {
                                    $value=$Values[$i]; $i++
                                    $Trace.Add('item')
                                    if($Mode -eq 1){continue}
                                    $value
                                    if($Mode -eq 2){break}
                                }until($i -ge $Values.Count)
                            ))
                        }
                    }
                }catch{
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }finally{$Trace.Add('finally')}
            }
            function Get-CommandCollectorArgument {
                [CmdletBinding()]param($Values,$Trace,[bool]$Fail)
                try {
                    [Convert]::ToBase64String($(
                        foreach($value in $Values){
                            Get-OfflineCollectorValue -Value $value -Trace $Trace -Fail:$Fail
                        }
                    ))
                }catch{
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }finally{$Trace.Add('finally')}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CollectorArguments", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries, entry => Assert.True(entry.EmittedClrMethod, entry.Name));
        const string probe = """
            $ErrorActionPreference='Stop'
            foreach($kind in 'switch','try','foreach','for','while','do-while','do-until'){
                foreach($mode in 0..2){foreach($case in 'empty','single','multiple','invalid','type'){
                    $values=switch($case){'empty'{@()};'single'{,[byte]2};'multiple'{,[byte[]]@(1,2)};'invalid'{,'invalid'};'type'{,[string]}}
                    $trace=[Collections.Generic.List[string]]::new()
                    $records=@(Get-CollectorArgumentResult -Kind $kind -Values $values -Mode $mode -Trace $trace)
                    [pscustomobject]@{kind=$kind;mode=$mode;case=$case;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
                }}
            }
            function global:Get-OfflineCollectorValue {
                [CmdletBinding()]param($Value,$Trace,[bool]$Fail)
                $Trace.Add('provider')
                if($Fail){Write-Error -Message 'collector provider failure' -ErrorId 'OfflineCollectorFailure'}
                $Value
            }
            foreach($preference in 'SilentlyContinue','Stop'){
                foreach($fail in $false,$true){foreach($values in (,([byte[]]@(1,2))), (,'invalid')){
                    $Error.Clear(); $ev=@(); $trace=[Collections.Generic.List[string]]::new()
                    $records=@(Get-CommandCollectorArgument -Values $values -Trace $trace -Fail:$fail -ErrorAction $preference -ErrorVariable ev)
                    [pscustomobject]@{kind='command';preference=$preference;fail=$fail;records=$records;trace=@($trace.ToArray());errors=@($ev|ForEach-Object{$_.FullyQualifiedErrorId});globalErrors=@($Error|ForEach-Object{$_.FullyQualifiedErrorId})}|ConvertTo-Json -Depth 6 -Compress
                }}
            }
            $trace=[Collections.Generic.List[string]]::new()
            $records=@(Get-CommandCollectorArgument -Values ([byte[]]@(1,2)) -Trace $trace -Fail:$false | Select-Object -First 1)
            [pscustomobject]@{kind='downstream-stop';records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        var originalLines = original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var generatedLines = generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(114, originalLines.Length);
        Assert.Equal(originalLines.Length, generatedLines.Length);
        Assert.All(originalLines.Zip(generatedLines), pair => Assert.Equal(pair.First, pair.Second));
        Assert.Contains("inner-finally", original);
        Assert.Contains("MethodInvocationException", original);
    }

    [Theory]
    [InlineData("foreach($value in $Values){return $value}")]
    [InlineData("try{$Values}catch{break}")]
    [InlineData("try{$Values}catch{continue}")]
    [InlineData("foreach($value in $Values){break missing}")]
    [InlineData("trap{continue}; foreach($value in $Values){$value}")]
    public void CollectorArguments_RejectEscapingTransfersAndTraps(string statements)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Collector { [CmdletBinding()]param($Values); try {[Convert]::ToBase64String($(" +
            statements + "))}catch{0} }", "collector-boundary.psm1");
        var bound = new PowerShellSemanticBinder().Bind(new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Contains(bound.Diagnostics, diagnostic => diagnostic.Code == "PSB2506");
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void CollectorArguments_KeepRuntimeFreeBoundary(string framework)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-Collector { [CmdletBinding()]param($Values); try {[Convert]::ToBase64String($(foreach($value in $Values){$value}))}catch{0} }",
            "collector-runtime-free.psm1");
        var pipeline = new PowerShellSemanticCompilationPipeline();
        Assert.Single(pipeline.Compile(new[] { document }, framework, PowerShellCompilationCapabilities.HybridModule).Lowered.Functions);
        Assert.Empty(pipeline.Compile(new[] { document }, framework, PowerShellCompilationCapability.None).Lowered.Functions);
    }
}
