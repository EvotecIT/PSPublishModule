using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
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
            AddStep(steps, ref order, "systemd", $"Enable {unit.Name}", $"systemctl enable {ShellQuote(unit.Name!)}", plannedCommands: plannedCommands);
            AddStep(steps, ref order, "systemd", $"Start {unit.Name}", $"systemctl start {ShellQuote(unit.Name!)}", plannedCommands: plannedCommands);
        }
    }
}
