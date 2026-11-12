using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGet.Configuration;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    /// <summary>Enrolls stopped runner installations and prepares their lifecycle hooks and scheduler entry point.</summary>
    /// <param name="spec">Local policy with physical roots and a prebuilt CLI executable.</param>
    /// <remarks>The caller protects the state directory and registers the native scheduler after this operation succeeds.</remarks>
    public void InstallMaintenance(RunnerMaintenanceSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (!MaintenanceContains(spec.StateRootPath, spec.ExecutablePath) || !File.Exists(spec.ExecutablePath))
            throw new ArgumentException("Maintenance requires a prebuilt executable inside its persistent state root.");
        if (new[] { spec.StateRootPath, spec.ExecutablePath }.Any(path => path.IndexOfAny(new[] { '"', '\r', '\n', '%', '!' }) >= 0))
            throw new ArgumentException("Hook installation paths cannot contain quotes, newlines or command substitutions.");
        MaintenancePhysicalPath(spec.ExecutablePath);
        if (!string.IsNullOrEmpty(spec.NuGetPackagesPath) && spec.NuGetConfigPaths.Length == 0)
            throw new ArgumentException("Expanded package retention requires the service identity's explicit NuGet configuration path.");
        foreach (var runner in spec.Runners)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(runner.RootPath, ".runner")));
            var value = json.RootElement;
            runner.AgentId = value.GetProperty("agentId").GetInt64();
            runner.Name = value.GetProperty("agentName").GetString()!;
            runner.GitHubUrl = value.GetProperty("gitHubUrl").GetString()!;
            runner.WorkRootPath = Path.GetFullPath(Path.Combine(runner.RootPath, value.GetProperty("workFolder").GetString()!));
        }
        ValidateMaintenanceSpec(spec);
        var processes = RunnerMaintenanceProcesses.Read();
        if (processes.Values.Any(p => (RunnerMaintenanceProcesses.IsWorker(p) || p.Name.Equals("Runner.Listener", StringComparison.OrdinalIgnoreCase))
            && (p.Executable is null || spec.Runners.Any(r => MaintenanceProcessBelongs(r, p)))))
            throw new InvalidOperationException("Stop the enrolled listeners during an idle maintenance window before installing hooks.");

        Directory.CreateDirectory(spec.StateRootPath);
        using var gate = MaintenanceLock(Path.Combine(spec.StateRootPath, "admission.lock"), TimeSpan.Zero)
            ?? throw new IOException("Maintenance is currently running.");
        var policy = Path.Combine(spec.StateRootPath, "policy.json");
        var wrapper = Path.Combine(spec.StateRootPath, "Invoke-RunnerMaintenance.ps1");
        using (var stream = typeof(RunnerHousekeepingService).Assembly.GetManifestResourceStream("PowerForge.Scripts.RunnerMaintenance.Invoke-RunnerMaintenance.ps1")
            ?? throw new IOException("Missing maintenance hook resource."))
        using (var reader = new StreamReader(stream)) MaintenanceWriteText(wrapper, reader.ReadToEnd());

        // Environment backups belong to this private host state. They are never copied into a checkout or diagnostic output.
        var changed = new List<(string Path, string Original)>();
        try
        {
            foreach (var path in spec.NuGetConfigPaths)
            {
                if (!Path.IsPathRooted(path)) throw new ArgumentException("NuGet configuration paths must be absolute.");
                MaintenancePhysicalPath(path);
                var original = File.Exists(path) ? File.ReadAllText(path) : "<configuration />";
                changed.Add((path, original));
                var backup = Path.Combine(spec.StateRootPath, "nuget-config-backups", MaintenanceRegistrationKey(path) + ".xml");
                if (!File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.WriteAllText(backup, original, new UTF8Encoding(false));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) File.WriteAllText(path, original, new UTF8Encoding(false));
                var settings = Settings.LoadSpecificSettings(Path.GetDirectoryName(path)!, Path.GetFileName(path));
                SettingsUtility.SetConfigValue(settings, "updatePackageLastAccessTime", "true");
                settings.SaveToDisk();
            }
            foreach (var runner in spec.Runners)
            {
                var environment = Path.Combine(runner.RootPath, ".env");
                var original = File.Exists(environment) ? File.ReadAllText(environment) : string.Empty;
                changed.Add((environment, original));
                var key = MaintenanceRegistrationKey(runner.RootPath);
                var hooks = Path.Combine(spec.StateRootPath, "hooks", key, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(hooks);
                var backup = Path.Combine(spec.StateRootPath, "hooks", key, "original.env");
                if (!File.Exists(backup)) File.WriteAllText(backup, original, new UTF8Encoding(false));
                var lines = original.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                foreach (var phase in new[] { "starting", "completed" })
                {
                    var variable = "ACTIONS_RUNNER_HOOK_JOB_" + (phase == "starting" ? "STARTED" : "COMPLETED");
                    var previous = lines.LastOrDefault(l => l.StartsWith(variable + "=", StringComparison.Ordinal))?.Substring(variable.Length + 1)
                        ?? Environment.GetEnvironmentVariable(variable);
                    previous = MaintenanceMigrateTemporaryHook(runner, variable, previous);
                    var settings = Path.Combine(hooks, phase + ".json");
                    if (!string.IsNullOrEmpty(previous) && MaintenanceContains(Path.Combine(spec.StateRootPath, "hooks"), previous!))
                        previous = MaintenanceRead(Path.Combine(Path.GetDirectoryName(previous)!, phase + ".json"), RunnerMaintenanceJsonContext.Default.RunnerMaintenanceHook)?.PreviousHook;
                    MaintenanceWrite(settings, new RunnerMaintenanceHook { Executable = spec.ExecutablePath, Policy = policy,
                        RunnerRoot = runner.RootPath, Phase = phase, PreviousHook = previous }, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceHook);
                    var path = Path.Combine(hooks, phase + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".ps1" : ".sh"));
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        File.WriteAllText(path, "& " + MaintenancePowerShellQuote(wrapper) + " -Configuration " + MaintenancePowerShellQuote(settings) + "\r\nexit $LASTEXITCODE\r\n", new UTF8Encoding(true));
                    else
                    {
                        File.WriteAllText(path, "#!/bin/sh\nexec pwsh -NoLogo -NoProfile -NonInteractive -File " + MaintenanceShellQuote(wrapper) + " -Configuration " + MaintenanceShellQuote(settings) + "\n");
                        RunnerMaintenanceProcesses.Run("chmod", spec.StateRootPath, "700", path);
                    }
                    MaintenanceSetEnvironment(lines, variable, path);
                }
                Directory.CreateDirectory(runner.TempPath);
                foreach (var variable in new[] { "TEMP", "TMP", "TMPDIR", "SystemTemp" }) MaintenanceSetEnvironment(lines, variable, runner.TempPath);
                if (!string.IsNullOrEmpty(spec.NuGetScratchPath)) MaintenanceSetEnvironment(lines, "NUGET_SCRATCH", spec.NuGetScratchPath!);
                MaintenanceSetEnvironment(lines, "NUGET_HTTP_CACHE_PATH", Path.Combine(runner.TempPath, "nuget-http"));
                MaintenanceSetEnvironment(lines, "DOTNET_BUNDLE_EXTRACT_BASE_DIR", Path.Combine(runner.TempPath, ".net"));
                MaintenanceWriteText(environment, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
            MaintenanceWrite(policy, spec, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceSpec);
            MaintenanceWrite(Path.Combine(spec.StateRootPath, "scheduled.json"), new RunnerMaintenanceHook
                { Executable = spec.ExecutablePath, Policy = policy, Phase = "scheduled" }, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceHook);
            File.WriteAllText(Path.Combine(spec.StateRootPath, "installed.json"), DateTime.UtcNow.ToString("O"), new UTF8Encoding(false));
        }
        catch
        {
            foreach (var item in changed) MaintenanceWriteText(item.Path, item.Original);
            throw;
        }
    }

    /// <summary>Runs a prepared lifecycle hook, preserving an existing hook and its failure behavior.</summary>
    /// <param name="configurationPath">Private hook configuration created by enrollment.</param>
    /// <param name="standardOutput">Optional raw output sink for the preserved custom hook.</param>
    /// <param name="standardError">Optional raw error sink for the preserved custom hook.</param>
    /// <returns>Maintenance result for the assigned lifecycle phase.</returns>
    public RunnerMaintenanceResult InvokeMaintenanceHook(string configurationPath, Action<string>? standardOutput = null, Action<string>? standardError = null)
    {
        var hook = MaintenanceRead(configurationPath, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceHook)
            ?? throw new IOException("Missing maintenance hook configuration.");
        var spec = MaintenanceRead(hook.Policy, RunnerMaintenanceJsonContext.Default.RunnerMaintenanceSpec)
            ?? throw new IOException("Missing maintenance policy.");
        if (!MaintenanceContains(spec.StateRootPath, configurationPath) || !PathsEqual(hook.Executable, spec.ExecutablePath) ||
            !Enum.TryParse<RunnerMaintenancePhase>(hook.Phase, true, out var phase)) throw new IOException("Invalid enrolled hook configuration.");
        Environment.SetEnvironmentVariable("NUGET_SCRATCH", spec.NuGetScratchPath);
        return MaintainCore(spec, phase, hook.RunnerRoot, dryRun: false,
            previousHook: string.IsNullOrEmpty(hook.PreviousHook) ? null : () => MaintenanceInvokePreviousHook(hook, spec, standardOutput, standardError));
    }

    private static void MaintenanceInvokePreviousHook(RunnerMaintenanceHook hook, RunnerMaintenanceSpec spec, Action<string>? standardOutput, Action<string>? standardError)
    {
            var path = hook.PreviousHook!;
            var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            string executable;
            string[] arguments;
            if (path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                executable = windows && !CommandExists("pwsh") ? "powershell.exe" : "pwsh";
                arguments = new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", path };
            }
            else
            {
                executable = windows ? "cmd.exe" : "/bin/bash";
                arguments = windows ? new[] { "/d", "/s", "/c", "\"" + path + "\"" } : new[] { path };
            }
            var previous = new ProcessRunner(ownProcessTree: true).RunAsync(new ProcessRunRequest(executable,
                string.IsNullOrEmpty(hook.RunnerRoot) ? spec.StateRootPath : hook.RunnerRoot, arguments, TimeSpan.FromSeconds(60),
                environmentVariables: null, captureOutput: true, captureError: true, outputLineReceived: standardOutput, errorLineReceived: standardError)).GetAwaiter().GetResult();
            if (!previous.Succeeded) throw new IOException("The preserved job hook failed.");
    }

    private static void MaintenanceSetEnvironment(List<string> lines, string name, string value)
    {
        if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new ArgumentException("Environment values must be single-line paths.");
        lines.RemoveAll(l => l.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
        lines.Add(name + "=" + value);
    }

    private static string? MaintenanceMigrateTemporaryHook(RunnerMaintenanceRegistration runner, string variable, string? previous)
    {
        if (string.IsNullOrEmpty(previous) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return previous;
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerForge.RunnerMaintenance");
        var root = Path.Combine(parent, runner.Name);
        if (!PathsEqual(previous!, Path.Combine(root, "Invoke-RunnerJobTempCleanup.ps1"))) return previous;
        MaintenancePhysicalPath(root);
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "configuration.json")));
        var value = configuration.RootElement;
        if (value.GetProperty("schemaVersion").GetInt32() != 1 || value.GetProperty("runnerName").GetString() != runner.Name ||
            !PathsEqual(value.GetProperty("runnerRoot").GetString()!, runner.RootPath) || !PathsEqual(value.GetProperty("tempRoot").GetString()!, runner.TempPath) ||
            value.GetProperty("gitHubUrl").GetString() != runner.GitHubUrl)
            throw new IOException("Previous temporary cleanup enrollment changed. Preserve its recovery record before migration.");
        var record = Path.Combine(parent, runner.Name + ".installation.json");
        MaintenancePhysicalPath(record);
        using var installation = JsonDocument.Parse(File.ReadAllText(record));
        var bytes = Convert.FromBase64String(installation.RootElement.GetProperty("originalEnvBase64").GetString()!);
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        if (!string.Equals(hash, installation.RootElement.GetProperty("originalEnvHash").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Previous temporary cleanup environment backup failed its integrity check.");
        var original = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        return original.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith(variable + "=", StringComparison.OrdinalIgnoreCase))?.Substring(variable.Length + 1);
    }

    private static string MaintenanceRegistrationKey(string root)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToLowerInvariant()))).Replace("-", "").Substring(0, 16);
    }

    private static string MaintenanceShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

    private static string MaintenancePowerShellQuote(string value) => "'" + value.Replace("'", "''") + "'";
}
