using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private static int CommandPowerShellDiagnose(string[] args, bool outputJson, ILogger logger)
    {
        if (args.Any(IsHelpArgument))
        {
            if (outputJson)
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.diagnose",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new { usage = PowerShellDiagnoseUsage })
                });
            else
                Console.WriteLine(PowerShellDiagnoseUsage);
            return 0;
        }

        if (!TryValidatePowerShellArguments(
                args,
                new[] { "--failure", "--output" },
                new[] { "--json", "--output-json" },
                out var manifestPath,
                out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.diagnose");
        var failurePath = TryGetOptionValue(args, "--failure");
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(failurePath))
            return WritePowerShellError(outputJson, 2, "A compilation manifest and --failure log are required.", logger, "powershell.diagnose");

        try
        {
            var fullManifestPath = Path.GetFullPath(manifestPath.Trim().Trim('"'));
            var fullFailurePath = Path.GetFullPath(failurePath.Trim().Trim('"'));
            var manifest = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(
                    File.ReadAllText(fullManifestPath),
                    CliJson.Options)
                ?? throw new InvalidDataException($"Compilation manifest '{fullManifestPath}' was empty.");
            PowerShellCompilationArtifactEvidence.Validate(manifest);
            var failure = PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest, File.ReadAllText(fullFailurePath));
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.diagnose",
                    Success = true,
                    ExitCode = 0,
                    Result = CliJson.SerializeToElement(failure, CliJson.Context.PowerShellCompilationFailure)
                });
                return 0;
            }

            logger.Info($"Stage: {failure.Stage}; reason: {failure.Reason}");
            logger.Info(failure.Summary);
            foreach (var location in failure.Locations)
                logger.Info($"{location.RelativePath}:{location.Line}:{location.Column} [{location.Code}] {location.UnitName} ({location.BoundaryContract})");
            if (failure.Locations.Length == 0)
                logger.Warn("No authored location matched the supplied runtime evidence.");
            return 0;
        }
        catch (Exception ex)
        {
            return WritePowerShellError(outputJson, 1, ex.Message, logger, "powershell.diagnose");
        }
    }
}
