using PowerForge;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void CommandCondition_PreservesOfflineDnsWorkflowAndRuntimeRebinding(string framework, string host)
    {
        var path = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "DNS", "Set-DNSServerIPAddress.ps1");
        Assert.Equal("989857F0FB658042F9F64B1D37F6B0F994B6EA5BBF34E57D9F088C9577D7AF85",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(path), ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CommandCondition", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(1, built.Manifest!.CompiledMethods);
        var entry = Assert.Single(built.Manifest.UnitDispositionLedger!.Entries, item => item.EmittedClrMethod);
        Assert.True(entry.UsesNativeFunctionBinding);
        Assert.False(entry.RetainedHostedSource);
        const string probe = """
            $module=Get-Module|Where-Object {$_.ExportedCommands.ContainsKey('Set-DnsServerIpAddress')}|Select-Object -First 1
            & $module {
                function script:Test-Connection {param($ComputerName,$Count,[switch]$Quiet)
                    $script:Trace.Add("query:$ComputerName/$Count/$Quiet")
                    if($script:QueryMode -eq 'error'){Write-Error 'offline-query-error'}
                    if($script:QueryMode -eq 'throw'){throw 'offline-query-throw'}
                    $script:QueryOutput
                }
                function script:Invoke-Command {param($ComputerName,[scriptblock]$ScriptBlock,$ArgumentList)
                    $script:Trace.Add("invoke:$ComputerName");& $ScriptBlock @ArgumentList
                }
                function script:Set-DnsClientServerAddress {param($InterfaceAlias,$ServerAddresses)
                    $script:Trace.Add("set:$InterfaceAlias/$ServerAddresses")
                }
                function script:Write-Host {param($Object);$script:Trace.Add("host:$Object")}
            }
            foreach($case in 'false','true','null','zero','one','empty-string','text','empty-array','one-false','two-false','error-false','error-true','throw','rebound'){
                & $module {param($Case)
                    $script:Trace=[Collections.Generic.List[string]]::new()
                    $script:QueryMode=if($Case -like 'error-*'){'error'}elseif($Case -eq 'throw'){'throw'}else{'normal'}
                    $script:QueryOutput=switch($Case){'false'{$false}'true'{$true}'null'{$null}'zero'{0}'one'{1}'empty-string'{''}'text'{'x'}'empty-array'{,@()}'one-false'{,@($false)}'two-false'{,@($false,$false)}'error-false'{$false}default{$true}}
                    if($Case -eq 'rebound'){function script:Test-Connection {param($ComputerName,$Count,[switch]$Quiet);$script:Trace.Add("rebound:$ComputerName/$Count/$Quiet");$false}}
                } $case
                $warnings=@();$errors=@();$failure=$null
                try{Set-DnsServerIpAddress -ComputerName 'offline.invalid' -NicName 'owned-interface' -IpAddresses '192.0.2.1' -WarningVariable warnings -ErrorVariable errors -ErrorAction Continue 2>$null 3>$null}
                catch{$failure=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                $trace=& $module {$script:Trace.ToArray()}
                [pscustomobject]@{case=$case;trace=@($trace);warnings=@($warnings|ForEach-Object {[string]$_});errors=@($errors|ForEach-Object {$_.FullyQualifiedErrorId});failure=$failure}|ConvertTo-Json -Depth 6 -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Equal(original, RunModuleProof(built.ArtifactPath!, probe, host));
        var rows = original.Split(Environment.NewLine).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        Assert.Equal(14, rows.Length);
        foreach (var row in rows)
        {
            var name = row.GetProperty("case").GetString();
            var trace = row.GetProperty("trace").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Equal(name == "rebound" ? "rebound:offline.invalid/2/True" : "query:offline.invalid/2/True", trace[0]);
            if (name == "throw") { Assert.NotEqual(JsonValueKind.Null, row.GetProperty("failure").ValueKind); Assert.Single(trace); }
            else if (name is "true" or "one" or "text" or "two-false" or "error-true")
            {
                Assert.Equal(4, trace.Length);
                Assert.Equal("set:owned-interface/192.0.2.1", trace[3]);
                Assert.Empty(row.GetProperty("warnings").EnumerateArray());
            }
            else { Assert.Single(trace); Assert.Single(row.GetProperty("warnings").EnumerateArray()); }
        }
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void CommandCondition_SelectsEachControlFlowOwnerAndRequiresNativeHostCapabilities(string framework)
    {
        var document = PowerShellSourceParser.Parse("""
            function Test-IfCommand {[CmdletBinding()]param();if(Read-OfflineCondition){1}else{0}}
            function Test-WhileCommand {[CmdletBinding()]param();while(Read-OfflineCondition){break};0}
            function Test-DoCommand {[CmdletBinding()]param();do{1}while(Read-OfflineCondition)}
            function Test-ForCommand {[CmdletBinding()]param();for($i=0;Read-OfflineCondition;$i++){break};0}
            function Test-ForeachCommand {[CmdletBinding()]param();foreach($item in Read-OfflineCondition){$item}}
            function Test-UntilCommand {[CmdletBinding()]param();do{1}until(Read-OfflineCondition)}
            """, Path.Combine(Path.GetTempPath(), "command-condition-boundaries.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework,
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(6, result.Emitted.Methods.Length);
        Assert.All(result.Emitted.Methods, method => Assert.NotNull(method.NativeFunctionBinding));
        Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework,
            PowerShellCompilationCapabilities.TypedLibrary).Emitted.Methods);
        foreach (var capabilities in new[] {
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes })
        {
            var rejected = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework, capabilities);
            // Other existing hosted command-region routes can emit without the native ABI.
            Assert.All(rejected.Emitted.Methods, method => Assert.Null(method.NativeFunctionBinding));
        }
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void CommandCondition_PreservesRepeatedLoopEvaluationAndFailureFinally(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ConditionLoops {
                [CmdletBinding()]param([string]$Mode)
                $value=0
                try {
                    if(Read-OfflineCondition -Position 0 -Mode $Mode){$value=1}
                    elseif(Read-OfflineCondition -Position 1 -Mode $Mode){$value=2}
                    while(Read-OfflineCondition -Position $value -Mode $Mode){$value++;if($value -gt 3){break}}
                    do {$value++} while(Read-OfflineCondition -Position $value -Mode $Mode)
                    do {$value++} until(Read-OfflineTerminal -Position $value)
                    for($value=0;Read-OfflineCondition -Position $value -Mode $Mode;$value++){'for:'+ $value}
                    foreach($item in Read-OfflineCondition -Position 0 -Mode $Mode){'each:'+ [string]$item}
                    'done'
                }catch{'caught:'+ $_.FullyQualifiedErrorId+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine}
                finally{Write-OfflineTrace -Value 'finally'}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ConditionLoops", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(1, built.Manifest!.CompiledMethods);
        const string probe = """
            $module=Get-Module|Where-Object {$_.ExportedCommands.ContainsKey('Get-ConditionLoops')}|Select-Object -First 1
            & $module {
                function script:Read-OfflineCondition {param($Position,$Mode)
                    $script:Trace.Add("read:$Position")
                    if($Mode -eq 'throw' -and $Position -eq 1){throw 'offline-loop-throw'}
                    if($Mode -eq 'empty'){return}
                    if($Mode -eq 'multiple' -and $Position -lt 2){$false;$false;return}
                    $Position -lt 2
                }
                function script:Write-OfflineTrace {param($Value);$script:Trace.Add($Value)}
                function script:Read-OfflineTerminal {param($Position);$script:Trace.Add("until:$Position");$Position -ge 2}
            }
            foreach($mode in 'normal','empty','multiple','throw'){
                & $module {$script:Trace=[Collections.Generic.List[string]]::new()}
                $records=@(Get-ConditionLoops -Mode $mode)
                $trace=& $module {$script:Trace.ToArray()}
                [pscustomobject]@{mode=$mode;records=$records;trace=@($trace)}|ConvertTo-Json -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Equal(original, RunModuleProof(built.ArtifactPath!, probe, host));
        Assert.Contains("\"for:0\",\"for:1\",\"each:True\",\"done\"", original);
        Assert.Contains("\"each:False\",\"each:False\"", original);
        Assert.Contains("caught:offline-loop-throw", original);
        Assert.Contains("finally", original);
    }
}
