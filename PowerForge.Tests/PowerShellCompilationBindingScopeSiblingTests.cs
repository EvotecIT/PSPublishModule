using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> BindingScopeSiblingHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { "lifecycle", "automatic", "validated" }
            .Select(shape => configuration.Concat(new object[] { shape }).ToArray()));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(BindingScopeSiblingHosts))]
    public void StringBindingScope_PreservesNativeLifecycleAutomaticAndValidatedStorage(string framework, string host, string shape)
    {
        const string body = "\"first=$First;value=$Value;verbose=$VerbosePreference\"";
        var parameters = shape == "validated" ? "[ValidatePattern('.*')][string]$First,[string]$Value" : "[string]$First,[string]$Value";
        var clause = shape == "lifecycle" ? "begin { " + body + " }" : "return " + body;
        var source = "function Read-SiblingBinding { [CmdletBinding()] param(" + parameters + ") " + clause + " }" + Environment.NewLine + """
            function New-SiblingValue {
                param([string]$Mode)
                $item=[pscustomobject]@{Mode=$Mode}
                Add-Member -InputObject $item -MemberType ScriptMethod -Name ToString -Force -Value {
                    if($this.Mode -eq 'WriteFirst') { Set-Variable -Scope 1 -Name First -Value 'changed' }
                    if($this.Mode -eq 'WritePreference') { Set-Variable -Scope 1 -Name VerbosePreference -Value 'Continue' }
                    if($this.Mode -eq 'ClearAttributes') {
                        $variable=Get-Variable -Scope 1 -Name First
                        $variable.Attributes.Clear()
                        $variable.Value=42
                    }
                    if($this.Mode -eq 'Automatic') {
                        try {
                            $bound=(Get-Variable -Scope 1 -Name PSBoundParameters -ErrorAction Stop).Value
                            $invocation=(Get-Variable -Scope 1 -Name MyInvocation -ErrorAction Stop).Value
                            $command=(Get-Variable -Scope 1 -Name PSCmdlet -ErrorAction Stop).Value
                            return "bound=$($bound['First']);invocation=$($invocation.MyCommand.Name);owner=$($command.MyInvocation.MyCommand.Name)"
                        } catch { return 'automatic-error:'+$_.FullyQualifiedErrorId }
                    }
                    "seen=$First"
                }
                $item
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.BindingScopeSibling", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(shape == "lifecycle" ? 0 : 1, result.Manifest!.CompiledMethods);
        var modes = shape == "automatic" ? "'Automatic'" : shape == "validated" ? "'WriteFirst','ClearAttributes'" : "'WriteFirst','WritePreference'";
        var probe = "$global:VerbosePreference='SilentlyContinue'; foreach($mode in " + modes + ") { " + """
            $item=New-SiblingValue -Mode $mode
            try { Read-SiblingBinding -First 'before' -Value $item }
            catch { 'error:'+$_.FullyQualifiedErrorId+';'+$_.Exception.Message }
            Read-SiblingBinding -First 'fresh' -Value 'plain'
            'caller-verbose:'+$global:VerbosePreference
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-binding-sibling");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-binding-sibling");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
