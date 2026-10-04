namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    // Public Stopping precedes the asynchronous stop worker. Paused differential
    // probes must observe the native loop interrupt flag before releasing either
    // implementation, otherwise they compare different points in cancellation.
    private const string AcknowledgedStopProbe = """
        function Start-CompilerTestStop([Management.Automation.PowerShell]$Invocation) {
            $flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
            $contextProperty=$Invocation.Runspace.GetType().GetProperty('ExecutionContext',$flags)
            if($null -eq $contextProperty) { throw 'The host does not expose its execution context.' }
            $context=$contextProperty.GetValue($Invocation.Runspace,$null)
            $stopping=$context.GetType().GetProperty('CurrentPipelineStopping',$flags)
            if($null -eq $stopping) { throw 'The host does not expose the loop stopping contract.' }
            $pendingStop=$Invocation.BeginStop($null,$null)
            $deadline=[DateTime]::UtcNow.AddSeconds(5)
            while(-not $stopping.GetValue($context,$null) -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
            if(-not $stopping.GetValue($context,$null)) { throw 'The engine did not acknowledge the stop request.' }
            return $pendingStop
        };
        """;
}
