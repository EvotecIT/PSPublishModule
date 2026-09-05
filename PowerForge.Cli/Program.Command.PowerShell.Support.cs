using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string PowerShellSupportUsage =
        "Usage: powerforge powershell support [--output json]";

    private static int CommandPowerShellSupport(string[] args, bool outputJson, ILogger logger)
    {
        if (!TryValidatePowerShellArguments(
                args,
                new[] { "--output" },
                new[] { "--json", "--output-json" },
                out var positional,
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.support");
        if (positional is not null)
            return WritePowerShellError(outputJson, 2, PowerShellSupportUsage, logger, "powershell.support");
        var matrix = PowerShellCompilationSupportMatrixService.Create();
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "powershell.support",
                Success = true,
                ExitCode = 0,
                Result = CliJson.SerializeToElement(matrix, CliJson.Context.PowerShellCompilationSupportMatrix)
            });
            return 0;
        }
        logger.Info($"PowerShell compilation toolchain channel: {matrix.ToolchainChannel}");
        foreach (var profile in matrix.Profiles.Where(static profile => profile.Advertised))
            logger.Info($"{profile.SupportLevel,-15} {profile.Id} (requires {profile.RuntimeRequirement})");
        logger.Info($"Experimental exact profiles: {matrix.Profiles.Count(static profile => !profile.Advertised)}");
        return 0;
    }
}
