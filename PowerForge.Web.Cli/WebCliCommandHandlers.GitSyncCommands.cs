using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleGitSync(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var specPath = TryGetOptionValue(subArgs, "--spec") ?? TryGetOptionValue(subArgs, "--step") ?? TryGetOptionValue(subArgs, "--step-json");
        var repo = TryGetOptionValue(subArgs, "--repo") ?? TryGetOptionValue(subArgs, "--repository") ?? TryGetOptionValue(subArgs, "--url");
        var destination = TryGetOptionValue(subArgs, "--destination") ?? TryGetOptionValue(subArgs, "--dest") ?? TryGetOptionValue(subArgs, "--path");

        JsonElement stepElement;
        string baseDir;

        if (!string.IsNullOrWhiteSpace(specPath))
        {
            var fullSpecPath = ResolveExistingFilePath(specPath);
            baseDir = Path.GetDirectoryName(fullSpecPath) ?? ".";
            using var doc = JsonDocument.Parse(File.ReadAllText(fullSpecPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Fail("git-sync --spec must be a JSON object.", outputJson, logger, "web.git-sync");
            stepElement = doc.RootElement.Clone();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(repo))
                return Fail("Missing required --repo (or use --spec).", outputJson, logger, "web.git-sync");
            if (string.IsNullOrWhiteSpace(destination))
                return Fail("Missing required --destination (or use --spec).", outputJson, logger, "web.git-sync");

            baseDir = Directory.GetCurrentDirectory();

            var step = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["repo"] = repo,
                ["destination"] = destination,
                ["repoBaseUrl"] = TryGetOptionValue(subArgs, "--repo-base-url") ?? TryGetOptionValue(subArgs, "--repoBaseUrl") ?? TryGetOptionValue(subArgs, "--repo-host"),
                ["ref"] = TryGetOptionValue(subArgs, "--ref") ?? TryGetOptionValue(subArgs, "--branch") ?? TryGetOptionValue(subArgs, "--tag") ?? TryGetOptionValue(subArgs, "--commit"),
                ["clean"] = HasOption(subArgs, "--clean"),
                ["fetchTags"] = HasOption(subArgs, "--fetch-tags") || HasOption(subArgs, "--fetchTags"),
                ["depth"] = ParseIntOption(TryGetOptionValue(subArgs, "--depth"), 0),
                ["timeoutSeconds"] = ParseIntOption(TryGetOptionValue(subArgs, "--timeout-seconds") ?? TryGetOptionValue(subArgs, "--timeoutSeconds"), 600),
                ["retry"] = ParseIntOption(TryGetOptionValue(subArgs, "--retry") ?? TryGetOptionValue(subArgs, "--retries"), 0),
                ["retryDelayMs"] = ParseIntOption(TryGetOptionValue(subArgs, "--retry-delay-ms") ?? TryGetOptionValue(subArgs, "--retryDelayMs"), 500),
                ["tokenEnv"] = TryGetOptionValue(subArgs, "--token-env") ?? TryGetOptionValue(subArgs, "--tokenEnv"),
                ["token"] = TryGetOptionValue(subArgs, "--token"),
                ["username"] = TryGetOptionValue(subArgs, "--username"),
                ["authType"] = TryGetOptionValue(subArgs, "--auth-type") ?? TryGetOptionValue(subArgs, "--authType") ?? TryGetOptionValue(subArgs, "--auth"),
                ["sparsePaths"] = TryGetOptionValue(subArgs, "--sparse-paths") ?? TryGetOptionValue(subArgs, "--sparsePaths"),
                ["submodules"] = HasOption(subArgs, "--submodules") || HasOption(subArgs, "--submodule"),
                ["submodulesRecursive"] = HasOption(subArgs, "--submodules-recursive") || HasOption(subArgs, "--submodulesRecursive"),
                ["submoduleDepth"] = ParseIntOption(TryGetOptionValue(subArgs, "--submodule-depth") ?? TryGetOptionValue(subArgs, "--submoduleDepth"), 0),
                ["lockMode"] = TryGetOptionValue(subArgs, "--lock-mode") ?? TryGetOptionValue(subArgs, "--lockMode"),
                ["lockPath"] = TryGetOptionValue(subArgs, "--lock-path") ?? TryGetOptionValue(subArgs, "--lockPath") ?? TryGetOptionValue(subArgs, "--lock"),
                ["writeManifest"] = HasOption(subArgs, "--write-manifest") || HasOption(subArgs, "--writeManifest"),
                ["manifestPath"] = TryGetOptionValue(subArgs, "--manifest-path") ?? TryGetOptionValue(subArgs, "--manifestPath") ?? TryGetOptionValue(subArgs, "--manifest")
            };

            // Do not use WebCliJson.Options for this anonymous/dictionary payload: source-gen options won't have metadata.
            var json = JsonSerializer.Serialize(step);
            using var doc = JsonDocument.Parse(json);
            stepElement = doc.RootElement.Clone();
        }

        WebPipelineStepResult stepResult;
        try
        {
            stepResult = WebPipelineRunner.RunGitSyncStepForCli(stepElement, baseDir, logger);
        }
        catch (Exception ex)
        {
            var errorMessage = RedactGitSyncSecrets(ex.Message, stepElement);
            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.git-sync",
                    Success = false,
                    ExitCode = 1,
                    Error = errorMessage
                });
                return 1;
            }

            logger.Error(errorMessage);
            return 1;
        }

        if (outputJson)
        {
            var pipeline = new WebPipelineResult
            {
                StepCount = 1,
                Success = stepResult.Success,
                Steps = new() { stepResult }
            };
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.git-sync",
                Success = stepResult.Success,
                ExitCode = stepResult.Success ? 0 : 1,
                Result = WebCliJson.SerializeToElement(pipeline, WebCliJson.Context.WebPipelineResult)
            });
            return stepResult.Success ? 0 : 1;
        }

        if (stepResult.Success)
        {
            logger.Success(stepResult.Message ?? "git-sync ok");
            return 0;
        }

        logger.Error(stepResult.Message ?? "git-sync failed");
        return 1;
    }

    private static string RedactGitSyncSecrets(string message, JsonElement step)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        var tokens = new System.Collections.Generic.List<string>();
        TryCollectGitSyncToken(step, tokens);
        if (tokens.Count == 0)
            return message;

        var redacted = message;
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            redacted = redacted.Replace(token, "***", StringComparison.Ordinal);
        }

        return redacted;
    }

    private static void TryCollectGitSyncToken(JsonElement element, System.Collections.Generic.List<string> tokens)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("token", out var tokenElement) && tokenElement.ValueKind == JsonValueKind.String)
        {
            var token = tokenElement.GetString();
            if (!string.IsNullOrWhiteSpace(token))
                tokens.Add(token);
        }

        if (element.TryGetProperty("repos", out var repos) && repos.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in repos.EnumerateArray())
                TryCollectGitSyncToken(entry, tokens);
        }
        else if (element.TryGetProperty("repositories", out var repositories) && repositories.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in repositories.EnumerateArray())
                TryCollectGitSyncToken(entry, tokens);
        }
    }
}
