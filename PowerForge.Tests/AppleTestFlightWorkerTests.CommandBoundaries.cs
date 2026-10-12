using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
    [Theory]
    [InlineData("--policy")]
    [InlineData("--config")]
    [InlineData("--output")]
    [InlineData("--job")]
    public void WorkerCliRejectsDuplicateOptionsBeforeReadingUntrustedPolicy(string option)
    {
        var root = Sandbox();
        try
        {
            var result = RunCli(root, "apple-testflight", "status", option, option == "--output" ? "json" : "first", option, option == "--output" ? "json" : "second", "--output", "json");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Duplicate apple-testflight option", result.Output);
        }
        finally { DeleteSandbox(root); }
    }

    [Theory]
    [InlineData("--policy")]
    [InlineData("--config")]
    public void PinnedOperatorRejectsSeparatedAndEqualsDuplicateOptions(string option)
    {
        if (!File.Exists("/usr/local/bin/pwsh")) return;
        var root = Sandbox();
        try
        {
            var harness = Path.Combine(root, "duplicates.ps1");
            File.WriteAllText(harness, """
                param($Owner,$Option)
                $ErrorActionPreference='Stop'
                $tokens=$null; $errors=$null
                $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.ps1'),[ref]$tokens,[ref]$errors)
                $function=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-OptionValue'},$true)
                Invoke-Expression $function.Extent.Text
                foreach($tail in @($Option,($Option.ToUpperInvariant()+'=second'))) {
                    $ArgumentList=@('apple-testflight','run',$Option,'first',$tail,'second')
                    try { Get-OptionValue -Option $Option | Out-Null; throw 'Duplicate was accepted.' }
                    catch { if($_.Exception.Message -notlike '*Duplicate option at the pinned operator boundary*'){throw} }
                }
                """ );
            RunTool("/usr/local/bin/pwsh", root, "-NoProfile", "-NonInteractive", "-File", harness, "-Owner", FindRepository(), "-Option", option);
        }
        finally { DeleteSandbox(root); }
    }

    [Fact]
    public void LeaseRejectionRequiresCanonicalErrorEnvelopeAndPreservesAmbiguousStages()
    {
        Assert.False(AppleTestFlightWorker.IsLeaseRejection(75, "restore failed"));
        Assert.False(AppleTestFlightWorker.IsLeaseRejection(75, "{\"command\":\"apple-release\",\"success\":false,\"exitCode\":75}"));
        Assert.Equal(AppleTestFlightJobStage.ReleaseReady,
            AppleTestFlightWorker.FailureStage(AppleTestFlightJobStage.Releasing, new AppleTestFlightLeaseBusyException()));
        Assert.Equal(AppleTestFlightJobStage.AppleProcessingPending,
            AppleTestFlightWorker.FailureStage(AppleTestFlightJobStage.Releasing, new AppleTestFlightLeaseBusyException(), AppleTestFlightJobStage.AppleProcessingPending));
        Assert.Equal(AppleTestFlightJobStage.Planning,
            AppleTestFlightWorker.FailureStage(AppleTestFlightJobStage.CheckpointExecuting, new AppleTestFlightLeaseBusyException()));
        Assert.Equal(AppleTestFlightJobStage.NeedsOperator,
            AppleTestFlightWorker.FailureStage(AppleTestFlightJobStage.Releasing, new AppleTestFlightToolException("interrupted")));
    }

    internal static (int ExitCode, string Output, string Error) RunCli(string root, params string[] arguments)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.Combine(FindRepository(), "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(cli);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["POWERFORGE_APPLE_STATE_ROOT"] = Path.Combine(root, "state");
        foreach (var name in new[] { "APP_STORE_CONNECT_KEY_ID", "APP_STORE_CONNECT_ISSUER_ID", "APP_STORE_CONNECT_PRIVATE_KEY_PATH",
            "APP_STORE_CONNECT_PRIVATE_KEY", "ASC_KEY_ID", "ASC_ISSUER_ID", "ASC_PRIVATE_KEY_PATH", "ASC_PRIVATE_KEY", "POWERFORGE_APPLE_WORKER_GATE" }) start.Environment.Remove(name);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Synthetic CLI did not finish."); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
