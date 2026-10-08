namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HostedDataClasses_PreserveConstructionMutationAndModuleIdentity(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            class OwnedValue { [string]$Name='original'; [long]$Count; [string]$Computer=[Environment]::MachineName }
            function New-Value {
                param([string]$Name='default',[object]$Count=2)
                $value=[OwnedValue]::new()
                $value.Name=$Name
                $value.Count=$Count
                $value
            }
            function New-NestedValue { process { function Child { process { [OwnedValue]::new() } }; Child } }
            Export-ModuleMember -Function New-Value,New-NestedValue
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeDataClass", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        const string probe = """
            foreach($round in 1,2,3) {
                Import-Module $modulePath -Force
                $module=Get-Module|Where-Object {$_.Path -eq $modulePath}
                $declared=@($module.Invoke({[OwnedValue]}))[0]
                $rows=@(New-Value;New-Value 'named' '9';New-NestedValue)
                $values=@($rows|ForEach-Object {[pscustomobject]@{name=$_.Name;count=$_.Count;computer=$_.Computer;type=$_.GetType().FullName;sameType=($_.GetType() -eq $declared)}})
                $failure=@(New-Value -Count 'bad' -ErrorAction Continue 2>&1|ForEach-Object {if($_ -is [System.Management.Automation.ErrorRecord]) {$_.FullyQualifiedErrorId+'|'+$_.Exception.GetType().FullName} else {$_.Count}})
                $other=New-Module -Name OtherScope -ScriptBlock { class OwnedValue {[string]$Name='other'}; function OtherValue { [OwnedValue]::new() } }
                $otherValue=@($other.Invoke({OtherValue}))[0]
                $own=New-Value
                [pscustomobject]@{round=$round;values=$values;failure=$failure;isolated=($own.GetType() -ne $otherValue.GetType());unchanged=$own.Name}|ConvertTo-Json -Depth 6 -Compress
                Remove-Module $module
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, "$modulePath='" + EscapeStatementErrorPath(built.ArtifactPath!) + "'; " + probe, host);
        Assert.True(original == generated, "Original:" + original + Environment.NewLine + "Generated:" + generated);
        Assert.Contains("\"isolated\":true", generated);
        Assert.DoesNotContain("\"sameType\":false", generated);
    }
}
