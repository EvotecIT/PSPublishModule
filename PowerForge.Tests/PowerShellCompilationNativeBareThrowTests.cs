using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeBareThrow_PreservesEmptyHandledStateAndOfflineRegistryFailures(string framework, string host)
    {
        var sources = new[] { "New-PSRegistry", "Remove-PSRegistry", "Set-PSRegistry" }.Select(name =>
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Registry", name + ".ps1"));
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources.Select(File.ReadAllText)) + Environment.NewLine + """
            function Invoke-BareThrow {
                [CmdletBinding()]param([object]$Trace)
                'before'; try { throw } finally { $Trace.Add('finally') }; 'after'
            }
            function Invoke-BareAfterCatch {
                [CmdletBinding()]param([object]$Trace)
                try { throw 'first' } catch { $Trace.Add('caught') }; throw
            }
            function Invoke-BareInCatch {
                [CmdletBinding()]param([object]$Trace)
                try { throw 'first' } catch { $Trace.Add($_.FullyQualifiedErrorId); try { throw } finally { $Trace.Add('finally') } }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeBareThrow", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "New-PSRegistry", "Remove-PSRegistry", "Set-PSRegistry", "Invoke-BareThrow", "Invoke-BareAfterCatch", "Invoke-BareInCatch" })
            Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name && unit.EmittedClrMethod);
        const string probe = """
            function Describe-BareError($record) {
                [pscustomobject]@{id=$record.FullyQualifiedErrorId;type=$record.Exception.GetType().FullName;message=$record.Exception.Message;category=$record.CategoryInfo.Category.ToString();line=$record.InvocationInfo.ScriptLineNumber;column=$record.InvocationInfo.OffsetInLine}
            }
            foreach($name in 'Invoke-BareThrow','Invoke-BareAfterCatch','Invoke-BareInCatch') {
                foreach($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    $trace=[Collections.Generic.List[string]]::new();$records=[Collections.Generic.List[object]]::new();$caught=$null
                    try { & $name -Trace $trace -ErrorAction $action 2>$null | ForEach-Object {$records.Add($_)} }
                    catch { $caught=Describe-BareError $_ }
                    [pscustomobject]@{name=$name;action=$action;records=@($records);trace=@($trace);caught=$caught} | ConvertTo-Json -Depth 6 -Compress
                }
            }
            # A helper invoked by a caller's catch has its own handled-exception slot.
            $trace=[Collections.Generic.List[string]]::new()
            try { throw 'caller' } catch { try { Invoke-BareThrow -Trace $trace -ErrorAction Stop } catch { Describe-BareError $_ | ConvertTo-Json -Compress } }
            & (Get-Command New-PSRegistry).Module {
                $script:RegistryTrace=[Collections.Generic.List[string]]::new();$script:MutationCalls=0
                function script:Get-PSRegistryDictionaries { $script:HiveDictionary=@{};$script:ReverseTypesDictionary=@{} }
                function script:Get-ComputerSplit { param($ComputerName); @('local','remote') }
                function script:Resolve-PrivateRegistry { param($RegistryPath);$RegistryPath }
                function script:Get-PSConvertSpecialRegistry { param($RegistryPath,$Computers,$HiveDictionary);$RegistryPath }
                function script:Get-PrivateRegistryTranslated { param($RegistryPath,$HiveDictionary,$Key,$Value,$Type,$ReverseTypesDictionary);[pscustomobject]@{HiveKey=$false} }
                function script:Unregister-MountedRegistry { $script:RegistryTrace.Add('unregister') }
                function script:New-PrivateRegistry { $script:MutationCalls++;throw 'unexpected mutation' }
                function script:Remove-PrivateRegistry { $script:MutationCalls++;throw 'unexpected mutation' }
                function script:Set-PrivateRegistry { $script:MutationCalls++;throw 'unexpected mutation' }
            }
            foreach($name in 'New-PSRegistry','Remove-PSRegistry','Set-PSRegistry') {
                foreach($action in 'Continue','Stop') {
                    & (Get-Command New-PSRegistry).Module { $script:RegistryTrace.Clear() }
                    $parameters=@{RegistryPath='offline-invalid-hive';ComputerName='offline';ErrorAction=$action;WarningAction='SilentlyContinue';WarningVariable='warnings'}
                    if($name -eq 'Set-PSRegistry') { $parameters.Type='string';$parameters.Value='data' }
                    $caught=$null;$warnings=@()
                    try { & $name @parameters } catch { $caught=Describe-BareError $_ }
                    $state=& (Get-Command New-PSRegistry).Module { [pscustomobject]@{trace=@($script:RegistryTrace);mutations=$script:MutationCalls} }
                    [pscustomobject]@{name=$name;action=$action;caught=$caught;warnings=@($warnings|ForEach-Object {$_.ToString()});state=$state} | ConvertTo-Json -Depth 6 -Compress
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("ScriptHalted", generated);
        Assert.Contains("\"mutations\":0", generated);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeBareThrow_RuntimeFreeOutsideCatchRemainsClosed(string framework)
    {
        using var fixture = ArtifactFixture.Create("function Invoke-Bare { throw }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
            PowerShellCompilationMode.Strict, targetFramework: framework, capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }
}
