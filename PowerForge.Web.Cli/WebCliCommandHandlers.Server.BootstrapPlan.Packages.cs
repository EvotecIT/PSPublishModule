using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static void AddBootstrapPackageSteps(
        ICollection<PowerForgeServerBootstrapPlanStep> steps,
        ref int order,
        PowerForgeServerRecoveryManifest manifest,
        ISet<string> plannedCommands)
    {
        var hasPackageWork = false;
        if (manifest.Packages?.Apt?.Length > 0)
        {
            AddStep(steps, ref order, "packages", "Install apt prerequisites",
                BuildGuardedPackageCommand(
                    "apt-get update && apt-get install -y " + string.Join(' ', manifest.Packages.Apt.Select(ShellQuote)),
                    manifest.Systemd),
                plannedCommands: plannedCommands);
            hasPackageWork = true;
        }

        var dotnetPackages = GetDeclaredDotnetSdkPackageNames(manifest.Packages?.DotnetSdks);
        if (dotnetPackages.Length > 0)
        {
            AddStep(steps, ref order, "runtimes", "Install .NET SDK prerequisites",
                BuildGuardedPackageCommand(
                    "apt-get update && apt-get install -y " + string.Join(' ', dotnetPackages.Select(ShellQuote)),
                    manifest.Systemd),
                plannedCommands: plannedCommands);
            hasPackageWork = true;
        }

        if (manifest.Packages?.Powershell == true)
        {
            AddStep(steps, ref order, "runtimes", "Configure Microsoft package repository",
                BuildGuardedPackageCommand(BuildMicrosoftPackageRepositoryInstallCommand(), manifest.Systemd),
                plannedCommands: plannedCommands);
            AddStep(steps, ref order, "runtimes", "Install PowerShell prerequisite",
                BuildGuardedPackageCommand(BuildPowerShellInstallCommand(), manifest.Systemd),
                plannedCommands: plannedCommands);
            hasPackageWork = true;
        }

        // Package maintainer scripts can start units after the initial disable step.
        if (hasPackageWork)
            AddDisabledSystemdSteps(steps, ref order, manifest.Systemd, plannedCommands,
                "Reassert stopped and disabled after packages", repeat: true);
    }

    private static string BuildGuardedPackageCommand(string command, PowerForgeServerSystemd? systemd)
    {
        var disabledUnits = (systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
            .Concat(systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
            .Where(static unit => unit.EnforceDisabled && !string.IsNullOrWhiteSpace(unit.Name))
            .ToArray();
        if (disabledUnits.Length == 0)
            return command;

        var guarded = new List<string>
        {
            "powerforge_package_status=0",
            $"bash -Eeuo pipefail -c {ShellQuote(command)} || powerforge_package_status=$?",
            "powerforge_reconcile_status=0"
        };
        // A failed unit query must not prevent the remaining units from being stopped.
        guarded.AddRange(disabledUnits.Select(unit =>
            $"( {BuildStopAndDisableUnitCommand(unit.Name!)} ) || powerforge_reconcile_status=3"));
        guarded.Add("if [ \"$powerforge_reconcile_status\" -ne 0 ]; then exit \"$powerforge_reconcile_status\"; fi");
        guarded.Add("if [ \"$powerforge_package_status\" -ne 0 ]; then exit \"$powerforge_package_status\"; fi");
        return string.Join('\n', guarded);
    }
}
