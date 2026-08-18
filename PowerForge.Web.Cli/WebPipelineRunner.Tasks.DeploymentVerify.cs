using System.Text;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteDeploymentVerify(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var manifestValue = GetString(step, "manifestPath") ??
                            GetString(step, "manifest-path") ??
                            Environment.GetEnvironmentVariable("POWERFORGE_DEPLOYMENT_MANIFEST");
        var manifestPath = ResolvePath(baseDir, manifestValue);
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new InvalidOperationException("deployment-verify: manifestPath is required when POWERFORGE_DEPLOYMENT_MANIFEST is not set.");

        var manifest = CloudflareDeploymentManifestStore.LoadRequired(manifestPath);
        var options = new DeploymentArtifactVerificationOptions
        {
            BaseUrl = GetString(step, "baseUrl") ??
                      GetString(step, "base-url") ??
                      Environment.GetEnvironmentVariable("POWERFORGE_DEPLOYMENT_BASE_URL"),
            PathPrefixes = ReadStringList(step, "pathPrefixes", "path-prefixes", "pathPrefix", "path-prefix").ToArray(),
            Attempts = GetInt(step, "attempts") ?? 3,
            DelayMilliseconds = GetInt(step, "delayMs") ?? GetInt(step, "delay-ms") ?? 5000,
            RequestAttempts = GetInt(step, "requestAttempts") ?? GetInt(step, "request-attempts") ?? 2,
            RequestRetryDelayMilliseconds = GetInt(step, "requestRetryDelayMs") ?? GetInt(step, "request-retry-delay-ms") ?? 250,
            TimeoutMilliseconds = GetInt(step, "timeoutMs") ?? GetInt(step, "timeout-ms") ?? 30000,
            MaxFiles = GetInt(step, "maxFiles") ?? GetInt(step, "max-files") ?? 50_000,
            MaxResponseBytes = GetLong(step, "maxResponseBytes") ?? GetLong(step, "max-response-bytes") ?? 256L * 1024L * 1024L,
            MaxTotalBytes = GetLong(step, "maxTotalBytes") ?? GetLong(step, "max-total-bytes") ?? 8L * 1024L * 1024L * 1024L
        };

        var result = DeploymentArtifactVerifier.Verify(manifest, options);
        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));
        if (!string.IsNullOrWhiteSpace(reportPath))
            WriteDeploymentVerifyReport(reportPath, result);

        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"));
        if (!string.IsNullOrWhiteSpace(summaryPath))
            WriteDeploymentVerifySummary(summaryPath, result);

        stepResult.Success = result.Success;
        stepResult.Message = result.Message;
        if (!result.Success)
            throw new InvalidOperationException(result.Message);
    }

    private static void WriteDeploymentVerifyReport(string reportPath, DeploymentArtifactVerificationResult result)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteDeploymentVerifySummary(string summaryPath, DeploymentArtifactVerificationResult result)
    {
        var directory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("# Deployment Artifact Verification");
        builder.AppendLine();
        builder.AppendLine($"- Result: {(result.Success ? "pass" : "fail")}");
        builder.AppendLine($"- Base URL: {result.BaseUrl}");
        builder.AppendLine($"- Selected files: {result.SelectedFileCount}");
        builder.AppendLine($"- Expected bytes: {result.SelectedBytes}");
        builder.AppendLine($"- Attempts: {result.AttemptsCompleted} of {result.AttemptsConfigured}");
        if (result.PathPrefixes.Length > 0)
            builder.AppendLine($"- Path prefixes: {string.Join(", ", result.PathPrefixes)}");
        builder.AppendLine();
        builder.AppendLine(result.Message);
        builder.AppendLine();
        builder.AppendLine("| Attempt | Result | Downloaded bytes | Error |");
        builder.AppendLine("| ---: | --- | ---: | --- |");
        foreach (var attempt in result.Attempts)
        {
            var error = (attempt.Error ?? string.Empty).Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
            builder.AppendLine($"| {attempt.Number} | {(attempt.Success ? "pass" : "fail")} | {attempt.DownloadedBytes} | {error} |");
        }
        File.WriteAllText(summaryPath, builder.ToString());
    }
}
