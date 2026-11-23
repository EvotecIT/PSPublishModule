using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string PowerShellCheckUsage =
        "Usage: powerforge powershell check <path> [--path <additional.ps1> ...] [--entry-point <main.ps1>] [--kind <exe|dll|library>] [--mode <Package|Hybrid|Strict>] [--framework <tfm>] [--target-contract <target.json>] [--semantic-profile <id>] [--rid <rid>] [--self-contained] [--optimization <None|Trimmed|NativeAot>] [--no-single-file] [--resource-mode <Declared|CompleteModule|None>] [--include-resource <pattern>] [--exclude-resource <pattern>] [--out <directory>] [--name <artifact>] [--write-review <review.json>] [--output json]";

    private const string PowerShellDoctorUsage =
        "Usage: powerforge powershell doctor [--target-contract <target.json>] [--output json]";

    private static int CommandPowerShellCheck(string[] args, bool outputJson, ILogger logger)
    {
        const string command = "powershell.check";
        if (!TryValidatePowerShellArguments(args, PowerShellSelectionOptions.Append("--write-review").ToArray(),
                PowerShellSelectionFlags, out var path, out var error))
            return WritePowerShellError(outputJson, 2, error, logger, command);

        PowerShellCompilationCheckResult result;
        try
        {
            if (!TryParsePowerShellSelection(args, path, out var selection, out error, allowAnalyzeAlias: true))
                return WritePowerShellError(outputJson, 2, error, logger, command);
            result = new PowerShellCompiler().Check(selection!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return WritePowerShellError(outputJson, 2, exception.Message, logger, command);
        }

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
            var target = targetPath is null ? null : ReadPowerShellTargetContract(targetPath);
            var result = new PowerShellCompiler().CheckEnvironment(target);
            if (outputJson)
                return WritePowerShellHostResult(command, result.Succeeded,
                    CliJson.SerializeToElement(result, CliJson.Context.PowerShellCompilationEnvironmentResult));
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
