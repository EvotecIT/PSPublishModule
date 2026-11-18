using System.Diagnostics;

namespace PowerForge;

public sealed partial class ModuleDependencyInstaller
{
    /// <summary>Logs which module is being installed with which tool, then how long it took.</summary>
    private void RunInstallStep(string progress, ModuleDependency dep, string? version, string tool, string verb, Action action)
        => RunInstallStep(progress, dep, version, tool, verb, () =>
        {
            action();
            return true;
        });

    private T RunInstallStep<T>(string progress, ModuleDependency dep, string? version, string tool, string verb, Func<T> action)
    {
        var label = string.IsNullOrWhiteSpace(version) ? dep.Name : $"{dep.Name} {version}";
        _logger.Info($"{verb} {label} ({progress}) via {tool}.");
        var timer = Stopwatch.StartNew();
        var result = action();
        var done = string.Equals(verb, "Updating", StringComparison.Ordinal) ? "Updated" : "Installed";
        _logger.Info($"{done} {label} via {tool} in {DotNetRepositoryReleaseService.FormatDuration(timer.Elapsed)}.");
        return result;
    }

    private void LogModuleFailure(string name, string progress, TimeSpan elapsed, Exception exception)
        => _logger.Warn($"Could not install {name} ({progress}) after {DotNetRepositoryReleaseService.FormatDuration(elapsed)}: {exception.Message}");
}
