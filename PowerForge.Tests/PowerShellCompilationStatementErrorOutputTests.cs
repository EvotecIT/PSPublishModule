using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorArtifact_StreamsConditionalAndLoopClrResults(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-DecodedText {
                [CmdletBinding()] param([byte[]]$Bytes)
                if ($null -ne $Bytes) { [Text.Encoding]::Unicode.GetString($Bytes) }
            }
            function Get-ParsedRecords {
                [CmdletBinding()] param([string[]]$Values)
                'before'
                foreach ($text in $Values) { [int]::Parse($text) }
                'after'
            }
            function Get-ReceiverText {
                [CmdletBinding()] param([Text.StringBuilder]$Builder)
                $Builder.ToString()
                'after'
            }
            """, ".psm1");
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "Generated.StatementOutput", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework
        };
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(3, result.Manifest.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            foreach ($bytes in @($null,[byte[]]@(),[byte[]]@(65),[byte[]]@(65,0),[byte[]]@(0,216),[Text.Encoding]::Unicode.GetBytes('hello'))) {
                $records=@(Get-DecodedText -Bytes $bytes)
                [pscustomobject]@{ kind='decoded'; bytes=$bytes; records=$records; types=@($records | ForEach-Object { $_.GetType().FullName }) } | ConvertTo-Json -Compress
            }
            foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                foreach ($command in 'Get-ParsedRecords','Get-ReceiverText') {
                    foreach ($callerCatch in $false,$true) {
                        if (!$callerCatch -and $action -eq 'Stop') { continue }
                        $parameters = if ($command -eq 'Get-ParsedRecords') { @{ Values=@('1','bad','2') } } else { @{ Builder=$null } }
                        $faults=@(); $Error.Clear(); $records=[Collections.Generic.List[string]]::new()
                        if ($callerCatch) {
                            try {
                                & $command @parameters -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object {
                                    if ($_ -is [Management.Automation.ErrorRecord]) { [void]$records.Add($_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                                    else { [void]$records.Add($_.GetType().FullName + ':' + [string]$_) }
                                }
                            } catch { [void]$records.Add('caught:' + $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                        } else {
                            & $command @parameters -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object {
                                if ($_ -is [Management.Automation.ErrorRecord]) { [void]$records.Add($_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                                else { [void]$records.Add($_.GetType().FullName + ':' + [string]$_) }
                            }
                        }
                        [pscustomobject]@{ command=$command; action=$action; callerCatch=$callerCatch; records=$records.ToArray();
                            faults=@($faults | ForEach-Object { $_.GetType().FullName }); errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId }) } | ConvertTo-Json -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-clr-output");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-clr-output");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("System.Int32:2", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("InvokeMethodOnNull", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
