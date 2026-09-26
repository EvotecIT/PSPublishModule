using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeLabeledLoops_PreserveTargetsFinallyIterationAndOfflineSelection(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Get-ADComputersToProcess.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + Environment.NewLine + """
            function Trace-LabeledLoops {
                [CmdletBinding()] param([int]$Mode,[string]$__loopContinue_0='collision')
                $trace=[Collections.Generic.List[string]]::new()
                $foreach='previous'
                :Outer foreach($i in 1,2,3,4) {
                    try {
                        foreach($j in 1,2) {
                            if($i -eq 2) { continue oUtEr }
                            if($i -eq 4) { break OUTER }
                            $trace.Add("$i/$j")
                        }
                    } catch { $trace.Add('unexpected catch') }
                    finally { $trace.Add("finally:$i/$($foreach.Current)") }
                    $trace.Add("tail:$i")
                }
                $trace.Add('after:'+ $__loopContinue_0)
                $trace.Add("state:$foreach")
                $trace -join '|'
            }
            function Trace-LabeledKinds {
                [CmdletBinding()] param([int]$Seed)
                $trace=[Collections.Generic.List[string]]::new()
                $i=0
                :WhileOuter while($i -lt 4) { $i++; foreach($j in 1,2) { if($i -eq 2){continue WhileOuter}; if($i -eq 4){break WhileOuter}; $trace.Add("w:$i/$j") } }
                $i=0
                :DoOuter do { $i++; foreach($j in 1,2) { if($i -eq 2){continue DoOuter}; if($i -eq 4){break DoOuter}; $trace.Add("d:$i/$j") } } until($i -ge 5)
                $i=0
                :DoWhileOuter do { $i++; foreach($j in 1,2) { if($i -eq 2){continue DoWhileOuter}; if($i -eq 4){break DoWhileOuter}; $trace.Add("dw:$i/$j") } } while($i -lt 5)
                :ForOuter for($i=1;$i -lt 5;$i++) { foreach($j in 1,2) { if($i -eq 2){continue ForOuter}; if($i -eq 4){break ForOuter}; $trace.Add("f:$i/$j") } }
                :Same foreach($i in 1,2) { :same foreach($j in 1,2) { if($j -eq 1){continue SAME}; $trace.Add("same:$i/$j") }; $trace.Add("outer:$i") }
                $trace -join '|'
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLabeledLoops", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach(var name in new[] {"Trace-LabeledLoops","Trace-LabeledKinds","Get-ADComputersToProcess"})
        {
            var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,unit=>unit.Name==name);
            Assert.True(unit.EmittedClrMethod,System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
        }
        const string probe="""
            Trace-LabeledLoops -Mode 1; Trace-LabeledLoops -Mode 2; Trace-LabeledKinds -Seed 1; Trace-LabeledKinds -Seed 2
            & (Get-Command Get-ADComputersToProcess).Module {
                function Write-Color { param($Text,$Color) }
                function Get-Date { [datetime]'2020-01-02T03:04:05' }
            }
            $cases=@(
                @{name='none';rules=@{};exclusions=@()},
                @{name='nested exclusion';rules=@{};exclusions=@('missing','alpha*')},
                @{name='include systems';rules=@{IncludeSystems=@('missing','Windows*')};exclusions=@()},
                @{name='exclude systems';rules=@{ExcludeSystems=@('missing','Linux*')};exclusions=@()},
                @{name='include spn';rules=@{IncludeServicePrincipalName=@('missing','HOST/*')};exclusions=@()},
                @{name='exclude spn';rules=@{ExcludeServicePrincipalName=@('missing','HOST/*')};exclusions=@()},
                @{name='no spn';rules=@{NoServicePrincipalName=$true};exclusions=@()},
                @{name='require spn';rules=@{NoServicePrincipalName=$false};exclusions=@()},
                @{name='enabled';rules=@{IsEnabled=$true};exclusions=@()},
                @{name='disabled';rules=@{IsEnabled=$false};exclusions=@()})
            foreach($repeat in 1,2) {
                foreach($type in 'Disable','Move','Delete') {
                    foreach($case in $cases) {
                        $rows=@(
                            [pscustomobject]@{SamAccountName='alpha$';DistinguishedName='CN=alpha,DC=example,DC=test';DNSHostName='alpha.example.test';DomainName='example.test';OperatingSystem='Windows 10';ServicePrincipalName=@('HOST/alpha');Enabled=$true;Action='Initial'},
                            [pscustomobject]@{SamAccountName='beta$';DistinguishedName='CN=beta,DC=example,DC=test';DNSHostName='beta.example.test';DomainName='example.test';OperatingSystem='Linux';ServicePrincipalName=@();Enabled=$false;Action='Initial'})
                        $count=@(Get-ADComputersToProcess -Type $type -Computers $rows -ActionIf $case.rules -Exclusions $case.exclusions -DomainInformation @{} -ProcessedComputers @{})
                        [pscustomobject]@{repeat=$repeat;type=$type;case=$case.name;count=$count;rows=$rows} | ConvertTo-Json -Depth 8 -Compress
                    }
                }
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.True(original==generated,"Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("finally:2/2",generated);
        Assert.Contains("finally:4/4|after:collision|state:previous",generated);
        Assert.Contains("same:1/2|outer:1|same:2/2|outer:2",generated);
        Assert.DoesNotContain("unexpected catch",generated);
        Assert.Contains("ExcludedByFilter",generated);
        Assert.Contains("ExcludedBySetting",generated);
    }

    [Theory]
    [InlineData("function Read-Unclosed { [CmdletBinding()]param(); :Outer foreach($i in 1,2) { continue Missing } }")]
    [InlineData("function Read-Unclosed { [CmdletBinding()]param([string]$Label); :Outer foreach($i in 1,2) { continue $Label } }")]
    [InlineData("function Read-Unclosed { [CmdletBinding()]param(); :Outer foreach($i in 1,2) { :Outer switch($i) { default { continue Outer } } } }")]
    [InlineData("function Read-Unclosed { [CmdletBinding()]param(); :Outer foreach($i in 1,2) { $x=@(if($i -eq 1){continue Outer}); $x } }")]
    [InlineData("function Read-Unclosed { [CmdletBinding()]param(); :Outer foreach($i in 1,2) { $callback={continue Outer}; $callback } }")]
    public void NativeLabeledLoops_UnclosedTargetsRemainHosted(string source)
    {
        using var fixture=ArtifactFixture.Create(source,".psm1");
        var plan=new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
            PowerShellCompilationMode.Hybrid,targetFramework:"net10.0",capabilities:PowerShellCompilationCapabilities.BinaryModule));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeLabeledLoops_RuntimeFreeTargetRemainsClosed(string framework)
    {
        using var fixture=ArtifactFixture.Create("function Read-Loop { :Outer foreach($i in 1,2) { continue Outer } }");
        var plan=new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
            PowerShellCompilationMode.Strict,targetFramework:framework,capabilities:PowerShellCompilationCapabilities.TypedExecutable));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }
}
