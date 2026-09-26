using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeDictionaryKeys_PreserveIdentityEvaluationErrorsAndNormalization(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "ConvertTo-NormalizedString.ps1");
        var disableSource = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Request-ADComputersDisable.ps1");
        var moveSource = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Request-ADComputersMove.ps1");
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, File.ReadAllText(source), File.ReadAllText(disableSource), File.ReadAllText(moveSource)) + Environment.NewLine + """
            function Write-Color { param([object[]]$Text,[object[]]$Color) }
            function ConvertFrom-DistinguishedName { param([string]$DistinguishedName,[switch]$ToDomainCN); 'example.test' }
            function Disable-WinADComputer { $global:UnsafeDictionaryProbeCalls++; throw 'Unsafe administration boundary' }
            function Move-WinADComputer { $global:UnsafeDictionaryProbeCalls++; throw 'Unsafe administration boundary' }
            function Get-ADComputerCurrentDistinguishedName { $global:UnsafeDictionaryProbeCalls++; throw 'Unsafe administration boundary' }
            function Set-ADComputer { $global:UnsafeDictionaryProbeCalls++; throw 'Unsafe administration boundary' }
            function Set-ADObject { $global:UnsafeDictionaryProbeCalls++; throw 'Unsafe administration boundary' }
            function Move-ADObject { $global:UnsafeDictionaryProbeCalls++; throw 'Unsafe administration boundary' }
            function Write-Event { $global:UnsafeDictionaryProbeCalls++; throw 'Unsafe administration boundary' }
            function Trace-DictionaryKeys {
                [CmdletBinding()]param([object]$Key,[object]$Second,[bool]$Ordered)
                $trace=[Collections.Generic.List[string]]::new()
                $map='previous'
                try {
                    if($Ordered) { $map=[ordered]@{$Key=$trace.Add('first'); $Second=$trace.Add('second')} }
                    else { $map=@{$Key=$trace.Add('first'); $Second=$trace.Add('second')} }
                    'keys:'+ $map.Count + ':' + $map.Contains($Key) + ':' + $map.Contains($Second)
                    foreach($k in $map.Keys) { 'key:'+ $k.GetType().FullName + ':' + $k }
                } catch {
                    'error:'+ $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName + ':' + $_.CategoryInfo.Category + ':' + $_.InvocationInfo.OffsetInLine
                    'map:'+ $map
                } finally { 'trace:'+ ($trace -join ',') }
            }
            function Read-MixedKeyMap {
                [CmdletBinding()]param()
                $map=@{[char]'a'='character'; a='string'; [int]1='integer'; '1'='text'; [long]2='long'; [guid]'00000000-0000-0000-0000-000000000001'='guid'}
                foreach($key in [char]'a','A',1,'1',[long]2,[guid]'00000000-0000-0000-0000-000000000001') { $map[$key] }
                $map.Count
            }
            """, ".psm1");
        File.WriteAllText(fixture.ScriptPath, File.ReadAllText(fixture.ScriptPath), new System.Text.UTF8Encoding(true));
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeDictionaryKeys", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "ConvertTo-NormalizedString", "Request-ADComputersDisable", "Request-ADComputersMove", "Trace-DictionaryKeys", "Read-MixedKeyMap" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
        }
        const string probe = """
            $ErrorActionPreference='Stop'
            $global:UnsafeDictionaryProbeCalls=0
            $observations=[Collections.Generic.List[string]]::new()
            foreach($ordered in $false,$true) {
                foreach($keys in @(@('first','second'),@('first','FIRST'),@([char]'a','a'),@(1,'1'),@($null,'second'),@([long]1,1),@([psobject]'first','FIRST'),@('ß','ss'))) {
                    $rows=@(Trace-DictionaryKeys -Key $keys[0] -Second $keys[1] -Ordered $ordered)
                    # Preserve ordered-map enumeration; compare unordered-map rows as a multiset.
                    if($ordered) { $observations.Add($rows -join '|') }
                    else { $observations.Add(($rows | Sort-Object) -join '|') }
                }
            }
            $observations.Add((@(Read-MixedKeyMap) -join '|'))
            foreach($simplify in $false,$true) {
                foreach($input in 'café','äöüß ÖÜÄ','Przemysław Kłys and Helène','Æ×Þ°±ß…','ABC-abc-ČŠŽ-čšž','under_score','plain') {
                    foreach($repeat in 1,2) {
                        try { $observations.Add((@(ConvertTo-NormalizedString -String $input -Simplify:$simplify) -join '|')) }
                        catch { $observations.Add('normalize-error:'+ $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                    }
                }
                $observations.Add((@(('éèà','ùçä') | ConvertTo-NormalizedString -Simplify:$simplify) -join '|'))
            }
            foreach($repeat in 1,2) {
                $disabled=[pscustomobject]@{Name='disabled';Action='Disable'}
                $moved=[pscustomobject]@{Name='moved';Action='Move'}
                $excluded=[pscustomobject]@{Name='excluded';Action='None'}
                $report=@{'example.test'=@{Computers=@($disabled,$moved,$excluded);Server='offline'}}
                $processed=@{}
                foreach($ou in 'OU=Offline,DC=example,DC=test',@{'example.test'='OU=Offline,DC=example,DC=test'}) {
                    $observations.Add((@(Request-ADComputersDisable -Report $report -ReportOnly -DisableAndMove $true -DisableMoveTargetOrganizationalUnit $ou -ProcessedComputers $processed) | ConvertTo-Json -Compress))
                    $observations.Add((@(Request-ADComputersMove -Report $report -ReportOnly -TargetOrganizationalUnit $ou -ProcessedComputers $processed) | ConvertTo-Json -Compress))
                    if($processed.Count -ne 0) { throw 'Report-only mutated pending state' }
                }
            }
            if($global:UnsafeDictionaryProbeCalls -ne 0) { throw 'Report-only reached an administration boundary' }
            $observations | ConvertTo-Json -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("character|", generated);
        Assert.Contains("trace:first,second", generated);
        Assert.DoesNotContain("normalize-error", generated);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeDictionaryKeys_RuntimeFreeAdmissionRemainsClosed(string framework)
    {
        using var fixture = ArtifactFixture.Create("function Read-KeyMap { param([char]$Key); return @{$Key='value'} }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
            PowerShellCompilationMode.Strict, targetFramework: framework, capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeDictionaryKeys_PreserveRuntimeFreeStringKeyLookup(string framework)
    {
        using var fixture = ArtifactFixture.Create("function Read-StringKey { param([string]$Key); $map=@{$Key='value'}; return $map[$Key] }");
        var library = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: framework);
        Assert.Single(library.Methods);
        Assert.Null(Assert.Single(library.Methods).NativeFunctionBinding);
    }
}
