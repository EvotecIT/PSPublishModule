using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeAccessCaptures_PreserveTargetOrderRecordsConversionAndFailure(string framework, string host)
    {
        const string body = "$Trace.Add('rhs'); if($Fail){throw 'capture failure'}; $item";
        var assignments = new[] {
            "$Holder.Map[$Key.Value] = foreach($item in $Items){ BODY }",
            "$Holder.Map[$Key.Value] = @(foreach($item in $Items){ BODY })",
            "[int[]]$Holder.Map[$Key.Value] = foreach($item in $Items){ BODY }",
            "$Holder.Map[$Key.Value] = for($i=0;$i -lt $Items.Count;$i++){ $item=$Items[$i]; BODY }",
            "$Holder.Map[$Key.Value] = @(try { foreach($item in $Items){ BODY } } finally { $Trace.Add('inner-finally') })"
        };
        var source = string.Join(Environment.NewLine, assignments.Select((assignment,index) =>
            "function Set-AccessCapture"+index+" { [CmdletBinding()]param($Holder,$Key,$Items,$Trace,[switch]$Fail); try { "+
            assignment.Replace("BODY",body)+" } catch {[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}} finally {$Trace.Add('outer-finally')} }"));
        using var fixture=ArtifactFixture.Create(source,".psm1");
        var built=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.AccessCapture",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true){TargetFramework=framework});
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        Assert.True(built.Manifest!.CompiledMethods == assignments.Length,string.Join(Environment.NewLine,
            built.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit=>unit.DiagnosticChain.Select(cause=>unit.Name+": "+cause.Message))));
        const string probe="""
            foreach($index in 0..4) {
                foreach($items in @(@(),@(7),@(7,8),@('bad'),@($null))) {
                    foreach($mode in 'normal','receiver','key') {
                        foreach($fail in $false,$true) {
                            $global:AccessCaptureTrace=[Collections.Generic.List[string]]::new()
                            $global:AccessCaptureMode=$mode
                            $global:AccessCaptureMap=[ordered]@{slot='prior'}
                            $holder=[pscustomobject]@{}
                            $holder|Add-Member ScriptProperty Map {
                                $global:AccessCaptureTrace.Add('receiver')
                                if($global:AccessCaptureMode -eq 'receiver'){throw 'receiver failure'}
                                $global:AccessCaptureMap
                            }
                            $key=[pscustomobject]@{}
                            $key|Add-Member ScriptProperty Value {
                                $global:AccessCaptureTrace.Add('key')
                                if($global:AccessCaptureMode -eq 'key'){throw 'key failure'}
                                'slot'
                            }
                            $name='Set-AccessCapture'+$index
                            $result=@(& $name -Holder $holder -Key $key -Items $items -Trace $global:AccessCaptureTrace -Fail:$fail -ErrorAction Stop)
                            $stored=$global:AccessCaptureMap['slot']
                            [pscustomobject]@{index=$index;items=@($items);mode=$mode;fail=$fail;result=$result;stored=@($stored);null=$null -eq $stored;type=$(if($null -ne $stored){$stored.GetType().FullName});trace=@($global:AccessCaptureTrace)}|ConvertTo-Json -Depth 8 -Compress
                        }
                    }
                }
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        var expected=original.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries);
        var actual=generated.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(150,expected.Length);
        Assert.Equal(expected.Length,actual.Length);
        for(var index=0;index<expected.Length;index++)
            Assert.True(expected[index]==actual[index],"Original: "+expected[index]+Environment.NewLine+"Generated: "+actual[index]);
        Assert.Contains("System.Int32[]",generated);
        Assert.Contains("capture failure",generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_ForestControllerCapturePreservesOfflineInventory(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "ActiveDirectory", "Get-WinADForestControllers.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ForestCapture", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Get-WinADForestControllers" && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding && !unit.RetainedHostedSource);
        const string probe = """
            function global:Get-ADForest {
                [CmdletBinding()]param([pscredential]$Credential)
                if($global:ForestCaptureMode -eq 'forest-error'){throw "offline forest failure`nsecond line"}
                [pscustomobject]@{Domains=@('good.invalid','bad.invalid');RootDomain='offline.invalid'}
            }
            function global:Get-ADDomainController {
                [CmdletBinding()]param([switch]$Discover,[string]$DomainName,[string]$Server,[string]$Filter,[switch]$Writable,[pscredential]$Credential)
                if($Discover) {
                    if($DomainName -eq 'bad.invalid'){throw "offline domain failure`nsecond line"}
                    return [pscustomobject]@{HostName=@('dc.good.invalid')}
                }
                foreach($ips in @(@(),@('192.0.2.1'),@('192.0.2.1','192.0.2.2'))) {
                    [pscustomobject]@{HostName='dc.good.invalid';Name='DC';IPV4Address=$ips;IPV6Address=$null;IsGlobalCatalog=$true;IsReadOnly=$false;Site='offline';OperationMasterRoles=@('SchemaMaster','PDCEmulator');LdapPort=389;SslPort=636}
                }
            }
            function global:Test-Connection {
                [CmdletBinding()]param([int]$Count,[string]$Server,[switch]$Quiet)
                $global:ForestCaptureCalls.Add($Server)
                $Server -eq '192.0.2.1'
            }
            foreach($mode in 'normal','forest-error') {
                foreach($availability in $false,$true) {
                    foreach($skip in $false,$true) {
                        $global:ForestCaptureMode=$mode
                        $global:ForestCaptureCalls=[Collections.Generic.List[string]]::new()
                        $warnings=@()
                        $result=@(Get-WinADForestControllers -TestAvailability:$availability -SkipEmpty:$skip -WarningAction SilentlyContinue -WarningVariable warnings)
                        [pscustomobject]@{mode=$mode;availability=$availability;skip=$skip;records=$result;calls=@($global:ForestCaptureCalls);warnings=@($warnings|ForEach-Object {$_.Message})}|ConvertTo-Json -Depth 8 -Compress
                    }
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Equal(8, generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"Pingable\":[true,false]", generated);
        Assert.Contains("offline domain failure second line", generated);
        Assert.Contains("offline forest failure second line", generated);
    }

    [Theory]
    [InlineData("$Holder.($Name) = foreach($item in $Items){$item}")]
    [InlineData("$Holder.GetMap()['slot'] = foreach($item in $Items){$item}")]
    [InlineData("$Holder[(Get-Date)] = foreach($item in $Items){$item}")]
    [InlineData("[Missing.Authored.Type]$Holder['slot'] = foreach($item in $Items){$item}")]
    public void NativeAccessCaptures_UnqualifiedTargetsRemainHosted(string assignment)
    {
        using var fixture=ArtifactFixture.Create("function Set-Access { [CmdletBinding()]param($Holder,$Name,$Items); "+assignment+" }", ".psm1");
        var result=new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(new[]{fixture.ScriptPath},
            "Generated.AccessBoundary","CompiledPowerShell","net10.0",PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Methods);
    }

    [Fact]
    public void NativeAccessCaptures_PreserveCleanupHostedTerminalRegion()
    {
        var root = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "CleanupMonster.psm1");
        var sources = Directory.GetFiles(Path.GetDirectoryName(root)!, "*.ps1", SearchOption.AllDirectories);
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(sources,
            "Generated.CleanupCaptureBoundary", "CompiledPowerShell", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Invoke-ADComputersCleanup");
        Assert.True(typed.PromotedRegions.Any(region => region.SourceName == "Invoke-ADComputersCleanup"),
            System.Text.Json.JsonSerializer.Serialize(typed.RegionCandidates));
    }

    [Fact]
    public void NativeAccessCaptures_DoNotWidenRuntimeFreeAdmission()
    {
        var source=PowerShellSourceParser.Parse("function Set-Access { param($Holder,$Items); $Holder['slot'] = foreach($item in $Items){$item} }",
            Path.Combine(Path.GetTempPath(),"access-capture-strict.ps1"));
        var result=new PowerShellSemanticCompilationPipeline().Compile(new[]{source},"net10.0",PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
    }
}
