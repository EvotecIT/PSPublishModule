using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeWorkflow_ArrayLoopCapturePreservesRecordsAndFailureState(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-CapturedLoop {
                param([int] $Count, [switch] $Fail)
                $values = 'prior'
                try {
                    $values = @(foreach ($item in @(0, 1, 2)) {
                        if ($item -ge $Count) { break }
                        if ($Fail -and $item -eq 1) { throw 'capture-failure' }
                        if ($item -eq 0) { $null } else { ,@($item, 'nested') }
                    })
                    ,$values
                } catch {
                    $values
                    $_.FullyQualifiedErrorId
                    $_.InvocationInfo.ScriptLineNumber
                } finally { 'finally' }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.WorkflowArrayCapture",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, entry => Assert.False(entry.RetainedHostedSource));
        const string probe = """
            foreach ($count in 0,1,3) {
                foreach ($fail in $false,$true) {
                    $records = @(Read-CapturedLoop -Count $count -Fail:$fail)
                    [ordered]@{
                        count = $count
                        fail = $fail
                        firstType = if ($null -eq $records[0]) { '<null>' } else { $records[0].GetType().FullName }
                        records = $records
                    } | ConvertTo-Json -Depth 8 -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "workflow-array-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "workflow-array-compiled");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Contains("System.Object[]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("prior", original.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeWorkflow_MultipleFormatArgumentsAndQueuePreserveHostBehavior(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Origin {
                param([Parameter(Mandatory)][string] $Url)
                $uri = [System.Uri] $Url
                '{0}://{1}' -f $uri.Scheme.ToLowerInvariant(), $uri.Host.ToLowerInvariant()
            }
            function Read-Queue {
                param([string[]] $Values, [switch] $ReadEmpty)
                $queue = [System.Collections.Generic.Queue[string]]::new()
                foreach ($value in $Values) { $queue.Enqueue($value) }
                while ($queue.Count -gt 0) { $queue.Dequeue() }
                if ($ReadEmpty) {
                    try { $queue.Dequeue() }
                    catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
                    finally { 'finally' }
                }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.WorkflowScalarAndQueue",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, entry => Assert.False(entry.RetainedHostedSource));
        const string probe = """
            foreach ($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($culture)
                [ordered]@{
                    culture = $culture
                    origins = @(Read-Origin 'HTTPS://Example.TEST:8443/a'; Read-Origin 'http://localhost/test')
                    queue = @(Read-Queue -Values @('first','second','first') -ReadEmpty)
                    empty = @(Read-Queue -Values @() -ReadEmpty)
                } | ConvertTo-Json -Depth 5 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "workflow-scalar-queue-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "workflow-scalar-queue-compiled");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Contains("https://example.test", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("finally", original.StandardOutput, StringComparison.Ordinal);
    }
}
