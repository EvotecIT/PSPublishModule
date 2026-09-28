using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static string BuildStopAndDisableUnitCommand(string unitName)
    {
        var name = ShellQuote(unitName);
        var queryFailure = ShellQuote($"Cannot inspect systemd unit before bootstrap: {unitName}");
        var unknownState = ShellQuote($"Unexpected systemd load state before bootstrap: {unitName}");
        return $"powerforge_unit_load_state=$(systemctl show --property=LoadState --value -- {name}) || " +
               $"{{ echo {queryFailure} >&2; exit 3; }}; " +
               "case \"$powerforge_unit_load_state\" in " +
               $"not-found) ;; loaded|masked) systemctl disable --now -- {name} ;; " +
               $"*) echo {unknownState} >&2; exit 3 ;; esac";
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
