using PowerForge;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StaticVariableMember_PreservesRightsMappingAndRuntimeTypeIdentity(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Convert-GenericRightsToFileSystemRights.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Read-StaticVariable {
                [CmdletBinding()]param($Type,$Trace)
                try {
                    $Trace.Add('before')
                    $value=$Type::MaxValue
                    $Trace.Add('after')
                    [pscustomobject]@{value=$value;type=$(if($null -ne $value){$value.GetType().FullName})}
                } catch {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                } finally {$Trace.Add('finally')}
            }
            function Read-ReboundStaticVariable {
                $type=[int];$first=$type::MaxValue
                $type=[byte];$second=$type::MaxValue
                "$first|$second"
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StaticVariableMembers",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(3, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Name != "<script>"), unit =>
        {
            Assert.True(unit.UsesNativeFunctionBinding, unit.Name);
            Assert.False(unit.RetainedHostedSource, unit.Name);
        });
        const string probe = """
            foreach($bits in 0,1,0x80000000,0x40000000,0x20000000,0x10000000,0xE0000000,0xF0000001,-1) {
                $rights=[Enum]::ToObject([System.Security.AccessControl.FileSystemRights],[int]$bits)
                for($round=0;$round -lt 2;$round++) {
                    $records=@(Convert-GenericRightsToFileSystemRights -OriginalRights $rights -ErrorAction Stop)
                    [pscustomobject]@{kind='rights';input=[int]$rights;round=$round;count=$records.Count;values=@($records|ForEach-Object{if($null -eq $_){$null}else{[pscustomobject]@{type=$_.GetType().FullName;bits=[int]$_}}})}|ConvertTo-Json -Depth 8 -Compress
                }
            }
            foreach($strict in 0,2) {
                if($strict -eq 0){Set-StrictMode -Off}else{Set-StrictMode -Version $strict}
                foreach($type in [int],[byte],[datetime],[string],$null,'System.Int32',7) {
                    $trace=[Collections.Generic.List[string]]::new()
                    $records=@(Read-StaticVariable -Type $type -Trace $trace -ErrorAction Stop)
                    [pscustomobject]@{kind='static';strict=$strict;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 8 -Compress
                }
                [pscustomobject]@{kind='rebound';strict=$strict;records=@(Read-ReboundStaticVariable)}|ConvertTo-Json -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(34, original.Length);
        Assert.Equal(original.Length, generated.Length);
        for (var index = 0; index < original.Length; index++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original[index]), JsonNode.Parse(generated[index])),
                "Original: " + original[index] + Environment.NewLine + "Generated: " + generated[index]);
        Assert.All(generated.Take(16), row => Assert.Equal("System.Security.AccessControl.FileSystemRights",
            JsonNode.Parse(row)!["values"]![0]!["type"]!.GetValue<string>()));
        Assert.Contains("2147483647|255", generated[^1]);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void StaticVariableMember_RuntimeFreeStrictRemainsClosed(string framework)
    {
        var document = PowerShellSourceParser.Parse(
            "function Read-StaticVariable {$type=[int];$type::MaxValue}",
            Path.Combine(Path.GetTempPath(), "static-variable-strict.ps1"));
        Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework,
            PowerShellCompilationCapabilities.TypedExecutable).Emitted.Methods);
    }
}
