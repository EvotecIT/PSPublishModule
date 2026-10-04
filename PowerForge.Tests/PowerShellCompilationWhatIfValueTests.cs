using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void WhatIfValue_PreservesRawValuesAndBooleanConversion(string framework, string host)
    {
        const string source = """
            function Read-RawWhatIf { [CmdletBinding(SupportsShouldProcess=$true)] param() return $WhatIfPreference }
            function Read-AliasedWhatIf { [CmdletBinding(SupportsShouldProcess=$true)] param() $value=$WhatIfPreference; return $value }
            function Read-BooleanWhatIf { [CmdletBinding(SupportsShouldProcess=$true)] param() return [bool]$WhatIfPreference }
            function Read-ConditionalWhatIf { [CmdletBinding(SupportsShouldProcess=$true)] param() if($WhatIfPreference) { return 'yes' }; return 'no' }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.WhatIfValues", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.All(result.Manifest.PublicAbi!.Methods, method =>
        {
            var preference = Assert.Single(method.Parameters, parameter => parameter.CompilerPurpose == "WhatIfPreference");
            Assert.Equal("System.Object", preference.TypeName);
            Assert.True(preference.Nullable);
        });
        const string probe = """
            foreach($shape in 'False','True','String','Null','EmptyArray','Array') {
                $global:WhatIfPreference=switch($shape) {
                    'False' { $false } 'True' { $true } 'String' { 'custom' } 'Null' { $null }
                    'EmptyArray' { ,([object[]]@()) } 'Array' { ,([object[]]@(0,1)) }
                }
                foreach($binding in 'Inherited','False','True') {
                    $parameters=@{}
                    if($binding -ne 'Inherited') { $parameters.WhatIf=[switch]($binding -eq 'True') }
                    foreach($name in 'Read-RawWhatIf','Read-AliasedWhatIf','Read-BooleanWhatIf','Read-ConditionalWhatIf') {
                        $values=@(& $name @parameters)
                        $records=@(foreach($value in $values) {
                            if($null -eq $value) { [pscustomobject]@{type='null';value=$null} }
                            else { [pscustomobject]@{type=$value.GetType().FullName;value=$value} }
                        })
                        [pscustomobject]@{shape=$shape;binding=$binding;name=$name;records=$records} | ConvertTo-Json -Compress -Depth 5
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-whatif-values");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-whatif-values");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("System.Management.Automation.SwitchParameter", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
