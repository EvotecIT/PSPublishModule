using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_StreamedCallsAndStopPreserveUnwindAndCleanup(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-Records { [CmdletBinding()] param() 1; return 2 }
            function Get-ForwardedRecords { [CmdletBinding()] param() Get-Records; return 3 }
            function Get-RepeatedRecords { [CmdletBinding()] param() Get-ForwardedRecords; Get-ForwardedRecords }
            function Get-Wrapper {
                [CmdletBinding()] param([bool]$Throw)
                try { Get-Records; return 3 }
                finally { if ($Throw) { throw [System.InvalidOperationException]::new('cleanup') } }
            }
            function Get-CaughtStop {
                [CmdletBinding()] param([System.Text.StringBuilder]$Trace)
                try { 'first'; 'second' }
                catch { [void]$Trace.Append('caught') }
                finally { [void]$Trace.Append('cleanup') }
                [void]$Trace.Append('after')
            }
            function Get-TypedCaughtStop {
                [CmdletBinding()] param([System.Text.StringBuilder]$Trace)
                try { 'first'; 'second' }
                catch [System.Exception] { [void]$Trace.Append('caught') }
                finally { [void]$Trace.Append('cleanup') }
                [void]$Trace.Append('after')
            }
            function Get-CallCaughtStop {
                [CmdletBinding()] param([System.Text.StringBuilder]$Trace)
                try { Get-Records }
                catch { [void]$Trace.Append('caught') }
                finally { [void]$Trace.Append('cleanup') }
                [void]$Trace.Append('after')
            }
            function Get-FinallyStop {
                [CmdletBinding()] param([System.Text.StringBuilder]$Trace)
                try { 'first'; 'second' }
                finally { 'cleanup-record'; [void]$Trace.Append('cleanup') }
            }
            function Get-EagerReturn {
                [CmdletBinding()] param([System.Text.StringBuilder]$Trace)
                try { return 5 } finally { [void]$Trace.Append('cleanup') }
            }
            function Get-CaughtError {
                [CmdletBinding()] param([System.Text.StringBuilder]$Trace)
                try { 'before'; throw [System.InvalidOperationException]::new('expected') }
                catch [System.Exception] { [void]$Trace.Append('caught'); 'handled' }
                finally { [void]$Trace.Append('cleanup') }
                'after'
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.OutputUnwind",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(10, result.Manifest!.CompiledMethods);
        const string probe = """
            'repeated:' + (@(Get-RepeatedRecords) -join '|')
            foreach ($throw in $false,$true) {
                $records = [System.Collections.Generic.List[object]]::new()
                try { Get-Wrapper -Throw $throw | ForEach-Object { [void]$records.Add($_) } } catch { 'error:' + $_.Exception.Message }
                'wrapper:{0}:{1}' -f $throw,($records -join '|')
            }
            $trace = [System.Text.StringBuilder]::new()
            Get-EagerReturn -Trace $trace | ForEach-Object { 'eager:{0}:trace:{1}' -f $_,$trace.ToString() }
            'eager-after:trace:' + $trace.ToString()
            $trace = [System.Text.StringBuilder]::new()
            $records = @(Get-CaughtError -Trace $trace)
            'ordinary-error:{0}:trace:{1}' -f ($records -join '|'),$trace.ToString()
            foreach ($command in 'Get-CaughtStop','Get-TypedCaughtStop','Get-CallCaughtStop','Get-FinallyStop') {
                $trace = [System.Text.StringBuilder]::new()
                $records = @(& $command -Trace $trace | Select-Object -First 1)
                '{0}:{1}:trace:{2}' -f $command,($records -join '|'),$trace.ToString()
            }
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("wrapper:True:1|2|3", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("repeated:1|2|3|1|2|3", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("eager:5:trace:", original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        Assert.Contains("eager-after:trace:cleanup", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ordinary-error:before|handled|after:trace:caughtcleanup", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Get-CaughtStop:first:trace:cleanup", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Get-TypedCaughtStop:first:trace:cleanup", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Get-CallCaughtStop:1:trace:cleanup", original.StandardOutput, StringComparison.Ordinal);
        // A second output attempt while unwinding a stopped pipeline interrupts the
        // rest of finally on both PowerShell hosts; do not invent extra cleanup.
        Assert.EndsWith("Get-FinallyStop:first:trace:", original.StandardOutput.Trim(), StringComparison.Ordinal);
        Assert.True((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()) ==
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()),
            "ORIGINAL:\n" + original.StandardOutput + "\nGENERATED:\n" + compiled.StandardOutput + "\nERROR:\n" + compiled.StandardError);
    }
}
