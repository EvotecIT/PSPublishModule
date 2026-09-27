namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeReferenceCells_PreserveInstanceCallsAndOptimizedOrMixedStorage(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-InstanceReferences {
                [CmdletBinding()]param([object]$Target,[object]$Alternate,[string]$Mode='ok')
                [int]$number=7;[string]$text='before';$owner=$Target
                try {
                    $success=$Target.Update($($Target=$Alternate;$Mode),[ref]$number,[ref]$text)
                    if($Mode -eq 'retained'){$owner.SavedNumber.Value=64;$owner.SavedText.Value='later'}
                    [pscustomobject]@{success=$success;number=$number;text=$text}
                } catch {
                    [pscustomobject]@{error=$_.FullyQualifiedErrorId;exception=$_.Exception.GetType().FullName;message=$_.Exception.Message;number=$number;text=$text;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }
            }
            function Read-MixedReferences {
                [CmdletBinding()]param([object]$Target,[string]$Mode='ok')
                [int]$number=7;$text=1
                if($Mode -eq 'typed'){[datetime]$text=[datetime]::MinValue}
                try {$success=$Target.Update($Mode,[ref]$number,[ref]$text);[pscustomobject]@{success=$success;number=$number;text=$text}}
                catch {[pscustomobject]@{error=$_.FullyQualifiedErrorId;exception=$_.Exception.GetType().FullName;message=$_.Exception.Message;number=$number;text=$text;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeInstanceReferences", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        const string probe = """
            Add-Type 'public sealed class OwnedInstanceReferenceProbe { private readonly int seed; public OwnedInstanceReferenceProbe(int seed) {this.seed=seed;} public bool Update(string mode,out int number,out string text) {number=seed;text="updated";if(mode=="throw")throw new System.InvalidOperationException("owned failure");return true;} }'
            $target=[OwnedInstanceReferenceProbe]::new(42);$alternate=[OwnedInstanceReferenceProbe]::new(99)
            foreach($mode in 'ok','throw','typed') {
                foreach($action in 'Continue','Stop') {
                    Read-InstanceReferences -Target $target -Alternate $alternate -Mode $mode -ErrorAction $action | ConvertTo-Json -Depth 8 -Compress
                    Read-MixedReferences -Target $target -Mode $mode -ErrorAction $action | ConvertTo-Json -Depth 8 -Compress
                }
            }
            Read-InstanceReferences -Target $null -Alternate $alternate | ConvertTo-Json -Depth 8 -Compress
            $scriptTarget=[pscustomobject]@{}
            $scriptTarget|Add-Member ScriptMethod Update {param($mode,$number,$text);$this|Add-Member NoteProperty SavedNumber $number -Force;$this|Add-Member NoteProperty SavedText $text -Force;$number.Value=23;$text.Value='script';$true}
            Read-InstanceReferences -Target $scriptTarget -Alternate $alternate | ConvertTo-Json -Depth 8 -Compress
            Read-InstanceReferences -Target $scriptTarget -Alternate $alternate -Mode retained | ConvertTo-Json -Depth 8 -Compress
            $scriptTarget.SavedNumber.Value=65
            [pscustomobject]@{retainedAfterReturn=$scriptTarget.SavedNumber.Value;text=$scriptTarget.SavedText.Value}|ConvertTo-Json -Depth 8 -Compress
            $module=(Get-Command Read-InstanceReferences).Module
            & $module {New-Variable -Name number -Scope Script -Value 13 -Option AllScope}
            try {
                Read-InstanceReferences -Target $target -Alternate $alternate | ConvertTo-Json -Depth 8 -Compress
                [pscustomobject]@{allScopeNumber=(& $module {$script:number})}|ConvertTo-Json -Depth 8 -Compress
            } finally {& $module {Remove-Variable -Name number -Scope Script -Force}}
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original:" + original + Environment.NewLine + "Generated:" + generated);
        Assert.Contains("\"number\":42", generated);
        Assert.Contains("\"number\":23", generated);
        Assert.Contains("\"number\":64", generated);
        Assert.Contains("\"retainedAfterReturn\":65", generated);
        Assert.Contains("\"allScopeNumber\":42", generated);
        Assert.Contains("owned failure", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeReferenceCells_PreserveUnoptimizedStorageAndRuntimeStaticTargets(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Reference {
                [CmdletBinding()]param([Type]$Target,[string]$Text,[string]$Mode='normal')
                $number=7
                if($Mode -eq 'typed'){[datetime]$number=[datetime]::MinValue}
                if($Mode -eq 'missing'){Remove-Variable number}
                try {
                    $success=$Target::TryParse($Text,[ref]$number)
                    [pscustomobject]@{success=$success;value=$number;type=if($null -eq $number){'<null>'}else{$number.GetType().FullName}}
                } catch {
                    [pscustomobject]@{error=$_.FullyQualifiedErrorId;exception=$_.Exception.GetType().FullName;value=$number;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }
            }
            function Read-ReferenceOrder {
                [CmdletBinding()]param()
                $target=[int];$number=7
                if($false){[datetime]$number=[datetime]::MinValue}
                $success=$target::TryParse($($target=[bool];'42'),[ref]$number)
                "$success|$number|$target"
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeReferenceCells", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        const string probe = """
            Add-Type 'public static class OwnedReferenceProbe { public static bool TryParse(string text, out int number) {number=99;if(text=="throw")throw new System.InvalidOperationException("owned failure");return true;} }'
            foreach($target in [int],[long],[bool],[decimal],[double],[version]) {
                foreach($text in '42','bad','') {
                    [pscustomobject]@{target=$target.FullName;text=$text;records=@(Read-Reference $target $text)}|ConvertTo-Json -Depth 8 -Compress
                }
            }
            foreach($mode in 'missing','typed') {Read-Reference ([int]) '42' $mode|ConvertTo-Json -Depth 8 -Compress}
            foreach($text in 'success','throw') {Read-Reference ([OwnedReferenceProbe]) $text|ConvertTo-Json -Depth 8 -Compress}
            Read-ReferenceOrder
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original:" + original + Environment.NewLine + "Generated:" + generated);
        Assert.Contains("NonExistingVariableReference", generated);
        Assert.Contains("True|42|bool", generated);
    }
}
