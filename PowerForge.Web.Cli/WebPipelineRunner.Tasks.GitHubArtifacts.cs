using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PowerForge;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteGitHubArtifactsPrune(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var continueOnError = GetBool(step, "continueOnError") ?? false;
        var optional = GetBool(step, "optional") ?? false;
        var optionalRepository = GetBool(step, "optionalRepository") ??
                                 GetBool(step, "optional-repository") ??
                                 GetBool(step, "skipIfMissingRepository") ??
                                 GetBool(step, "skip-if-missing-repository") ??
                                 optional;
        var optionalToken = GetBool(step, "optionalToken") ??
                            GetBool(step, "optional-token") ??
                            GetBool(step, "skipIfMissingToken") ??
                            GetBool(step, "skip-if-missing-token") ??
                            optional;

        var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
        var summaryPath = GetString(step, "summaryPath") ?? GetString(step, "summary-path");

        var repository = GetString(step, "repo") ??
                         GetString(step, "repository") ??
                         GetString(step, "githubRepository") ??
                         GetString(step, "github-repository");
        var repoEnv = GetString(step, "repoEnv") ?? GetString(step, "repo-env") ?? "GITHUB_REPOSITORY";
        if (string.IsNullOrWhiteSpace(repository))
            repository = Environment.GetEnvironmentVariable(repoEnv);
        if (string.IsNullOrWhiteSpace(repository))
        {
            if (!optionalRepository)
                throw new InvalidOperationException($"github-artifacts-prune: missing repository (set '{repoEnv}' or provide repo/repository).");

            var skippedResult = CreateGitHubArtifactCleanupSkippedResult(
                repository: string.Empty,
                dryRun: true,
                message: $"Skipped: missing repository (set '{repoEnv}' or provide repo/repository).");
            WriteGitHubArtifactCleanupArtifacts(baseDir, reportPath, summaryPath, skippedResult);
            stepResult.Success = true;
            stepResult.Message = "github-artifacts-prune skipped: missing repository.";
            return;
        }

        var token = GetString(step, "token") ?? GetString(step, "apiToken") ?? GetString(step, "api-token");
        var tokenEnv = GetString(step, "tokenEnv") ?? GetString(step, "token-env") ?? "GITHUB_TOKEN";
        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable(tokenEnv);
        if (string.IsNullOrWhiteSpace(token))
        {
            if (!optionalToken)
                throw new InvalidOperationException($"github-artifacts-prune: missing token (set '{tokenEnv}' or provide token).");

            var skippedResult = CreateGitHubArtifactCleanupSkippedResult(
                repository: repository,
                dryRun: true,
                message: $"Skipped: missing token (set '{tokenEnv}' or provide token).");
            WriteGitHubArtifactCleanupArtifacts(baseDir, reportPath, summaryPath, skippedResult);
            stepResult.Success = true;
            stepResult.Message = "github-artifacts-prune skipped: missing token.";
            return;
        }

        var dryRun = GetBool(step, "dryRun") ?? GetBool(step, "dry-run") ?? true;
        if (GetBool(step, "apply") == true || GetBool(step, "execute") == true)
            dryRun = false;

        var includeNames = ReadGitHubArtifactsStringList(step, "names", "name", "include", "includes").ToArray();
        var excludeNames = ReadGitHubArtifactsStringList(step, "exclude", "excludes", "excludeNames", "exclude-names").ToArray();

        var keepLatestPerName = GetInt(step, "keep") ??
                                GetInt(step, "keepLatestPerName") ??
                                GetInt(step, "keep-latest-per-name") ??
                                5;
        if (keepLatestPerName < 0)
            throw new InvalidOperationException("github-artifacts-prune: keep must be >= 0.");

        var maxAgeDays = GetInt(step, "maxAgeDays") ??
                         GetInt(step, "max-age-days") ??
                         GetInt(step, "ageDays") ??
                         GetInt(step, "age-days") ??
                         7;
        int? effectiveMaxAgeDays = maxAgeDays < 1 ? null : maxAgeDays;

        var maxDelete = GetInt(step, "maxDelete") ?? GetInt(step, "max-delete") ?? GetInt(step, "limit") ?? 200;
        if (maxDelete < 1)
            throw new InvalidOperationException("github-artifacts-prune: maxDelete must be >= 1.");

        var pageSize = GetInt(step, "pageSize") ?? GetInt(step, "page-size") ?? 100;
        if (pageSize < 1 || pageSize > 100)
            throw new InvalidOperationException("github-artifacts-prune: pageSize must be between 1 and 100.");

        var failOnDeleteError = GetBool(step, "failOnDeleteError") ?? GetBool(step, "fail-on-delete-error") ?? false;
        var apiBaseUrl = GetString(step, "apiBaseUrl") ?? GetString(step, "api-base-url");
        var spec = new GitHubArtifactCleanupSpec
        {
            ApiBaseUrl = apiBaseUrl,
            Repository = repository,
            Token = token,
            IncludeNames = includeNames,
            ExcludeNames = excludeNames,
            KeepLatestPerName = keepLatestPerName,
            MaxAgeDays = effectiveMaxAgeDays,
            MaxDelete = maxDelete,
            PageSize = pageSize,
            DryRun = dryRun,
            FailOnDeleteError = failOnDeleteError
        };
        var service = new GitHubArtifactCleanupService(new NullLogger());
        GitHubArtifactCleanupResult result;
        try
        {
            result = service.Prune(spec);
        }
        catch (Exception ex) when (continueOnError)
        {
            result = CreateGitHubArtifactCleanupSkippedResult(repository, dryRun, $"Cleanup failed: {ex.Message}");
            result.Success = false;
            WriteGitHubArtifactCleanupArtifacts(baseDir, reportPath, summaryPath, result);
            stepResult.Success = true;
            stepResult.Message = $"github-artifacts-prune allowed failure: {ex.Message}";
            return;
        }

        WriteGitHubArtifactCleanupArtifacts(baseDir, reportPath, summaryPath, result);

        if (!result.Success && continueOnError)
        {
            stepResult.Success = true;
            stepResult.Message = $"github-artifacts-prune allowed failure: {BuildGitHubArtifactCleanupMessage(result)}";
            return;
        }

        stepResult.Success = result.Success;
        stepResult.Message = BuildGitHubArtifactCleanupMessage(result);
        if (!result.Success)
            throw new InvalidOperationException(stepResult.Message);
    }

    private static IEnumerable<string> ReadGitHubArtifactsStringList(JsonElement step, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(step, name);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var token in value.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    yield return trimmed;
            }
        }
    }

    private static string BuildGitHubArtifactCleanupMessage(GitHubArtifactCleanupResult result)
    {
        var mode = result.DryRun ? "dry-run" : "applied";
        var text = $"github-artifacts-prune: {mode}, scanned {result.ScannedArtifacts}, matched {result.MatchedArtifacts}, planned {result.PlannedDeletes}";
        if (!result.DryRun)
            text += $", deleted {result.DeletedArtifacts}, failed {result.FailedDeletes}";
        if (!string.IsNullOrWhiteSpace(result.Message))
            text += $", note: {result.Message}";
        return text;
    }

    private static string BuildGitHubArtifactCleanupSummary(GitHubArtifactCleanupResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GitHub Artifact Cleanup");
        builder.AppendLine();
        builder.AppendLine($"- Repository: {result.Repository}");
        builder.AppendLine($"- Mode: {(result.DryRun ? "dry-run" : "apply")}");
        builder.AppendLine($"- Scanned artifacts: {result.ScannedArtifacts}");
        builder.AppendLine($"- Matched artifacts: {result.MatchedArtifacts}");
        builder.AppendLine($"- Planned deletes: {result.PlannedDeletes}");
        builder.AppendLine($"- Planned bytes: {result.PlannedDeleteBytes}");
        builder.AppendLine($"- Keep latest per name: {result.KeepLatestPerName}");
        builder.AppendLine($"- Max age days: {(result.MaxAgeDays?.ToString() ?? "disabled")}");
        builder.AppendLine($"- Max delete: {result.MaxDelete}");
        if (!result.DryRun)
        {
            builder.AppendLine($"- Deleted artifacts: {result.DeletedArtifacts}");
            builder.AppendLine($"- Deleted bytes: {result.DeletedBytes}");
            builder.AppendLine($"- Failed deletes: {result.FailedDeletes}");
        }

        builder.AppendLine();
        builder.AppendLine("| Artifact | ID | Size (bytes) | Reason |");
        builder.AppendLine("| --- | ---: | ---: | --- |");
        foreach (var item in result.Planned.Take(100))
            builder.AppendLine($"| {item.Name} | {item.Id} | {item.SizeInBytes} | {item.Reason ?? string.Empty} |");

        if (result.Planned.Length > 100)
            builder.AppendLine($"| ... | ... | ... | {result.Planned.Length - 100} additional planned entries omitted |");

        if (!result.DryRun && result.Failed.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failed Deletes");
            builder.AppendLine();
            foreach (var item in result.Failed.Take(50))
                builder.AppendLine($"- `{item.Name}` (`{item.Id}`): {item.DeleteError}");
        }

        return builder.ToString();
    }

    private static GitHubArtifactCleanupResult CreateGitHubArtifactCleanupSkippedResult(string repository, bool dryRun, string message)
    {
        return new GitHubArtifactCleanupResult
        {
            Repository = repository,
            DryRun = dryRun,
            Success = true,
            Message = message
        };
    }

    private static void WriteGitHubArtifactCleanupArtifacts(string baseDir, string? reportPath, string? summaryPath, GitHubArtifactCleanupResult result)
    {
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var resolvedReportPath = ResolvePathWithinRoot(baseDir, reportPath, reportPath);
            var reportDirectory = Path.GetDirectoryName(resolvedReportPath);
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(resolvedReportPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
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
            File.WriteAllText(resolvedSummaryPath, BuildGitHubArtifactCleanupSummary(result));
        }
    }
}
