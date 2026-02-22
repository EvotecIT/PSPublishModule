using System.Diagnostics;
using System.Threading;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private void RunServiceLifecycleStep(
        DotNetPublishPlan plan,
        List<DotNetPublishArtefactResult> artefacts,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (artefacts is null) throw new ArgumentNullException(nameof(artefacts));
        if (step is null) throw new ArgumentNullException(nameof(step));
        if (string.IsNullOrWhiteSpace(step.TargetName))
            throw new InvalidOperationException("Service lifecycle step is missing TargetName.");

        var target = plan.Targets.FirstOrDefault(t => t.Name.Equals(step.TargetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Service lifecycle target not found: {step.TargetName}");

        var lifecycle = target.Publish.Service?.Lifecycle;
        if (lifecycle is null || !lifecycle.Enabled)
            return;
        if (lifecycle.Mode != DotNetPublishServiceLifecycleMode.Step)
            return;

        var style = step.Style ?? target.Publish.Style;
        var artefact = artefacts.LastOrDefault(a =>
            a.Target.Equals(target.Name, StringComparison.OrdinalIgnoreCase)
            && a.Framework.Equals(step.Framework ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && a.Runtime.Equals(step.Runtime ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && a.Style == style);

        if (artefact is null)
            throw new InvalidOperationException(
                $"Service lifecycle step could not find publish artefact for target '{target.Name}' ({step.Framework}, {step.Runtime}, {style}).");

        if (artefact.ServicePackage is null)
            throw new InvalidOperationException(
                $"Service lifecycle is enabled for '{target.Name}' but no service package metadata was produced.");

        ExecuteServiceLifecycle(artefact.OutputDir, artefact.ServicePackage, lifecycle);
    }

    private void ExecuteServiceLifecycle(
        string outputDir,
        DotNetPublishServicePackageResult package,
        DotNetPublishServiceLifecycleOptions lifecycle)
    {
        if (!lifecycle.Enabled) return;
        if (string.IsNullOrWhiteSpace(outputDir))
            throw new ArgumentException("Output directory must not be empty.", nameof(outputDir));
        if (package is null)
            throw new ArgumentNullException(nameof(package));

        if (!IsWindows())
        {
            HandlePolicy(
                lifecycle.OnUnsupportedPlatform,
                $"Service lifecycle requested for '{package.ServiceName}', but current OS is not Windows.");
            return;
        }

        if (lifecycle.WhatIf)
        {
            _logger.Info($"Service lifecycle (WhatIf) for '{package.ServiceName}'");
            _logger.Info($" -> stop={lifecycle.StopIfExists}, delete={lifecycle.DeleteIfExists}, install={lifecycle.Install}, start={lifecycle.Start}, verify={lifecycle.Verify}");
            return;
        }

        var serviceName = (package.ServiceName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new InvalidOperationException("Service lifecycle requires a non-empty service name.");

        var executableFullPath = ResolvePath(outputDir, package.ExecutablePath);
        EnsurePathWithinRoot(outputDir, executableFullPath, $"Service executable path for '{serviceName}'");
        if (!File.Exists(executableFullPath))
            throw new FileNotFoundException($"Service executable not found: {executableFullPath}", executableFullPath);

        var timeoutSeconds = lifecycle.StopTimeoutSeconds <= 0 ? 30 : lifecycle.StopTimeoutSeconds;

        var exists = ServiceExists(outputDir, serviceName);
        if (exists && lifecycle.StopIfExists)
            StopService(outputDir, serviceName, timeoutSeconds, lifecycle);

        exists = ServiceExists(outputDir, serviceName);
        if (exists && lifecycle.DeleteIfExists)
            DeleteService(outputDir, serviceName, timeoutSeconds, lifecycle);

        if (lifecycle.Install)
        {
            exists = ServiceExists(outputDir, serviceName);
            if (exists)
            {
                HandlePolicy(
                    lifecycle.OnExecutionFailure,
                    $"Service '{serviceName}' already exists and cannot be installed. Enable DeleteIfExists or disable Install.");
            }
            else
            {
                InstallService(outputDir, serviceName, package, executableFullPath, lifecycle);
            }
        }

        if (lifecycle.Start)
            StartService(outputDir, serviceName, timeoutSeconds, lifecycle);

        if (lifecycle.Verify)
            VerifyService(outputDir, serviceName, lifecycle.Start, lifecycle);
    }

    private void ExecuteServiceLifecycleInlineBeforePublish(
        string outputDir,
        string targetName,
        DotNetPublishServicePackageOptions service,
        DotNetPublishServiceLifecycleOptions lifecycle)
    {
        if (service is null || lifecycle is null) return;
        if (!lifecycle.Enabled || lifecycle.Mode != DotNetPublishServiceLifecycleMode.InlineRebuild) return;

        var serviceName = ResolveServiceLifecycleName(targetName, service.ServiceName);
        if (!IsWindows())
        {
            HandlePolicy(
                lifecycle.OnUnsupportedPlatform,
                $"Inline service lifecycle requested for '{serviceName}', but current OS is not Windows.");
            return;
        }

        if (lifecycle.WhatIf)
        {
            _logger.Info($"Service lifecycle inline-pre (WhatIf) for '{serviceName}'");
            _logger.Info($" -> stop={lifecycle.StopIfExists}, delete={lifecycle.DeleteIfExists}");
            return;
        }

        var timeoutSeconds = lifecycle.StopTimeoutSeconds <= 0 ? 30 : lifecycle.StopTimeoutSeconds;
        var exists = ServiceExists(outputDir, serviceName);
        if (exists && lifecycle.StopIfExists)
            StopService(outputDir, serviceName, timeoutSeconds, lifecycle);

        exists = ServiceExists(outputDir, serviceName);
        if (exists && lifecycle.DeleteIfExists)
            DeleteService(outputDir, serviceName, timeoutSeconds, lifecycle);
    }

    private void ExecuteServiceLifecycleInlineAfterPublish(
        string outputDir,
        DotNetPublishServicePackageResult package,
        DotNetPublishServiceLifecycleOptions lifecycle)
    {
        if (package is null || lifecycle is null) return;
        if (!lifecycle.Enabled || lifecycle.Mode != DotNetPublishServiceLifecycleMode.InlineRebuild) return;

        var serviceName = ResolveServiceLifecycleName(null, package.ServiceName);
        if (!IsWindows())
        {
            HandlePolicy(
                lifecycle.OnUnsupportedPlatform,
                $"Inline service lifecycle requested for '{serviceName}', but current OS is not Windows.");
            return;
        }

        if (lifecycle.WhatIf)
        {
            _logger.Info($"Service lifecycle inline-post (WhatIf) for '{serviceName}'");
            _logger.Info($" -> install={lifecycle.Install}, start={lifecycle.Start}, verify={lifecycle.Verify}");
            return;
        }

        var timeoutSeconds = lifecycle.StopTimeoutSeconds <= 0 ? 30 : lifecycle.StopTimeoutSeconds;
        if (lifecycle.Install)
        {
            var executableFullPath = ResolvePath(outputDir, package.ExecutablePath);
            EnsurePathWithinRoot(outputDir, executableFullPath, $"Service executable path for '{serviceName}'");
            if (!File.Exists(executableFullPath))
                throw new FileNotFoundException($"Service executable not found: {executableFullPath}", executableFullPath);

            var exists = ServiceExists(outputDir, serviceName);
            if (exists)
            {
                HandlePolicy(
                    lifecycle.OnExecutionFailure,
                    $"Service '{serviceName}' already exists and cannot be installed. Enable DeleteIfExists or disable Install.");
            }
            else
            {
                InstallService(outputDir, serviceName, package, executableFullPath, lifecycle);
            }
        }

        if (lifecycle.Start)
            StartService(outputDir, serviceName, timeoutSeconds, lifecycle);

        if (lifecycle.Verify)
            VerifyService(outputDir, serviceName, lifecycle.Start, lifecycle);
    }

    private static string ResolveServiceLifecycleName(string? targetName, string? configuredServiceName)
    {
        var serviceName = (configuredServiceName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(serviceName))
            return serviceName;

        var target = (targetName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(target))
            return target;

        throw new InvalidOperationException("Service lifecycle requires a non-empty service name.");
    }

    private static bool ServiceExists(string workingDir, string serviceName)
    {
        var result = RunProcess("sc.exe", workingDir, new[] { "query", serviceName });
        return result.ExitCode == 0;
    }

    private void StopService(string workingDir, string serviceName, int timeoutSeconds, DotNetPublishServiceLifecycleOptions lifecycle)
    {
        _logger.Info($"Service lifecycle: stopping '{serviceName}'");
        var result = RunProcess("sc.exe", workingDir, new[] { "stop", serviceName });
        if (result.ExitCode != 0 && !ContainsServiceMessage(result, "1062", "SERVICE_NOT_ACTIVE"))
        {
            HandlePolicy(
                lifecycle.OnExecutionFailure,
                $"Failed to stop service '{serviceName}' (exit {result.ExitCode}). {SummarizeServiceError(result)}");
            return;
        }

        if (!WaitForServiceState(workingDir, serviceName, "STOPPED", TimeSpan.FromSeconds(timeoutSeconds)))
        {
            HandlePolicy(
                lifecycle.OnExecutionFailure,
                $"Timed out waiting for service '{serviceName}' to stop.");
        }
    }

    private void DeleteService(string workingDir, string serviceName, int timeoutSeconds, DotNetPublishServiceLifecycleOptions lifecycle)
    {
        _logger.Info($"Service lifecycle: deleting '{serviceName}'");
        var result = RunProcess("sc.exe", workingDir, new[] { "delete", serviceName });
        if (result.ExitCode != 0 && !ContainsServiceMessage(result, "1060", "does not exist"))
        {
            HandlePolicy(
                lifecycle.OnExecutionFailure,
                $"Failed to delete service '{serviceName}' (exit {result.ExitCode}). {SummarizeServiceError(result)}");
            return;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds));
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (!ServiceExists(workingDir, serviceName))
                return;
            Thread.Sleep(500);
        }

        HandlePolicy(
            lifecycle.OnExecutionFailure,
            $"Timed out waiting for service '{serviceName}' deletion.");
    }

    private void InstallService(
        string workingDir,
        string serviceName,
        DotNetPublishServicePackageResult package,
        string executableFullPath,
        DotNetPublishServiceLifecycleOptions lifecycle)
    {
        _logger.Info($"Service lifecycle: installing '{serviceName}'");

        var binPath = $"\"{executableFullPath}\"";
        if (!string.IsNullOrWhiteSpace(package.Arguments))
            binPath += " " + package.Arguments!.Trim();

        var createArgs = new List<string>
        {
            "create",
            serviceName,
            "binPath=",
            binPath,
            "start=",
            "auto",
            "DisplayName=",
            string.IsNullOrWhiteSpace(package.DisplayName) ? serviceName : package.DisplayName
        };

        var create = RunProcess("sc.exe", workingDir, createArgs);
        if (create.ExitCode != 0)
        {
            HandlePolicy(
                lifecycle.OnExecutionFailure,
                $"Failed to create service '{serviceName}' (exit {create.ExitCode}). {SummarizeServiceError(create)}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(package.Description))
        {
            var desc = RunProcess("sc.exe", workingDir, new[] { "description", serviceName, package.Description! });
            if (desc.ExitCode != 0)
            {
                HandlePolicy(
                    lifecycle.OnExecutionFailure,
                    $"Failed to set description for service '{serviceName}' (exit {desc.ExitCode}). {SummarizeServiceError(desc)}");
            }
        }

        ConfigureServiceRecovery(workingDir, serviceName, package, lifecycle);
    }

    private void ConfigureServiceRecovery(
        string workingDir,
        string serviceName,
        DotNetPublishServicePackageResult package,
        DotNetPublishServiceLifecycleOptions lifecycle)
    {
        var recovery = ResolveRecoveryOptions(package);
        if (recovery is null || !recovery.Enabled)
            return;

        var reset = recovery.ResetPeriodSeconds <= 0 ? 86400 : recovery.ResetPeriodSeconds;
        var delayMs = (recovery.RestartDelaySeconds <= 0 ? 60 : recovery.RestartDelaySeconds) * 1000;
        var actions = $"restart/{delayMs}/restart/{delayMs}/restart/{delayMs}";

        _logger.Info($"Service lifecycle: configuring recovery for '{serviceName}'");
        var failure = RunProcess("sc.exe", workingDir, new[] { "failure", serviceName, "reset=", reset.ToString(), "actions=", actions });
        if (failure.ExitCode != 0)
        {
            HandlePolicy(
                recovery.OnFailure,
                $"Failed to configure recovery actions for service '{serviceName}' (exit {failure.ExitCode}). {SummarizeServiceError(failure)}");
            return;
        }

        var failureFlag = RunProcess("sc.exe", workingDir, new[] { "failureflag", serviceName, recovery.ApplyToNonCrashFailures ? "1" : "0" });
        if (failureFlag.ExitCode != 0)
        {
            HandlePolicy(
                recovery.OnFailure,
                $"Failed to configure recovery failure flag for service '{serviceName}' (exit {failureFlag.ExitCode}). {SummarizeServiceError(failureFlag)}");
        }
    }

    private static DotNetPublishServiceRecoveryOptions? ResolveRecoveryOptions(
        DotNetPublishServicePackageResult package)
    {
        if (package is null) return null;
        // Recovery is defined on service package options, but lifecycle currently receives only materialized package.
        // The lifecycle step resolves options from plan target and stores them in package metadata during generation.
        // If metadata does not include recovery payload, no recovery actions are applied.
        return package.Recovery;
    }

    private void StartService(string workingDir, string serviceName, int timeoutSeconds, DotNetPublishServiceLifecycleOptions lifecycle)
    {
        _logger.Info($"Service lifecycle: starting '{serviceName}'");
        var start = RunProcess("sc.exe", workingDir, new[] { "start", serviceName });
        if (start.ExitCode != 0 && !ContainsServiceMessage(start, "1056", "already running"))
        {
            HandlePolicy(
                lifecycle.OnExecutionFailure,
                $"Failed to start service '{serviceName}' (exit {start.ExitCode}). {SummarizeServiceError(start)}");
            return;
        }

        if (!WaitForServiceState(workingDir, serviceName, "RUNNING", TimeSpan.FromSeconds(timeoutSeconds)))
        {
            HandlePolicy(
                lifecycle.OnExecutionFailure,
                $"Timed out waiting for service '{serviceName}' to start.");
        }
    }

    private void VerifyService(
        string workingDir,
        string serviceName,
        bool expectedRunning,
        DotNetPublishServiceLifecycleOptions lifecycle)
    {
        var state = QueryServiceState(workingDir, serviceName);
        if (string.IsNullOrWhiteSpace(state))
        {
            HandlePolicy(
                lifecycle.OnExecutionFailure,
                $"Service verification failed: '{serviceName}' does not exist.");
            return;
        }

        var resolvedState = state ?? string.Empty;
        if (expectedRunning && !resolvedState.Equals("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            HandlePolicy(
                lifecycle.OnExecutionFailure,
                $"Service verification failed: '{serviceName}' state is '{resolvedState}', expected RUNNING.");
            return;
        }

        _logger.Info($"Service lifecycle verification: '{serviceName}' state={resolvedState}");
    }

    private static bool WaitForServiceState(string workingDir, string serviceName, string expectedState, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var state = QueryServiceState(workingDir, serviceName);
            if (string.Equals(state, expectedState, StringComparison.OrdinalIgnoreCase))
                return true;
            Thread.Sleep(500);
        }

        return false;
    }

    private static string? QueryServiceState(string workingDir, string serviceName)
    {
        var query = RunProcess("sc.exe", workingDir, new[] { "query", serviceName });
        if (query.ExitCode != 0) return null;

        var text = ((query.StdOut ?? string.Empty) + "\n" + (query.StdErr ?? string.Empty)).ToUpperInvariant();
        if (text.Contains("RUNNING")) return "RUNNING";
        if (text.Contains("STOPPED")) return "STOPPED";
        if (text.Contains("START_PENDING")) return "START_PENDING";
        if (text.Contains("STOP_PENDING")) return "STOP_PENDING";
        if (text.Contains("PAUSED")) return "PAUSED";
        return "UNKNOWN";
    }

    private static bool ContainsServiceMessage((int ExitCode, string StdOut, string StdErr) result, params string[] patterns)
    {
        var text = ((result.StdOut ?? string.Empty) + "\n" + (result.StdErr ?? string.Empty));
        foreach (var pattern in patterns ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            if (text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string SummarizeServiceError((int ExitCode, string StdOut, string StdErr) result)
    {
        var tail = TailLines(string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr, maxLines: 10, maxChars: 2000);
        return string.IsNullOrWhiteSpace(tail) ? string.Empty : (tail ?? string.Empty).Trim();
    }
}
