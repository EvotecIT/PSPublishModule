using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StringParameterScope_BoundPreferencesOverrideConversionMutations(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-BindingPreferences {
                [CmdletBinding()] param([string]$First,[string]$Value)
                Microsoft.PowerShell.Utility\Write-Verbose 'binding-verbose'
                if($ErrorActionPreference -eq 'Stop') { return "stop=$Value;verbose=$VerbosePreference" }
                return "continue=$Value;verbose=$VerbosePreference"
            }
            function New-PreferenceValue {
                param([bool]$Stop,[bool]$Verbose)
                $item=[pscustomobject]@{Stop=$Stop;Verbose=$Verbose}
                Add-Member -InputObject $item -MemberType ScriptMethod -Name ToString -Force -Value {
                    $action=[Management.Automation.ActionPreference]::Continue
                    if($this.Stop) { $action=[Management.Automation.ActionPreference]::Stop }
                    Set-Variable -Scope 1 -Name ErrorActionPreference -Value $action
                    $action=[Management.Automation.ActionPreference]::SilentlyContinue
                    if($this.Verbose) { $action=[Management.Automation.ActionPreference]::Continue }
                    Set-Variable -Scope 1 -Name VerbosePreference -Value $action
                    'converted'
                }
                $item
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StringBindingPreferences", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 1,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        const string probe = """
            $global:ErrorActionPreference='Continue'
            $global:VerbosePreference='SilentlyContinue'
            foreach($case in @(
                @{Stop=$false;Verbose=$false;BoundAction='Stop';BoundVerbose=$true},
                @{Stop=$true;Verbose=$true;BoundAction='Continue';BoundVerbose=$false},
                @{Stop=$false;Verbose=$true;BoundAction='Continue';BoundVerbose=$false}
            )) {
                $item=New-PreferenceValue -Stop $case.Stop -Verbose $case.Verbose
                $records=[Collections.Generic.List[object]]::new()
                try {
                    Invoke-BindingPreferences -ErrorAction $case.BoundAction -First 'seed' -Verbose:$case.BoundVerbose -Value $item 4>&1 2>&1 |
                        ForEach-Object { $records.Add($_) }
                } catch { $records.Add($_) }
                foreach($record in $records) {
                    if($record -is [Management.Automation.ErrorRecord]) { 'error:'+$record.FullyQualifiedErrorId+';'+$record.Exception.Message }
                    elseif($record -is [Management.Automation.VerboseRecord]) { 'verbose:'+$record.Message }
                    else { 'value:'+$record }
                }
                'caller:'+$global:ErrorActionPreference+'/'+$global:VerbosePreference
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-binding-preferences");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-binding-preferences");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
