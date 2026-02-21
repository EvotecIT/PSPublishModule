using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteEngineLock(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var operation = (GetString(step, "operation") ??
                         GetString(step, "action") ??
                         GetString(step, "lockMode") ??
                         GetString(step, "lock-mode") ??
                         "verify").Trim();
        var continueOnError = GetBool(step, "continueOnError") ?? false;
        var failOnDrift = GetBool(step, "failOnDrift") ?? GetBool(step, "fail-on-drift") ?? true;
        var requireImmutableRef = GetBool(step, "requireImmutableRef") ??
                                  GetBool(step, "require-immutable-ref") ??
                                  GetBool(step, "requireSha") ??
                                  GetBool(step, "require-sha") ??
                                  false;
        var useEnv = GetBool(step, "useEnv") ?? GetBool(step, "use-env") ?? GetBool(step, "env") ?? false;

        var repository = GetString(step, "expectedRepository") ??
                         GetString(step, "expected-repository") ??
                         GetString(step, "repository") ??
                         GetString(step, "repo");
        var @ref = GetString(step, "expectedRef") ?? GetString(step, "expected-ref") ?? GetString(step, "ref");
        var channel = GetString(step, "expectedChannel") ?? GetString(step, "expected-channel") ?? GetString(step, "channel");
        var repositoryEnv = GetString(step, "repositoryEnv") ?? GetString(step, "repository-env") ?? "POWERFORGE_REPOSITORY";
        var refEnv = GetString(step, "refEnv") ?? GetString(step, "ref-env") ?? "POWERFORGE_REF";
        var channelEnv = GetString(step, "channelEnv") ?? GetString(step, "channel-env") ?? "POWERFORGE_CHANNEL";

        if (useEnv)
        {
            repository ??= Environment.GetEnvironmentVariable(repositoryEnv);
            @ref ??= Environment.GetEnvironmentVariable(refEnv);
            channel ??= Environment.GetEnvironmentVariable(channelEnv);
        }

        var lockPathValue = GetString(step, "path") ??
                            GetString(step, "lockPath") ??
                            GetString(step, "lock-path") ??
                            GetString(step, "lock") ??
                            Path.Combine(".powerforge", "engine-lock.json");
        var lockPath = ResolvePathWithinRoot(baseDir, lockPathValue, Path.Combine(".powerforge", "engine-lock.json"));

        var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
        var summaryPath = GetString(step, "summaryPath") ?? GetString(step, "summary-path");
        var reportResult = new WebEngineLockResult
        {
            Path = lockPath,
            Mode = operation
        };
        var reportMessage = string.Empty;
        var reportSuccess = false;

        try
        {
            var resolvedResult = operation.ToLowerInvariant() switch
            {
                "show" => ExecuteEngineLockShow(lockPath, operation),
                "verify" => ExecuteEngineLockVerify(lockPath, operation, repository, @ref, channel, failOnDrift, requireImmutableRef),
                "update" => ExecuteEngineLockUpdate(lockPath, operation, repository, @ref, channel, requireImmutableRef),
                _ => throw new InvalidOperationException($"engine-lock: unsupported operation '{operation}'. Supported values: show, verify, update.")
            };

            reportResult = resolvedResult;
            reportSuccess = true;
            reportMessage = BuildEngineLockPipelineMessage(resolvedResult);

            stepResult.Success = true;
            stepResult.Message = reportMessage;
        }
        catch (Exception ex)
        {
            reportSuccess = false;
            reportMessage = ex.Message;
            stepResult.Success = continueOnError;
            stepResult.Message = continueOnError
                ? $"engine-lock allowed failure: {ex.Message}"
                : $"engine-lock failed: {ex.Message}";

            if (!continueOnError)
                throw;
        }
        finally
        {
            WriteEngineLockPipelineArtifacts(baseDir, reportPath, summaryPath, reportSuccess, reportMessage, reportResult);
        }
    }

    private static WebEngineLockResult ExecuteEngineLockShow(string lockPath, string operation)
    {
        var lockSpec = WebEngineLockFile.Read(lockPath, WebCliJson.Options);
        return new WebEngineLockResult
        {
            Path = lockPath,
            Mode = operation,
            Exists = true,
            Repository = lockSpec.Repository,
            Ref = lockSpec.Ref,
            ImmutableRef = WebEngineLockFile.IsCommitSha(lockSpec.Ref),
            Channel = lockSpec.Channel,
            UpdatedUtc = lockSpec.UpdatedUtc
        };
    }

    private static WebEngineLockResult ExecuteEngineLockVerify(
        string lockPath,
        string operation,
        string? expectedRepository,
        string? expectedRef,
        string? expectedChannel,
        bool failOnDrift,
        bool requireImmutableRef)
    {
        var lockSpec = WebEngineLockFile.Read(lockPath, WebCliJson.Options);
        var validation = WebEngineLockFile.Validate(lockSpec);
        if (validation.Length > 0)
            throw new InvalidOperationException($"engine-lock verify failed: {string.Join(" ", validation)}");
        if (requireImmutableRef && !WebEngineLockFile.IsCommitSha(lockSpec.Ref))
            throw new InvalidOperationException($"engine-lock verify failed: lock ref '{lockSpec.Ref}' is not an immutable commit SHA (40/64 hex).");

        var driftReasons = new List<string>();
        AddEngineLockPipelineDriftIfAny(driftReasons, "repository", lockSpec.Repository, expectedRepository);
        AddEngineLockPipelineDriftIfAny(driftReasons, "ref", lockSpec.Ref, expectedRef);
        AddEngineLockPipelineDriftIfAny(driftReasons, "channel", lockSpec.Channel, expectedChannel);

        if (driftReasons.Count > 0 && failOnDrift)
            throw new InvalidOperationException($"engine-lock drift detected: {string.Join(" ", driftReasons)}");

        return new WebEngineLockResult
        {
            Path = lockPath,
            Mode = operation,
            Exists = true,
            Repository = lockSpec.Repository,
            Ref = lockSpec.Ref,
            ImmutableRef = WebEngineLockFile.IsCommitSha(lockSpec.Ref),
            Channel = lockSpec.Channel,
            UpdatedUtc = lockSpec.UpdatedUtc,
            DriftDetected = driftReasons.Count > 0,
            DriftReasons = driftReasons.ToArray()
        };
    }

    private static WebEngineLockResult ExecuteEngineLockUpdate(
        string lockPath,
        string operation,
        string? repository,
        string? @ref,
        string? channel,
        bool requireImmutableRef)
    {
        var candidate = File.Exists(lockPath)
            ? WebEngineLockFile.Read(lockPath, WebCliJson.Options)
            : WebEngineLockFile.CreateDefault();

        if (!string.IsNullOrWhiteSpace(repository))
            candidate.Repository = repository.Trim();
        if (!string.IsNullOrWhiteSpace(@ref))
            candidate.Ref = @ref.Trim();
        if (!string.IsNullOrWhiteSpace(channel))
            candidate.Channel = channel.Trim();

        var normalized = WebEngineLockFile.Normalize(candidate, stampUpdatedUtc: true);
        var validation = WebEngineLockFile.Validate(normalized);
        if (validation.Length > 0)
            throw new InvalidOperationException($"engine-lock update failed: {string.Join(" ", validation)}");
        if (requireImmutableRef && !WebEngineLockFile.IsCommitSha(normalized.Ref))
            throw new InvalidOperationException($"engine-lock update failed: ref '{normalized.Ref}' is not an immutable commit SHA (40/64 hex).");

        WebEngineLockFile.Write(lockPath, normalized, WebCliJson.Options);
        var saved = WebEngineLockFile.Read(lockPath, WebCliJson.Options);

        return new WebEngineLockResult
        {
            Path = lockPath,
            Mode = operation,
            Exists = true,
            Repository = saved.Repository,
            Ref = saved.Ref,
            ImmutableRef = WebEngineLockFile.IsCommitSha(saved.Ref),
            Channel = saved.Channel,
            UpdatedUtc = saved.UpdatedUtc
        };
    }

    private static void AddEngineLockPipelineDriftIfAny(List<string> driftReasons, string name, string actualValue, string? expectedValue)
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
            return;

        var expected = expectedValue.Trim();
        if (actualValue.Equals(expected, StringComparison.Ordinal))
            return;

        driftReasons.Add($"expected {name} '{expected}' but lock has '{actualValue}'.");
    }

    private static string BuildEngineLockPipelineMessage(WebEngineLockResult result)
    {
        var mode = string.IsNullOrWhiteSpace(result.Mode) ? "verify" : result.Mode;
        var baseMessage = $"engine-lock {mode}: {result.Repository}@{result.Ref}";
        if (!result.DriftDetected)
            return baseMessage;

        if (result.DriftReasons.Length == 0)
            return $"{baseMessage} (drift detected)";

        return $"{baseMessage} (drift: {string.Join(" ", result.DriftReasons)})";
    }

    private static void WriteEngineLockPipelineArtifacts(
        string baseDir,
        string? reportPath,
        string? summaryPath,
        bool success,
        string message,
        WebEngineLockResult result)
    {
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var resolvedReportPath = ResolvePathWithinRoot(baseDir, reportPath, reportPath);
            var reportDirectory = Path.GetDirectoryName(resolvedReportPath);
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);

            var payload = new
            {
                success,
                message,
                result
            };
            File.WriteAllText(resolvedReportPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
        }

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            var resolvedSummaryPath = ResolvePathWithinRoot(baseDir, summaryPath, summaryPath);
            var summaryDirectory = Path.GetDirectoryName(resolvedSummaryPath);
            if (!string.IsNullOrWhiteSpace(summaryDirectory))
                Directory.CreateDirectory(summaryDirectory);

            var markdown = BuildEngineLockSummary(success, message, result);
            File.WriteAllText(resolvedSummaryPath, markdown);
        }
    }

    private static string BuildEngineLockSummary(bool success, string message, WebEngineLockResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Engine Lock");
        builder.AppendLine();
        builder.AppendLine($"- Result: {(success ? "pass" : "fail")}");
        builder.AppendLine($"- Operation: {result.Mode}");
        builder.AppendLine($"- Path: `{result.Path}`");
        if (!string.IsNullOrWhiteSpace(result.Repository))
            builder.AppendLine($"- Repository: `{result.Repository}`");
        if (!string.IsNullOrWhiteSpace(result.Ref))
            builder.AppendLine($"- Ref: `{result.Ref}`");
        builder.AppendLine($"- Immutable ref: {(result.ImmutableRef ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(result.Channel))
            builder.AppendLine($"- Channel: `{result.Channel}`");
        if (!string.IsNullOrWhiteSpace(result.UpdatedUtc))
            builder.AppendLine($"- Updated UTC: `{result.UpdatedUtc}`");
        if (result.DriftDetected)
            builder.AppendLine("- Drift detected: yes");
        builder.AppendLine();
        builder.AppendLine(message);

        if (result.DriftReasons.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Drift reasons");
            foreach (var reason in result.DriftReasons)
                builder.AppendLine($"- {reason}");
        }

        return builder.ToString();
    }
}
