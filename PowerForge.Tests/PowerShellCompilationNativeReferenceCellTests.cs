namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
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
