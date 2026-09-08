using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ModulePreferences_ShadowAllScopeAndReadOnlyValuesDuringCommonParameterBinding(string framework, string host)
    {
        const string sourceTemplate = """
            foreach($name in 'VerbosePreference','DebugPreference','WarningPreference','InformationPreference','ErrorActionPreference') {
                Microsoft.PowerShell.Utility\Set-Variable -Scope Script -Name $name -Value ([Management.Automation.ActionPreference]::SilentlyContinue) -Option OPTIONS
            }
            Microsoft.PowerShell.Utility\Set-Variable -Scope Script -Name WhatIfPreference -Value $false -Option OPTIONS
            Microsoft.PowerShell.Utility\Set-Variable -Scope Script -Name ConfirmPreference -Value ([Management.Automation.ConfirmImpact]::High) -Option OPTIONS
            function Read-OwnerPreferences {
                [CmdletBinding(SupportsShouldProcess=$true)] param()
                return @($VerbosePreference,$DebugPreference,$WarningPreference,$InformationPreference,$ErrorActionPreference,$WhatIfPreference,$ConfirmPreference)
            }
            """;
        const string probe = """
            foreach($phase in 'before','bound','after') {
                $parameters=@{}
                if($phase -eq 'bound') { $parameters=@{Verbose=$true;Debug=$true;WarningAction='Continue';InformationAction='Continue';ErrorAction='Continue';WhatIf=$true;Confirm=$false} }
                $values=@(Read-OwnerPreferences @parameters)
                $records=@(foreach($value in $values) { [pscustomobject]@{type=$value.GetType().FullName;value=[string]$value} })
                [pscustomobject]@{phase=$phase;records=$records} | ConvertTo-Json -Depth 4 -Compress
            }
            """;
        foreach (var options in new[] { "AllScope", "AllScope,ReadOnly" })
        {
            using var fixture = ArtifactFixture.Create(sourceTemplate.Replace("OPTIONS", options, StringComparison.Ordinal), ".psm1");
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath, fixture.OutputPath, "Generated.ModuleAllScopePreferences", PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.Equal(1, result.Manifest!.CompiledMethods);
            var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
                fixture.RootPath, "original-all-scope-preferences");
            var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
                fixture.RootPath, "compiled-all-scope-preferences");
            Assert.True(original.ExitCode == 0, options + ": " + original.StandardOutput + original.StandardError);
            Assert.True(compiled.ExitCode == 0, options + ": " + compiled.StandardOutput + compiled.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(original.StandardError), options + ": " + original.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), options + ": " + compiled.StandardError);
            Assert.True(original.StandardOutput == compiled.StandardOutput,
                options + ": Original:\n" + original.StandardOutput + "Compiled:\n" + compiled.StandardOutput);
        }
    }
}
