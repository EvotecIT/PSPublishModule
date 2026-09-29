using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static void AddEarlyDisabledSystemdSteps(
        ICollection<PowerForgeServerBootstrapPlanStep> steps,
        ref int order,
        PowerForgeServerSystemd? systemd,
        ISet<string> plannedCommands)
    {
        AddDisabledSystemdSteps(steps, ref order, systemd, plannedCommands, "Stop and disable", repeat: false);
    }

    private static void AddDisabledSystemdSteps(
        ICollection<PowerForgeServerBootstrapPlanStep> steps,
        ref int order,
        PowerForgeServerSystemd? systemd,
        ISet<string> plannedCommands,
        string titlePrefix,
        bool repeat)
    {
        // Timers must stop before their services, otherwise a timer may restart a stopped service.
        var units = (systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
            .Concat(systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>());
        foreach (var unit in units.Where(static unit => unit.EnforceDisabled && !string.IsNullOrWhiteSpace(unit.Name)))
        {
            AddStep(steps, ref order, "systemd", $"{titlePrefix} {unit.Name}",
                BuildStopAndDisableUnitCommand(unit.Name!),
                // Repeated safety passes must not be removed by command de-duplication.
                plannedCommands: repeat ? null : plannedCommands);
        }
    }

    private static string BuildStopAndDisableUnitCommand(string unitName)
    {
        var name = ShellQuote(unitName);
        var queryFailure = ShellQuote($"Cannot inspect systemd unit before bootstrap: {unitName}");
        var unknownState = ShellQuote($"Unexpected systemd load state before bootstrap: {unitName}");
        var danglingEnablement = ShellQuote($"Absent systemd unit has an enablement link; remove it before bootstrap: {unitName}");
        var absentGuard = "powerforge_unit_roots=$(systemd-analyze unit-paths) || exit 3; " +
                          "while IFS= read -r powerforge_unit_root; do " +
                          "[ -d \"$powerforge_unit_root\" ] || continue; " +
                          $"powerforge_unit_link=$(find -P \"$powerforge_unit_root\" -type l -name {name} -print -quit) || exit 3; " +
                          $"test -z \"$powerforge_unit_link\" || {{ echo {danglingEnablement} >&2; exit 3; }}; " +
                          "done <<< \"$powerforge_unit_roots\"";
        return $"powerforge_unit_load_state=$(systemctl show --property=LoadState --value -- {name}) || " +
               $"{{ echo {queryFailure} >&2; exit 3; }}; " +
               "case \"$powerforge_unit_load_state\" in " +
               $"not-found) {absentGuard} ;; loaded|masked) " +
               $"systemctl disable --runtime --now -- {name} && systemctl disable --now -- {name} && " +
               $"powerforge_unit_file_state=$(systemctl show --property=UnitFileState --value -- {name}) && " +
               $"powerforge_unit_active_state=$(systemctl show --property=ActiveState --value -- {name}) && " +
               "case \"$powerforge_unit_file_state\" in disabled|masked|masked-runtime) ;; *) exit 3 ;; esac && " +
               "case \"$powerforge_unit_active_state\" in inactive|failed) ;; *) exit 3 ;; esac ;; " +
               $"*) echo {unknownState} >&2; exit 3 ;; esac";
    }

    private static string BuildBootstrapDisabledUnitExitTrap(PowerForgeServerSystemd? systemd)
    {
        var units = (systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
            .Concat(systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
            .Where(static unit => unit.EnforceDisabled && !string.IsNullOrWhiteSpace(unit.Name))
            .ToArray();
        if (units.Length == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "powerforge_reconcile_disabled_on_exit() {",
            "  powerforge_original_status=$?",
            "  trap - EXIT",
            "  powerforge_reconcile_status=0"
        };
        // A failed unit check must not prevent attempts on the remaining units.
        lines.AddRange(units.Select(unit =>
            $"  ( {BuildStopAndDisableUnitCommand(unit.Name!)} ) || powerforge_reconcile_status=3"));
        lines.Add("  if [ \"$powerforge_original_status\" -ne 0 ]; then exit \"$powerforge_original_status\"; fi");
        lines.Add("  exit \"$powerforge_reconcile_status\"");
        lines.Add("}");
        lines.Add("trap powerforge_reconcile_disabled_on_exit EXIT");
        return string.Join('\n', lines);
    }

    private static void EnsureDisabledUnitBootstrapLockOwnership(PowerForgeServerRecoveryManifest manifest)
    {
        var enforced = (manifest.Systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
            .Concat(manifest.Systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
            .Any(static unit => unit.EnforceDisabled && !string.IsNullOrWhiteSpace(unit.Name));
        if (enforced && (manifest.OperationLocks?.Length ?? 0) > 0 &&
            string.Equals(manifest.Deploy?.OperationLockOwner, "command", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Bootstrap cannot enforce disabled units with command-owned deployment locks; use engine-owned locks so final reconciliation remains protected.");
    }

    private static void AddSystemdActivationSteps(
        ICollection<PowerForgeServerBootstrapPlanStep> steps,
        ref int order,
        IEnumerable<PowerForgeServerSystemdUnit> units,
        string activation,
        ISet<string> plannedCommands)
    {
        foreach (var unit in units.Where(unit =>
                     unit.Enabled &&
                     string.Equals(unit.Activation, activation, StringComparison.Ordinal) &&
                     !string.IsNullOrWhiteSpace(unit.Name)))
        {
            AddStep(steps, ref order, "systemd", $"Enable {unit.Name}", $"systemctl enable -- {ShellQuote(unit.Name!)}", plannedCommands: plannedCommands);
            AddStep(steps, ref order, "systemd", $"Start {unit.Name}", $"systemctl start -- {ShellQuote(unit.Name!)}", plannedCommands: plannedCommands);
        }
    }
}
