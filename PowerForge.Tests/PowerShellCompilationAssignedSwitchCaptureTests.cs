using PowerForge;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void AssignedSwitchCapture_PreservesOfflineDictionaryCopyAndTransfers(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Objects", "Copy-DictionaryManual.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Test-SwitchCapture {
                [CmdletBinding()]param($Items,$Trace)
                $_='prior-item';$switch='prior-switch';$value='prior-value'
                try {
                    $value = switch ($Items) {
                        'skip' {$Trace.Add('skip');continue}
                        'break' {$Trace.Add('break');'before-break';break}
                        'fail' {$Trace.Add('fail');'partial';throw 'switch failure'}
                        'return' {$Trace.Add('return');return 'returned'}
                        {$_ -is [int]} {$Trace.Add('integer');$_;continue}
                        default {$Trace.Add('other');$_}
                    }
                    'after'
                } catch {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                } finally {$Trace.Add('finally')}
                [pscustomobject]@{value=@($value);isNull=$null -eq $value;type=$(if($null -ne $value){$value.GetType().FullName});item=$PSItem;switchState=$switch}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.AssignedSwitchCapture", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.True(built.Manifest!.CompiledMethods == 2, string.Join(Environment.NewLine,
            built.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            function Get-OfflineProjection($Value) {
                if($null -eq $Value){return @{type='null'}}
                if($Value -is [Collections.IDictionary]) {
                    $entries=@(foreach($key in $Value.Keys){[pscustomobject]@{key=$key;value=(Get-OfflineProjection $Value[$key])}})
                    return [pscustomobject]@{type=$Value.GetType().FullName;entries=$entries}
                }
                if($Value -is [Collections.IList]) {
                    return [pscustomobject]@{type=$Value.GetType().FullName;items=@(foreach($item in $Value){Get-OfflineProjection $item})}
                }
                [pscustomobject]@{type=$Value.GetType().FullName;value=$Value}
            }
            $list=[Collections.Generic.List[object]]::new();$list.Add([ordered]@{X=7});$list.Add('tail')
            $samples=@(
                $null,
                [ordered]@{},
                [ordered]@{Number=7;Text='text';NullValue=$null},
                [ordered]@{Empty=@();One=@(7);Many=@(1,2)},
                [ordered]@{Nested=[ordered]@{Child=[ordered]@{Text='inner'}}},
                [ordered]@{Object=[pscustomobject]@{Name='object';Count=2};List=$list;Date=[datetime]'2026-01-02T03:04:05Z'}
            )
            foreach($sample in $samples) {
                $before=Get-OfflineProjection $sample
                $records=@(Copy-DictionaryManual -Dictionary $sample -ErrorAction Stop)
                $copy=$records[0]
                [pscustomobject]@{kind='copy';count=$records.Count;before=$before;after=(Get-OfflineProjection $sample);copy=(Get-OfflineProjection $copy);sameRoot=[object]::ReferenceEquals($sample,$copy);sameNested=$(if($sample -and $sample.Contains('Nested')){[object]::ReferenceEquals($sample['Nested'],$copy['Nested'])})}|ConvertTo-Json -Depth 20 -Compress
            }
            $cases=@($null,1,@(1,2),@('skip',2),@('break',2),@('fail',2),@('return',2),@('text','skip',3))
            foreach($case in $cases) {
                $trace=[Collections.Generic.List[string]]::new()
                $result=@(Test-SwitchCapture -Items $case -Trace $trace -ErrorAction Stop)
                [pscustomobject]@{kind='capture';input=@($case);result=$result;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 10 -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(14, original.Length);
        Assert.Equal(original.Length, generated.Length);
        for (var index = 0; index < original.Length; index++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original[index]), JsonNode.Parse(generated[index])),
                "Original: " + original[index] + Environment.NewLine + "Generated: " + generated[index]);
        Assert.All(generated.Take(6), row => Assert.False(JsonNode.Parse(row)!["sameRoot"]!.GetValue<bool>()));
        Assert.Contains("prior-value", generated[11]);
        Assert.Contains("returned", generated[12]);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void AssignedSwitchCapture_RuntimeFreeRemainsClosedAndLocalLoopTransfersQualify(string framework)
    {
        var strict = PowerShellSourceParser.Parse(
            "function Test-StrictCapture {param([string]$Value);$result=switch($Value){'x'{1}default{2}};return $result}",
            Path.Combine(Path.GetTempPath(), "assigned-switch-strict.ps1"));
        Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { strict }, framework,
            PowerShellCompilationCapabilities.TypedExecutable).Emitted.Methods);
        using var fixture = ArtifactFixture.Create(
            "function Test-Escape {[CmdletBinding()]param($Value);:outer foreach($item in $Value){$result=switch($item){'x'{break outer}default{2}};$result}}");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Hybrid, targetFramework: framework,
            capabilities: PowerShellCompilationCapabilities.HybridModule));
        Assert.All(plan.Files.SelectMany(file => file.Units), unit => Assert.True(unit.IsCompilable));
    }
}
