using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_HybridAdvancedFunctionRunsBeginProcessEndAndCleanPerPipeline()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Invoke-Lifecycle {
                [CmdletBinding(SupportsShouldProcess, ConfirmImpact='Low')]
                param(
                    [Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName, Position=0)]
                    [Alias('Value')]
                    [int] $Number,
                    [Parameter(ValueFromRemainingArguments)]
                    [string[]] $Remaining,
                    [string] $CleanupPath
                )
                begin { $total = 0 }
                process {
                    Write-Progress -Activity 'Lifecycle' -Status "Number $Number"
                    if ($Number -lt 0) { Write-Error "negative:$Number"; return }
                    if ($Number -eq 99) { throw 'terminating:99' }
                    if ($PSCmdlet.ShouldProcess("number:$Number", 'Accumulate')) {
                        $total += $Number
                        $total
                        if ($Remaining.Count -gt 0) { "remaining:$($Remaining -join ',')" }
                    }
                }
                end { "end:$total" }
                clean {
                    if ($CleanupPath) { [System.IO.File]::WriteAllText($CleanupPath, 'cleaned') }
                }
            }
            Export-ModuleMember -Function Invoke-Lifecycle
            """,
            ".psm1");
        var cleanup = Path.Combine(fixture.RootPath, "cleaned.txt");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Lifecycle",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        var lifecycle = Assert.Single(result.Manifest.Lifecycles);
        Assert.Equal(PowerShellCompilationLifecycleExecution.HostedSteppablePipeline, lifecycle.Execution);
        Assert.True(lifecycle.HasBegin && lifecycle.HasProcess && lifecycle.HasEnd && lifecycle.HasClean);
        Assert.Equal(2, lifecycle.SchemaVersion);
        Assert.Equal("7.3", lifecycle.MinimumPowerShellVersion);
        Assert.True(lifecycle.PreservesOriginalPipelineRecord);
        Assert.True(lifecycle.CleanupGuaranteed);
        Assert.True(lifecycle.ValueFromPipeline);
        Assert.True(lifecycle.ValueFromPipelineByPropertyName);
        Assert.True(lifecycle.ValueFromRemainingArguments);
        Assert.True(lifecycle.CommonParameters);
        Assert.True(lifecycle.SupportsShouldProcess);
        Assert.Equal("Low", lifecycle.ConfirmImpact, ignoreCase: true);
        var generatedLifecycleSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(result.GeneratedSourcePath!, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        Assert.Contains("Interlocked.CompareExchange(ref __powerForgeCleaned, 1, 0)", generatedLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("runspace.AvailabilityChanged += __powerForgeAvailabilityChanged;", generatedLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("CompleteStoppedLifecycle(runspace, terminalHost: false, completion);", generatedLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("lock (__powerForgeLifecycleGate)", generatedLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("var pipeline = GetLifecyclePipeline();", generatedLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("WriteLifecycleOutput(pipeline.Process", generatedLifecycleSource, StringComparison.Ordinal);

        var escapedCleanup = cleanup.Replace("'", "''", StringComparison.Ordinal);
        var proof = RunModuleProof(
            result.ArtifactPath!,
            "$command = Get-Command Invoke-Lifecycle; " +
            "\"$($command.CommandType)|$($command.Parameters.ContainsKey('Verbose'))|$($command.Parameters.ContainsKey('WhatIf'))\"; " +
            $"1,2 | Invoke-Lifecycle -CleanupPath '{escapedCleanup}' -Confirm:$false");
        Assert.Equal(new[] { "Cmdlet|True|True", "1", "3", "end:3" }, proof.Split(Environment.NewLine));
        Assert.Equal("cleaned", File.ReadAllText(cleanup));

        var byProperty = RunModuleProof(
            result.ArtifactPath!,
            "[pscustomobject]@{ Number = 4 } | Invoke-Lifecycle -Confirm:$false");
        Assert.Equal(new[] { "4", "end:4" }, byProperty.Split(Environment.NewLine));

        var remaining = RunModuleProof(
            result.ArtifactPath!,
            "Invoke-Lifecycle 2 a b -Confirm:$false");
        Assert.Equal(new[] { "2", "remaining:a,b", "end:2" }, remaining.Split(Environment.NewLine));

        var nonTerminating = RunModuleProof(
            result.ArtifactPath!,
            "-1,2 | Invoke-Lifecycle -ErrorAction SilentlyContinue -Confirm:$false");
        Assert.Equal(new[] { "2", "end:2" }, nonTerminating.Split(Environment.NewLine));

        var whatIf = RunModuleProof(
            result.ArtifactPath!,
            "$result = 2 | Invoke-Lifecycle -WhatIf; $result[-1]");
        Assert.Contains("What if:", whatIf, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("0", whatIf, StringComparison.Ordinal);

        File.Delete(cleanup);
        var terminating = RunModuleProof(
            result.ArtifactPath!,
            $"try {{ 99 | Invoke-Lifecycle -CleanupPath '{escapedCleanup}' -Confirm:$false }} catch {{ 'caught:' + $_.Exception.Message }}; Test-Path '{escapedCleanup}'");
        Assert.Contains("terminating:99", terminating, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("True", terminating, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridLifecycleStopInterruptsRunningProcessWithoutWaitingForTheLifecycleGate()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-SlowLifecycle { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Number,[string] $StartedPath,[string] $CleanupPath) " +
            "begin { $total = 0 } process { $total += $Number; [IO.File]::WriteAllText($StartedPath,'started'); while ($true) { Start-Sleep -Milliseconds 100 } } " +
            "end { $total } clean { [IO.File]::WriteAllText($CleanupPath,'cleaned') } }; Export-ModuleMember -Function Invoke-SlowLifecycle",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Lifecycle.ConcurrentStop",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Single(result.Manifest!.Lifecycles);
        var started = Path.Combine(fixture.RootPath, "started.txt");
        var cleaned = Path.Combine(fixture.RootPath, "cleaned.txt");
        var command =
            "$ps=[powershell]::Create(); " +
            $"[void]$ps.AddScript(\"Import-Module -Name '{EscapePowerShell(result.ArtifactPath!)}' -Force; 1 | Invoke-SlowLifecycle -StartedPath '{EscapePowerShell(started)}' -CleanupPath '{EscapePowerShell(cleaned)}'\"); " +
            "$async=$ps.BeginInvoke(); $deadline=[DateTime]::UtcNow.AddSeconds(5); " +
            $"while (!(Test-Path '{EscapePowerShell(started)}') -and [DateTime]::UtcNow -lt $deadline) {{ Start-Sleep -Milliseconds 20 }}; " +
            $"if (!(Test-Path '{EscapePowerShell(started)}')) {{ throw 'Lifecycle process did not start.' }}; " +
            "$watch=[Diagnostics.Stopwatch]::StartNew(); $ps.Stop(); $watch.Stop(); " +
            "try { $ps.EndInvoke($async) } catch { }; " +
            "$ps.Dispose(); " +
            $"$cleanupDeadline=[DateTime]::UtcNow.AddSeconds(2); while (!(Test-Path '{EscapePowerShell(cleaned)}') -and [DateTime]::UtcNow -lt $cleanupDeadline) {{ Start-Sleep -Milliseconds 20 }}; " +
            $"$proof=\"$($watch.ElapsedMilliseconds)|$(Test-Path '{EscapePowerShell(cleaned)}')\"; $proof";

        var proof = RunPowerShellWithTimeout(command, 15_000);

        Assert.Equal(0, proof.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(proof.StandardError), proof.StandardError);
        var parts = proof.StandardOutput.Trim().Split('|');
        Assert.InRange(long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), 0, 5_000);
        Assert.True(bool.Parse(parts[1]), "The authored clean block did not run after PowerShell.Stop().");
    }

    [Fact]
    public void Build_HybridLifecyclePreservesOriginalRecordAndCleansEveryFailurePath()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Test-RecordIdentity {
                [CmdletBinding()] param([Parameter(ValueFromPipelineByPropertyName)][int] $Number)
                process { "$($_.Marker)|$([object]::ReferenceEquals($_.PSObject.BaseObject, $_.Self.PSObject.BaseObject))|$Number" }
            }
            function Test-BeginFailure {
                [CmdletBinding()] param([string] $CleanupPath)
                begin { throw 'begin-failure' }
                clean { [System.IO.File]::WriteAllText($CleanupPath, 'begin-clean') }
            }
            function Test-EndFailure {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Number, [string] $CleanupPath)
                process { $Number }
                end { throw 'end-failure' }
                clean { [System.IO.File]::WriteAllText($CleanupPath, 'end-clean') }
            }
            function Test-EarlyStop {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Number, [string] $CleanupPath)
                process { $Number }
                clean { [System.IO.File]::WriteAllText($CleanupPath, 'stop-clean') }
            }
            Export-ModuleMember -Function Test-RecordIdentity,Test-BeginFailure,Test-EndFailure,Test-EarlyStop
            """,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Lifecycle.FailurePaths",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        var identity = RunModuleProof(
            result.ArtifactPath!,
            "$item=[pscustomobject]@{Number=7;Marker='kept';Self=$null}; $item.Self=$item; $item | Test-RecordIdentity");
        Assert.Equal("kept|True|7", identity);

        var beginPath = Path.Combine(fixture.RootPath, "begin-clean.txt");
        var endPath = Path.Combine(fixture.RootPath, "end-clean.txt");
        var stopPath = Path.Combine(fixture.RootPath, "stop-clean.txt");
        var failureProof = RunModuleProof(
            result.ArtifactPath!,
            $"try {{ Test-BeginFailure -CleanupPath '{EscapePowerShell(beginPath)}' }} catch {{ $_.Exception.Message }}; " +
            $"try {{ 1 | Test-EndFailure -CleanupPath '{EscapePowerShell(endPath)}' }} catch {{ $_.Exception.Message }}; " +
            $"1..3 | Test-EarlyStop -CleanupPath '{EscapePowerShell(stopPath)}' | Select-Object -First 1; " +
            $"Get-Content '{EscapePowerShell(beginPath)}'; Get-Content '{EscapePowerShell(endPath)}'; Get-Content '{EscapePowerShell(stopPath)}'");
        Assert.Contains("begin-failure", failureProof, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("end-failure", failureProof, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("begin-clean" + Environment.NewLine + "end-clean" + Environment.NewLine + "stop-clean", failureProof, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Net472LifecycleRunsOnWindowsPowerShellAndRejectsCleanBefore73()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")))
            return;
        using var fixture = ArtifactFixture.Create(
            """
            function Invoke-LegacyLifecycle {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Number)
                begin { $total = 0 }
                process { $total += $Number }
                end { $total }
            }
            function Invoke-CleanLifecycle {
                [CmdletBinding()] param()
                process { 'new-host' }
                clean { $null = 1 }
            }
            Export-ModuleMember -Function Invoke-LegacyLifecycle,Invoke-CleanLifecycle
            """,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Lifecycle.WindowsPowerShell",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net472"
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        var proof = RunWindowsPowerShellModuleProof(
            result.ArtifactPath!,
            "1,2 | Invoke-LegacyLifecycle; try { Invoke-CleanLifecycle } catch { $_.Exception.Message }");

        Assert.StartsWith("3" + Environment.NewLine, proof, StringComparison.Ordinal);
        Assert.Contains("requires PowerShell 7.3 or newer", proof, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsHostedLifecycle()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Lifecycle { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int]$Number) begin { $total = 0 } process { $total += $Number } end { $total } }",
            ".psm1");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictLifecycle",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("begin", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Strict", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapePowerShell(string path)
        => path.Replace("'", "''", StringComparison.Ordinal);

    private static string RunWindowsPowerShellModuleProof(string modulePath, string command)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"Import-Module -Name '{EscapePowerShell(modulePath)}' -Force; {command}");
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Windows PowerShell lifecycle proof timed out.");
        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        return string.Join(Environment.NewLine, output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunPowerShellWithTimeout(string command, int timeoutMilliseconds)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);
        process.Start();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"PowerShell lifecycle stop proof did not exit within {timeoutMilliseconds} milliseconds.");
        }
        return (process.ExitCode, process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
    }
}
