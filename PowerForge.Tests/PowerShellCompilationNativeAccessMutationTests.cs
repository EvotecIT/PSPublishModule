using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void NativeAccessMutations_DoNotWidenRuntimeFreeAdmission()
    {
        var document = PowerShellSourceParser.Parse(
            "function Set-Value { param([object]$Map) $Map['value']++ }",
            Path.Combine(Path.GetTempPath(), "access-mutation-strict.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeAccessMutations_PreserveValuesErrorsAndTargetEvaluation(string framework, string host)
    {
        const string source = """
            function Invoke-AccessMutation {
                [CmdletBinding()] param([object]$Target,[object]$Selector,[string]$Mode)
                try {
                    if($Mode -eq 'member-post-plus') { $result=$Target.Value++ }
                    elseif($Mode -eq 'member-pre-plus') { $result=++$Target.Value }
                    elseif($Mode -eq 'member-post-minus') { $result=$Target.Value-- }
                    elseif($Mode -eq 'member-pre-minus') { $result=--$Target.Value }
                    elseif($Mode -eq 'index-post-plus') { $result=$Target[$Selector.Key]++ }
                    elseif($Mode -eq 'index-pre-plus') { $result=++$Target[$Selector.Key] }
                    elseif($Mode -eq 'index-post-minus') { $result=$Target[$Selector.Key]-- }
                    elseif($Mode -eq 'index-pre-minus') { $result=--$Target[$Selector.Key] }
                    elseif($Mode -eq 'nested-index') { $result=$Target['Value'][$Selector.Key]++ }
                    else { $Target.Value++; $Target['Value']--; $result='standalone' }
                    [pscustomobject]@{result=$result;resultType=if($null -eq $result){'null'}else{$result.GetType().FullName}}
                } catch {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}
                }
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.AccessMutations",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(1, built.Manifest!.CompiledMethods);
        const string probe = """
            Add-Type -TypeDefinition 'public class NativeMutationBox { private object stored; public string Trace=""; public NativeMutationBox(object value){stored=value;} public object Value {get{Trace+="get;";return stored;}set{Trace+="set;";stored=value;}} public object this[string key] {get{Trace+="index-get;";return stored;}set{Trace+="index-set;";stored=value;}} }'
            $global:mutationKeyReads=0
            $selector=[pscustomobject]@{}
            $selector|Add-Member ScriptProperty Key { $global:mutationKeyReads++; 'Value' }
            foreach($mode in 'member-post-plus','member-pre-plus','member-post-minus','member-pre-minus',
                'index-post-plus','index-pre-plus','index-post-minus','index-pre-minus','nested-index','standalone') {
                foreach($value in $null,0,1,[int]::MaxValue,[long]::MaxValue,1.5,'2','invalid') {
                    $target=@{Value=$value}
                    if($mode -eq 'nested-index'){$target=@{Value=@{Value=$value}}}
                    $alias=$target
                    $global:mutationKeyReads=0
                    $output=@(Invoke-AccessMutation -Target $target -Selector $selector -Mode $mode)
                    [pscustomobject]@{mode=$mode;input=$value;output=$output;stored=$target.Value
                        storedType=if($null -eq $target.Value){'null'}else{$target.Value.GetType().FullName}
                        keyReads=$global:mutationKeyReads;identity=[object]::ReferenceEquals($target,$alias)
                    }|ConvertTo-Json -Compress -Depth 8
                }
            }
            foreach($kind in 'clr','script-property') {
                foreach($mode in 'member-post-plus','member-pre-plus','member-post-minus','member-pre-minus',
                    'index-post-plus','index-pre-plus','index-post-minus','index-pre-minus') {
                    if($kind -eq 'script-property' -and $mode.StartsWith('index')){continue}
                    foreach($value in $null,0,[int]::MaxValue,'2','invalid') {
                        $global:mutationGets=0;$global:mutationSets=0;$global:mutationKeyReads=0
                        if($kind -eq 'clr'){$target=[NativeMutationBox]::new($value)}
                        else {
                            $target=[pscustomobject]@{Stored=$value}
                            $target|Add-Member ScriptProperty Value { $global:mutationGets++;$this.Stored } {
                                param($next) $global:mutationSets++;$this.Stored=$next
                            }
                        }
                        $output=@(Invoke-AccessMutation -Target $target -Selector $selector -Mode $mode)
                        $stored=$target.Value
                        [pscustomobject]@{kind=$kind;mode=$mode;input=$value;output=$output;stored=$stored
                            trace=$target.Trace;gets=$global:mutationGets;sets=$global:mutationSets
                            keyReads=$global:mutationKeyReads
                        }|ConvertTo-Json -Compress -Depth 8
                    }
                }
            }
            """;
        Assert.Equal(RunModuleProof(fixture.ScriptPath, probe, host),
            RunModuleProof(built.ArtifactPath!, probe, host));
    }
}
