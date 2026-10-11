using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private static int CommandAppleReleaseEvidence(string[] argv, bool outputJson, ILogger logger)
    {
        AppleReleaseLocalEvidence result;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var index = 0; index < argv.Length; index++)
            {
                var option = argv[index];
                if (option != "--config" && option != "--source-commit" && option != "--output")
                    throw new ArgumentException($"Unknown apple-release evidence option '{option}'.");
                if (values.ContainsKey(option))
                    throw new ArgumentException($"Duplicate apple-release evidence option '{option}'.");
                if (++index >= argv.Length || argv[index].StartsWith("-", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for apple-release evidence option '{option}'.");
                values.Add(option, argv[index]);
            }
            if (!values.TryGetValue("--config", out var configPath) || string.IsNullOrWhiteSpace(configPath))
                throw new ArgumentException("Apple-release evidence requires --config.");
            if (!values.TryGetValue("--source-commit", out var sourceCommit) || string.IsNullOrWhiteSpace(sourceCommit))
                throw new ArgumentException("Apple-release evidence requires --source-commit.");
            if (values.TryGetValue("--output", out var output) && !output.Equals("json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Apple-release evidence supports --output json.");
            result = new PowerForgeReleaseService(logger).ValidateLocalAppleEvidence(configPath, sourceCommit);
        }
        catch (Exception exception)
        {
            result = new AppleReleaseLocalEvidence
            {
                SourceCommit = values.TryGetValue("--source-commit", out var source) ? source : string.Empty,
                RejectionPhase = AppleReleaseLocalEvidenceRejectionPhase.ConfigurationValidation,
                ErrorMessage = exception.Message
            };
        }
        var exitCode = result.Success ? 0 : 1;
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "apple-release",
                Success = result.Success,
                ExitCode = exitCode,
                Error = result.ErrorMessage,
                Result = CliJson.SerializeToElement(result, CliJson.Context.AppleReleaseLocalEvidence)
            });
        }
        else if (result.Success)
        {
            foreach (var path in result.EvidencePaths)
                Console.WriteLine(path);
        }
        else
            logger.Error(result.ErrorMessage ?? "Local Apple evidence validation failed.");
        return exitCode;
    }
}
