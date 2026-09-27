namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HostedEnumValues_PreserveFlagsDefaultsAndModuleIsolation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            [Flags()] enum NativeChoice { Zero = 0; One = 1; Two = 2 }
            function Read-Choice { param([NativeChoice]$Value=[NativeChoice]::One) $Value.HasFlag([NativeChoice]::One) }
            function Read-Flag { [NativeChoice]::Two }
            Export-ModuleMember -Function Read-Choice,Read-Flag
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeEnumValues", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        const string probe = """
            foreach($round in 1,2,3) {
                Import-Module $modulePath -Force
                $module=Get-Module|Where-Object {$_.Path -eq $modulePath}
                $declared=@($module.Invoke({[NativeChoice]}))[0]
                $value=Read-Flag
                $other=New-Module -Name OtherChoice -ScriptBlock { enum NativeChoice { Other = 9 }; function OtherValue { [NativeChoice]::Other } }
                $otherValue=@($other.Invoke({OtherValue}))[0]
                $failures=@(foreach($bad in @('Missing',7,$null)) {try {Read-Choice $bad} catch {$_.FullyQualifiedErrorId+'|'+$_.Exception.GetType().FullName}})
                [pscustomobject]@{round=$round;flags=@(Read-Choice;Read-Choice 'One, Two';Read-Choice 'Two';Read-Choice 3 extra);name=[string]$value;number=[int]$value;sameType=($declared -eq $value.GetType());isolated=($value.GetType() -ne $otherValue.GetType());flagsAttribute=$declared.IsDefined([FlagsAttribute],$false);failures=$failures}|ConvertTo-Json -Depth 6 -Compress
                Remove-Module $module
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, "$modulePath='" + EscapeStatementErrorPath(built.ArtifactPath!) + "'; " + probe, host);
        Assert.True(original == generated, "Original:" + original + Environment.NewLine + "Generated:" + generated);
        Assert.Contains("\"sameType\":true", generated);
        Assert.Contains("\"isolated\":true", generated);
        Assert.Contains("\"flagsAttribute\":true", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HostedEnumParameters_PreserveIdentityDefaultsArrayBindingAndRepeatedImports(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            enum NativeChoice { Zero; One = 1; Two = 2 }
            function Read-Choice { param([NativeChoice]$Value='One') $Value }
            function Read-Choices {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][NativeChoice[]]$Value='Two')
                process {$Value}
            }
            Export-ModuleMember -Function Read-Choice,Read-Choices
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeEnumMetadata", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        const string probe = """
            foreach($round in 1,2,3) {
                Import-Module $modulePath -Force
                $metadata=(Get-Command Read-Choice).Parameters['Value'].ParameterType
                $rows=@(Read-Choice;Read-Choice 'two';Read-Choice 1 extra;Read-Choices;Read-Choices -Value @('Zero',2);'One','Two'|Read-Choices)
                $values=@($rows|ForEach-Object {[pscustomobject]@{name=[string]$_;numeric=[int]$_;type=$_.GetType().FullName;sameType=($_.GetType() -eq $metadata)}})
                $failures=@(foreach($bad in @('Missing',7,$null)) {try {Read-Choice -Value $bad} catch {$_.FullyQualifiedErrorId+'|'+$_.Exception.GetType().FullName}})
                [pscustomobject]@{round=$round;values=$values;failures=$failures;arrayType=(Get-Command Read-Choices).Parameters['Value'].ParameterType.FullName}|ConvertTo-Json -Depth 6 -Compress
                Remove-Module (Get-Module|Where-Object {$_.Path -eq $modulePath})
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, "$modulePath='" + EscapeStatementErrorPath(built.ArtifactPath!) + "'; " + probe, host);
        Assert.True(original == generated, "Original:" + original + Environment.NewLine + "Generated:" + generated);
        Assert.Contains("NativeChoice[]", generated);
        Assert.Contains("ParameterArgumentTransformationError", generated);
        Assert.DoesNotContain("\"sameType\":false", generated);
    }
}
