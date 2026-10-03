using PowerForge;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ObjectParameterValue_PreservesOfflineHelpersAndCallbackEffects(string framework, string host)
    {
        var add = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "Objects", "Add-ToHashTable.ps1");
        var body = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "Email", "Set-EmailBodyPreparedTable.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(add) + Environment.NewLine + File.ReadAllText(body) + """

            function Test-ParameterEquality {
                [CmdletBinding()]param($Value)
                [pscustomobject]@{eq=$Value -eq 'a';ceq=$Value -ceq 'a';ne=$Value -ne '';nullLeft=$null -eq $Value;nullRight=$Value -eq $null}
            }
            function Test-ParameterInterpolation {
                [CmdletBinding()]param($Value,$Trace)
                $marker='prior'
                try {$text="prefix:$Value";[pscustomobject]@{text=$text;marker=$marker}}
                catch {[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;marker=$marker}}
                finally {$Trace.Add('finally')}
            }
            function New-ParameterStringToken {
                param([string]$Mode)
                $token=[pscustomobject]@{Mode=$Mode}
                Add-Member -InputObject $token -MemberType ScriptMethod -Name ToString -Force -Value {
                    if($this.Mode -eq 'Write'){Set-Variable -Scope 1 -Name marker -Value changed}
                    if($this.Mode -eq 'Throw'){throw 'adapted stringify failed'}
                    "marker=$marker"
                }
                $token
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ObjectParameterValues",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var units = built.Manifest!.UnitDispositionLedger!.Entries;
        foreach (var name in new[] { "Add-ToHashTable", "Set-EmailBodyPreparedTable", "Test-ParameterEquality", "Test-ParameterInterpolation" })
        {
            var unit = Assert.Single(units, candidate => candidate.Name == name);
            Assert.True(unit.EmittedClrMethod, unit.Name);
            Assert.True(unit.UsesNativeFunctionBinding, unit.Name);
            Assert.False(unit.RetainedHostedSource, unit.Name);
        }
        const string probe = """
            Add-Type -TypeDefinition @'
            public sealed class ParameterStringCallback {
                public static System.Collections.Generic.List<string> Trace;
                public static System.Func<string> Read;
                public bool Fail;
                public override string ToString() {
                    Trace.Add("stringify");
                    if(Fail) throw new System.InvalidOperationException("parameter stringify failed");
                    return Read == null ? "callback" : Read();
                }
            }
            '@
            $samples=@(@{v=$null},@{v=''},@{v='a'},@{v='A'},@{v=0},@{v=7},@{v=@()},@{v=@('a','A','')},@{v=[pscustomobject]@{Name='note'}})
            foreach($sample in $samples) {
                foreach($loose in $false,$true) {
                    $map=@{};$records=@();$caught=$null
                    try {
                        if($loose){$records=@(Add-ToHashTable -Hashtable $map -Key 'key' -Value $sample.v -ErrorAction Stop -Verbose)}
                        else{$records=@(Add-ToHashTable -Hashtable $map -Key 'key' -Value $sample.v)}
                    }catch{$caught=$_.FullyQualifiedErrorId}
                    [pscustomobject]@{kind='add';loose=$loose;input=$sample.v;records=$records;hasKey=$map.ContainsKey('key');same=$(if($map.ContainsKey('key')){[object]::ReferenceEquals($map['key'],$sample.v)});value=$map['key'];caught=$caught}|ConvertTo-Json -Depth 8 -Compress
                }
                [pscustomobject]@{kind='equality';input=$sample.v;records=@(Test-ParameterEquality -Value $sample.v)}|ConvertTo-Json -Depth 8 -Compress
            }
            foreach($bad in 'duplicate','null-map') {
                $map=if($bad -eq 'duplicate'){@{key='prior'}}else{$null}
                $caught=$null
                try{Add-ToHashTable -Hashtable $map -Key 'key' -Value 'new'}catch{$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                [pscustomobject]@{kind='add-failure';mode=$bad;map=$map;caught=$caught}|ConvertTo-Json -Depth 8 -Compress
            }
            foreach($ofs in ' ','|') {
                $OFS=$ofs
                foreach($sample in $samples) {
                    foreach($loose in $false,$true) {
                        if($loose){$records=@(Set-EmailBodyPreparedTable -TableData $sample.v -TableWelcomeMessage $sample.v -ErrorAction Stop -Verbose)}
                        else{$records=@(Set-EmailBodyPreparedTable -TableData $sample.v -TableWelcomeMessage $sample.v)}
                        [pscustomobject]@{kind='body';ofs=$ofs;loose=$loose;input=$sample.v;records=$records;types=@($records|ForEach-Object{$_.GetType().FullName})}|ConvertTo-Json -Depth 8 -Compress
                    }
                }
            }
            foreach($fail in $false,$true) {
                $trace=[Collections.Generic.List[string]]::new();[ParameterStringCallback]::Trace=$trace
                [ParameterStringCallback]::Read=[Func[string]]{Set-Variable -Name marker -Value changed -Scope 1;return $marker}
                $value=[ParameterStringCallback]::new();$value.Fail=$fail
                $records=@(Test-ParameterInterpolation -Value $value -Trace $trace -ErrorAction Stop)
                [pscustomobject]@{kind='callback';fail=$fail;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 8 -Compress
            }
            foreach($mode in 'Read','Write','Throw') {
                $trace=[Collections.Generic.List[string]]::new()
                $value=New-ParameterStringToken -Mode $mode
                $records=@(Test-ParameterInterpolation -Value $value -Trace $trace -ErrorAction Stop)
                [pscustomobject]@{kind='adapted-callback';mode=$mode;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 8 -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(70, original.Length);
        Assert.Equal(original.Length, generated.Length);
        for (var index = 0; index < original.Length; index++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original[index]), JsonNode.Parse(generated[index])),
                "Original: " + original[index] + Environment.NewLine + "Generated: " + generated[index]);
        Assert.Contains("changed", generated[65]);
        Assert.Equal("prior", JsonNode.Parse(generated[65])!["records"]![0]!["marker"]!.GetValue<string>());
        Assert.Equal("changed", JsonNode.Parse(generated[^2])!["records"]![0]!["marker"]!.GetValue<string>());
        Assert.Contains("a|A|", generated[61]);
        Assert.Contains("finally", generated[^1]);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void ObjectParameterValue_KeepsRuntimeFreeAndNonDirectShapesClosed(string framework)
    {
        var document = PowerShellSourceParser.Parse("""
            function Get-Equality {param($Value);$Value -eq 'a'}
            function Get-Interpolation {param($Value);"prefix:$Value"}
            function Get-Nested {param($Value);$Value.Name -eq 'a'}
            """, Path.Combine(Path.GetTempPath(), "object-parameter-value.ps1"));
        var pipeline = new PowerShellSemanticCompilationPipeline();
        var strict = pipeline.Compile(new[] {document}, framework, PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);
        var hybrid = pipeline.Compile(new[] {document}, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(hybrid.Emitted.Methods, method => method.GeneratedName == "Get_Nested" && method.NativeFunctionBinding is not null);
    }
}
