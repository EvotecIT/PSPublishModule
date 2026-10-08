using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void FallibleAssignment_PreservesReferenceInitializationAndReassignment(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-FirstList {
                [CmdletBinding()] param([int]$Capacity)
                $List=[Collections.ArrayList]::new($Capacity)
                [void]$List.Add('item')
                'after'
                return [object]::ReferenceEquals($List,$null)
            }
            function Get-NullSeededList {
                [CmdletBinding()] param([int]$Capacity)
                $List=$null
                $List=[Collections.ArrayList]::new($Capacity)
                [void]$List.Add('item')
                'after'
                return [object]::ReferenceEquals($List,$null)
            }
            function Get-ReassignedList {
                [CmdletBinding()] param([int]$Capacity)
                $List=[Collections.ArrayList]::new()
                $List=[Collections.ArrayList]::new($Capacity)
                [void]$List.Add('item')
                'after'
                return [object]::ReferenceEquals($List,$null)
            }
            function Get-CaughtList {
                [CmdletBinding()] param([int]$Capacity)
                $List=$null
                try { $List=[Collections.ArrayList]::new($Capacity) }
                catch [ArgumentOutOfRangeException] { 'caught-constructor' }
                finally { 'finally' }
                [void]$List.Add('item')
                'after'
                return [object]::ReferenceEquals($List,$null)
            }
            function Get-LoopList {
                [CmdletBinding()] param([int]$Capacity)
                for ([int]$Index=0; $Index -lt 2; $Index++) {
                    $List=[Collections.ArrayList]::new($Capacity)
                    [void]$List.Add('item')
                    'iteration'
                }
                return 'after'
            }
            function Get-StaticReference {
                [CmdletBinding()] param([int]$Capacity)
                $Encoding=[Text.Encoding]::GetEncoding($Capacity)
                [void]$Encoding.GetPreamble()
                'after'
                return [object]::ReferenceEquals($Encoding,$null)
            }
            function Get-InstanceReference {
                [CmdletBinding()] param([int]$Capacity)
                $Text='abc'
                $Characters=$Text.ToCharArray($Capacity,1)
                [void]$Characters.Clone()
                'after'
                return [object]::ReferenceEquals($Characters,$null)
            }
            function Get-BranchedReference {
                [CmdletBinding()] param([int]$Capacity)
                $List=$null
                if ($Capacity -eq 0) { $List=[Collections.ArrayList]::new() }
                else { $List=[Collections.ArrayList]::new($Capacity) }
                [void]$List.Add('item')
                'after'
                return [object]::ReferenceEquals($List,$null)
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.FallibleAssignment", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(8, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            foreach ($command in 'Get-FirstList','Get-NullSeededList','Get-ReassignedList','Get-CaughtList','Get-LoopList',
                'Get-StaticReference','Get-InstanceReference','Get-BranchedReference') {
                foreach ($capacity in -1,0,2) {
                    foreach ($action in 'Continue','SilentlyContinue','Stop') {
                        $records=[Collections.Generic.List[string]]::new()
                        $observe={
                                if ($_ -is [Management.Automation.ErrorRecord]) { [void]$records.Add('error:' + $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                                else { [void]$records.Add('value:' + $_) }
                        }
                        if ($action -eq 'Stop') {
                            try { & $command -Capacity $capacity -ErrorAction $action 2>&1 | ForEach-Object $observe }
                            catch { [void]$records.Add('caught:' + $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                        } else { & $command -Capacity $capacity -ErrorAction $action 2>&1 | ForEach-Object $observe }
                        [pscustomobject]@{ command=$command; capacity=$capacity; action=$action; records=$records.ToArray() } | ConvertTo-Json -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-fallible-assignment");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-fallible-assignment");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("ArgumentOutOfRangeException", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("value:after", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("InvokeMethodOnNull", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("value:caught-constructor", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("value:iteration", original.StandardOutput, StringComparison.Ordinal);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "ORIGINAL:\n" + original.StandardOutput + "\nGENERATED:\n" + compiled.StandardOutput);
    }
}
