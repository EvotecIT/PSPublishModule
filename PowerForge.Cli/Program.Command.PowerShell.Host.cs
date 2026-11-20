using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string PowerShellCheckUsage =
        "Usage: powerforge powershell check <path> [--kind <exe|dll|library>] [--mode <Package|Hybrid|Strict>] [--framework <tfm>] [--target-contract <target.json>] [--resource-mode <Declared|CompleteModule|None>] [--include-resource <pattern>] [--exclude-resource <pattern>] [--out <directory>] [--write-review <review.json>] [--output json]";

    private const string PowerShellDoctorUsage =
        "Usage: powerforge powershell doctor [--target-contract <target.json>] [--output json]";

    private static int CommandPowerShellCheck(string[] args, bool outputJson, ILogger logger)
    {
        const string command = "powershell.check";
        if (!TryValidatePowerShellArguments(args,
                new[] { "--kind", "--mode", "--framework", "--semantic-profile", "--target-contract", "--resource-mode",
                    "--include-resource", "--exclude-resource", "--out", "--write-review", "--output" },
                new[] { "--json", "--output-json" }, out var path, out var error))
            return WritePowerShellError(outputJson, 2, error, logger, command);
        if (!TryParsePowerShellAnalysisRequest(args, path, out var analysis, out error))
            return WritePowerShellError(outputJson, 2, error, logger, command);

        var result = new PowerShellCompilationHostService().Check(new PowerShellCompilationCheckRequest(analysis!.Path)
        {
            Kind = analysis.Kind,
            Mode = analysis.Mode == PowerShellCompilationMode.Analyze ? null : analysis.Mode,
            Target = analysis.TargetContract,
            TargetFramework = TryGetOptionValue(args, "--framework") ?? PowerShellCompilationTargetFrameworkPolicy.Default,
            SemanticProfileId = analysis.SemanticProfileId,
            ResourceMode = analysis.ResourceMode,
            IncludeResource = GetOptionValues(args, "--include-resource").ToArray(),
            ExcludeResource = GetOptionValues(args, "--exclude-resource").ToArray(),
            OutputDirectory = TryGetOptionValue(args, "--out")
        });
        var reviewPath = TryGetOptionValue(args, "--write-review");
        if (result.ProposedReview is not null && reviewPath is not null)
        {
            try
            {
                PowerShellCompilationReviewFile.Write(reviewPath, result.ProposedReview, result.ModuleSourceDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return WritePowerShellError(outputJson, 1, $"Could not save the proposed review: {exception.Message}", logger, command);
            }
        }

        if (outputJson)
            return WritePowerShellHostResult(command, result.CanProceed,
                CliJson.SerializeToElement(result, CliJson.Context.PowerShellCompilationCheckResult));
        if (result.Explanation is { } explanation)
            logger.Info($"{explanation.Mode}: {explanation.TypedUnits} typed units, {explanation.RuntimeFallbackUnits} runtime-routed units, {explanation.RejectedUnits} rejected units.");
        WritePowerShellHostDiagnostics(result.Diagnostics, logger);
        foreach (var dependency in result.Dependencies)
            logger.Info($"{dependency.Disposition}: {dependency.RelativePath}");
        if (result.CanProceed && reviewPath is not null)
            logger.Info($"Review the reported dependencies before building with --reviewed-check \"{reviewPath}\".");
        return result.CanProceed ? 0 : 1;
    }

    private static int CommandPowerShellDoctor(string[] args, bool outputJson, ILogger logger)
    {
        const string command = "powershell.doctor";
        if (!TryValidatePowerShellArguments(args, new[] { "--target-contract", "--output" },
                new[] { "--json", "--output-json" }, out var path, out var error))
            return WritePowerShellError(outputJson, 2, error, logger, command);
        if (path is not null)
            return WritePowerShellError(outputJson, 2, PowerShellDoctorUsage, logger, command);
        try
        {
            var targetPath = TryGetOptionValue(args, "--target-contract");
            var target = targetPath is null
                ? null
                : JsonSerializer.Deserialize(File.ReadAllText(targetPath), CliJson.Context.PowerShellCompilationTargetContract)
                  ?? throw new InvalidDataException("The target contract was empty.");
            var result = new PowerShellCompilationHostService().Doctor(target);
            if (outputJson)
                return WritePowerShellHostResult(command, result.Succeeded,
                    CliJson.SerializeToElement(result, CliJson.Context.PowerShellCompilationDoctorResult));
            if (result.Succeeded)
                logger.Info($"Selected .NET SDK: {result.DotNetSdkVersion}");
            WritePowerShellHostDiagnostics(result.Diagnostics, logger);
            return result.Succeeded ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return WritePowerShellError(outputJson, 1, exception.Message, logger, command);
        }
    }

    private static int WritePowerShellHostResult(string command, bool success, JsonElement result)
    {
        var exitCode = success ? 0 : 1;
        WriteJson(new CliJsonEnvelope
        {
            SchemaVersion = OutputSchemaVersion,
            Command = command,
            Success = success,
            ExitCode = exitCode,
            Result = result
        });
        return exitCode;
    }

    private static void WritePowerShellHostDiagnostics(IEnumerable<PowerShellCompilationHostDiagnostic> diagnostics, ILogger logger)
    {
        foreach (var diagnostic in diagnostics)
        {
            var location = diagnostic.Line > 0 ? $"{diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column}: " : string.Empty;
            var message = $"{location}{diagnostic.Message} {diagnostic.Fix}";
            if (diagnostic.Severity == PowerShellCompilationHostSeverity.Error)
                logger.Error(message);
            else
                logger.Warn(message);
        }
    }
}
