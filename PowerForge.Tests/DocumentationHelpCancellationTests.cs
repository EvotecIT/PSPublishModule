using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class DocumentationHelpCancellationTests
{
    [Fact]
    public async Task HelpInvocation_CancellationStopsRunningPipelineAndPropagatesToken()
    {
        using var started = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        using var ps = PowerShell.Create();
        ps.AddScript("param($started) $started.Set(); Start-Sleep -Seconds 30; 'completed'")
            .AddArgument(started);

        var cancel = Task.Run(() =>
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "The PowerShell pipeline did not start.");
            cancellation.Cancel();
        });

        try
        {
            var error = Assert.Throws<OperationCanceledException>(() =>
                GetHelpParser.InvokeHelp(ps, timeoutSeconds: 15, cancellation.Token));

            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(PSInvocationState.Stopped, ps.InvocationStateInfo.State);
        }
        finally
        {
            await cancel;
        }
    }
}
