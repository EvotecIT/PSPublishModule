namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static readonly char[] AllowedSystemdUnitNameCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:_.@-".ToCharArray();

    private static void ValidateSystemdActivation(
        PowerForgeServerSystemd? systemd,
        ICollection<string> errors)
    {
        ValidateSystemdUnits(systemd?.Services, ".service", "systemd.services", errors);
        ValidateSystemdUnits(systemd?.Timers, ".timer", "systemd.timers", errors);
    }

    private static void ValidateSystemdUnits(
        IEnumerable<PowerForgeServerSystemdUnit>? units,
        string requiredSuffix,
        string path,
        ICollection<string> errors)
    {
        var index = 0;
        foreach (var unit in units ?? Array.Empty<PowerForgeServerSystemdUnit>())
        {
            if (!IsValidSystemdUnitName(unit.Name, requiredSuffix))
                errors.Add($"{path}[{index}].name must be a safe {requiredSuffix} unit name.");

            if (!string.IsNullOrWhiteSpace(unit.Activation))
            {
                if (unit.Activation is not (PowerForgeServerSystemdActivation.BeforeDeploy or PowerForgeServerSystemdActivation.AfterDeploy))
                    errors.Add($"Systemd unit '{unit.Name}' has unsupported activation phase '{unit.Activation}'.");
                if (!unit.Enabled)
                    errors.Add($"Systemd unit '{unit.Name}' must be enabled when activation is declared.");
            }

            if (unit.ExpectedState is not null)
            {
                if (unit.ExpectedState is not (PowerForgeServerSystemdState.Active or PowerForgeServerSystemdState.Inactive))
                    errors.Add($"Systemd unit '{unit.Name}' has unsupported expected state '{unit.ExpectedState}'.");
                if (string.IsNullOrWhiteSpace(unit.Activation))
                    errors.Add($"Systemd unit '{unit.Name}' must declare activation when expectedState is declared.");
            }

            index++;
        }
    }

    private static bool IsValidSystemdUnitName(string? name, string requiredSuffix)
        => !string.IsNullOrWhiteSpace(name) &&
           name.Length <= 255 &&
           name.EndsWith(requiredSuffix, StringComparison.Ordinal) &&
           IsAsciiLetterOrDigit(name[0]) &&
           name.All(character => Array.IndexOf(AllowedSystemdUnitNameCharacters, character) >= 0);

    private static void ValidateNamedCommandText(
        IEnumerable<PowerForgeServerNamedCommand>? commands,
        string path,
        ICollection<string> errors)
    {
        var index = 0;
        foreach (var command in commands ?? Array.Empty<PowerForgeServerNamedCommand>())
        {
            if (string.IsNullOrWhiteSpace(command.Command))
                errors.Add($"{path}[{index}].command must contain a non-whitespace command.");
            index++;
        }
    }
}
