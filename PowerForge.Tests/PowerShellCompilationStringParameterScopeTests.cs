using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> StringParameterScopeHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { false, true }
            .Select(pipeline => configuration.Concat(new object[] { pipeline }).ToArray()));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StringParameterScopeHosts))]
    public void StringParameterScope_PreservesEarlierBindingsMutationAndFailureCleanup(string framework, string host, bool pipeline)
    {
        const string source = """
            function Read-BindingContext {
                [CmdletBinding()] param([string]$First,[string]$Value,[int]$Number)
                return "first=$First;value=$Value;number=$Number;verbose=$VerbosePreference"
            }
            function New-BindingValue {
                [CmdletBinding()] param([string]$Mode)
                $item=[pscustomobject]@{Mode=$Mode}
                Add-Member -InputObject $item -MemberType ScriptMethod -Name ToString -Force -Value {
                    $global:BindingTrace += "mode=$($this.Mode);first=$First"
                    if($this.Mode -eq 'WriteFirst') { Set-Variable -Scope 1 -Name First -Value 'changed' }
                    if($this.Mode -eq 'WriteFirstNumber') { Set-Variable -Scope 1 -Name First -Value 42 }
                    if($this.Mode -eq 'ClearFirstAttributes') { $variable=Get-Variable -Scope 1 -Name First; $variable.Attributes.Clear(); $variable.Value=42 }
                    $extra=''
                    if($this.Mode -eq 'SetFirstReadOnly') {
                        try { Set-Variable -Scope 1 -Name First -Option ReadOnly -ErrorAction Stop; $extra='options-set' }
                        catch { $extra='options-error:'+$_.FullyQualifiedErrorId }
                    }
                    if($this.Mode -eq 'WritePreference') { Set-Variable -Scope 1 -Name VerbosePreference -Value ([Management.Automation.ActionPreference]::Continue) }
                    if($this.Mode -eq 'Throw') { throw 'binding string failed' }
                    "seen=$First;$extra"
                }
                $item
            }
            """;
        const string probe = """
            $global:VerbosePreference='SilentlyContinue'
            $global:BindingTrace=@()
            foreach($mode in 'Read','WriteFirst','WriteFirstNumber','ClearFirstAttributes','SetFirstReadOnly','WritePreference','Throw') {
                $item=New-BindingValue -Mode $mode
                try { $records=@(Read-BindingContext -First 'before' -Value $item -Number 7 2>&1) }
                catch { $records=@($_) }
                foreach($record in $records) {
                    if($record -is [Management.Automation.ErrorRecord]) { 'error:'+$record.FullyQualifiedErrorId }
                    else { 'value:'+$record }
                }
                Read-BindingContext -First 'fresh' -Value 'plain' -Number 8
                'caller-verbose:'+$global:VerbosePreference
            }
            """;
        const string pipelineProbe = """
            'pipeline-start'
            $global:BindingTrace=@()
            @(
                [pscustomobject]@{First='one';Value=(New-BindingValue -Mode Read)}
                [pscustomobject]@{First='two';Value=(New-BindingValue -Mode WriteFirst)}
                [pscustomobject]@{First='two';Value=(New-BindingValue -Mode Read)}
            ) | Read-BindingContext -Number 9
            $global:BindingTrace
            'pipeline-end'
            """;
        var selectedSource = pipeline
            ? source.Replace("[string]$First,[string]$Value", "[Parameter(ValueFromPipelineByPropertyName)][string]$First,[Parameter(ValueFromPipelineByPropertyName)][string]$Value", StringComparison.Ordinal)
            : source;
        var selectedProbe = probe + Environment.NewLine + (pipeline ? pipelineProbe : string.Empty);
        using var fixture = ArtifactFixture.Create(selectedSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StringParameterScope", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        var unit = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, static item => item.Name == "Read-BindingContext");
        Assert.Equal(pipeline, unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + selectedProbe,
            fixture.RootPath, "original-string-parameter-scope");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + selectedProbe,
            fixture.RootPath, "compiled-string-parameter-scope");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
