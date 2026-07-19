namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static void ValidateSystemdActivation(
        PowerForgeServerSystemd? systemd,
        ICollection<string> errors)
    {
        var units = (systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
            .Concat(systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>());
        foreach (var unit in units)
        {
            if (string.IsNullOrWhiteSpace(unit.Activation))
                continue;

            if (unit.Activation is not (PowerForgeServerSystemdActivation.BeforeDeploy or PowerForgeServerSystemdActivation.AfterDeploy))
                errors.Add($"Systemd unit '{unit.Name}' has unsupported activation phase '{unit.Activation}'.");
            if (!unit.Enabled)
                errors.Add($"Systemd unit '{unit.Name}' must be enabled when activation is declared.");
        }
    }
}
