using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleEngineLock(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var mode = ResolveEngineLockMode(subArgs);
        if (!mode.Equals("show", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("verify", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("update", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("Invalid --mode. Supported values: show, verify, update.", outputJson, logger, "web.engine-lock");
        }

        var configPath = TryGetOptionValue(subArgs, "--config");
        var baseDir = string.IsNullOrWhiteSpace(configPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(ResolveExistingFilePath(configPath)) ?? Directory.GetCurrentDirectory();

        var lockPathValue = TryGetOptionValue(subArgs, "--path") ??
                            TryGetOptionValue(subArgs, "--lock-path") ??
                            TryGetOptionValue(subArgs, "--lockPath") ??
                            ".powerforge/engine-lock.json";
        var lockPath = ResolvePathRelative(baseDir, lockPathValue);

        var expectedRepository = TryGetOptionValue(subArgs, "--repository") ?? TryGetOptionValue(subArgs, "--repo");
        var expectedRef = TryGetOptionValue(subArgs, "--ref");
        var expectedChannel = TryGetOptionValue(subArgs, "--channel");
        var requireImmutableRef = HasOption(subArgs, "--require-immutable-ref") ||
                                  HasOption(subArgs, "--requireImmutableRef") ||
                                  HasOption(subArgs, "--require-sha") ||
                                  HasOption(subArgs, "--requireSha");
        var useEnv = HasOption(subArgs, "--use-env") || HasOption(subArgs, "--env");
        if (useEnv)
        {
            expectedRepository ??= Environment.GetEnvironmentVariable("POWERFORGE_REPOSITORY");
            expectedRef ??= Environment.GetEnvironmentVariable("POWERFORGE_REF");
        }

        var existed = File.Exists(lockPath);
        WebEngineLockSpec? lockSpec = null;
        if (existed)
        {
            try
            {
                lockSpec = WebEngineLockFile.Read(lockPath, WebCliJson.Options);
            }
            catch (Exception ex)
            {
                return CompleteEngineLock(
                    outputJson,
                    logger,
                    outputSchemaVersion,
                    success: false,
                    error: ex.Message,
                    result: new WebEngineLockResult
                    {
                        Path = lockPath,
                        Mode = mode,
                        Exists = true
                    });
            }
        }

        string? error = null;
        var driftReasons = new List<string>();

        switch (mode.ToLowerInvariant())
        {
            case "show":
                if (lockSpec is null)
                    error = $"Engine lock file not found: {lockPath}";
                break;

            case "verify":
                if (lockSpec is null)
                {
                    error = $"Engine lock file not found: {lockPath}";
                }
                else
                {
                    var validation = WebEngineLockFile.Validate(lockSpec);
                    if (validation.Length > 0)
                    {
                        error = string.Join(" ", validation);
                    }
                    else if (requireImmutableRef && !WebEngineLockFile.IsCommitSha(lockSpec.Ref))
                    {
                        error = $"engine-lock verify failed: lock ref '{lockSpec.Ref}' is not an immutable commit SHA (40/64 hex).";
                    }
                    else
                    {
                        AddEngineLockDriftIfAny(driftReasons, "repository", lockSpec.Repository, expectedRepository);
                        AddEngineLockDriftIfAny(driftReasons, "ref", lockSpec.Ref, expectedRef);
                        AddEngineLockDriftIfAny(driftReasons, "channel", lockSpec.Channel, expectedChannel);
                    }
                }
                break;

            case "update":
                var candidate = lockSpec is null
                    ? WebEngineLockFile.CreateDefault()
                    : WebEngineLockFile.Normalize(lockSpec, stampUpdatedUtc: false);

                if (!string.IsNullOrWhiteSpace(expectedRepository))
                    candidate.Repository = expectedRepository.Trim();
                if (!string.IsNullOrWhiteSpace(expectedRef))
                    candidate.Ref = expectedRef.Trim();
                if (!string.IsNullOrWhiteSpace(expectedChannel))
                    candidate.Channel = expectedChannel.Trim();

                candidate = WebEngineLockFile.Normalize(candidate, stampUpdatedUtc: true);
                var updateValidation = WebEngineLockFile.Validate(candidate);
                if (updateValidation.Length > 0)
                {
                    error = string.Join(" ", updateValidation);
                }
                else if (requireImmutableRef && !WebEngineLockFile.IsCommitSha(candidate.Ref))
                {
                    error = $"engine-lock update failed: ref '{candidate.Ref}' is not an immutable commit SHA (40/64 hex).";
                }
                else
                {
                    WebEngineLockFile.Write(lockPath, candidate, WebCliJson.Options);
                    lockSpec = WebEngineLockFile.Read(lockPath, WebCliJson.Options);
                    existed = true;
                }
                break;
        }

        var resolved = lockSpec ?? new WebEngineLockSpec();
        var result = new WebEngineLockResult
        {
            Path = lockPath,
            Mode = mode,
            Exists = existed,
            Repository = resolved.Repository,
            Ref = resolved.Ref,
            ImmutableRef = WebEngineLockFile.IsCommitSha(resolved.Ref),
            Channel = resolved.Channel,
            UpdatedUtc = resolved.UpdatedUtc,
            DriftDetected = driftReasons.Count > 0,
            DriftReasons = driftReasons.ToArray()
        };

        if (driftReasons.Count > 0 && string.IsNullOrWhiteSpace(error))
            error = "Engine lock drift detected.";

        var success = string.IsNullOrWhiteSpace(error);
        return CompleteEngineLock(outputJson, logger, outputSchemaVersion, success, error, result);
    }

    private static string ResolveEngineLockMode(string[] args)
    {
        var explicitMode = TryGetOptionValue(args, "--mode");
        if (!string.IsNullOrWhiteSpace(explicitMode))
            return explicitMode.Trim();
        if (HasOption(args, "--verify"))
            return "verify";
        if (HasOption(args, "--update"))
            return "update";
        return "show";
    }

    private static void AddEngineLockDriftIfAny(List<string> driftReasons, string name, string actualValue, string? expectedValue)
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
            return;

        var expected = expectedValue.Trim();
        if (actualValue.Equals(expected, StringComparison.Ordinal))
            return;

        driftReasons.Add($"expected {name} '{expected}' but lock has '{actualValue}'.");
    }

    private static int CompleteEngineLock(bool outputJson, WebConsoleLogger logger, int outputSchemaVersion, bool success, string? error, WebEngineLockResult result)
    {
        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.engine-lock",
                Success = success,
                ExitCode = success ? 0 : 1,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebEngineLockResult),
                Error = error
            });
            return success ? 0 : 1;
        }

        if (success)
            logger.Success($"Engine lock ({result.Mode}): {result.Repository}@{result.Ref}");
        else
            logger.Error(error ?? "Engine lock command failed.");

        logger.Info($"Path: {result.Path}");
        if (!string.IsNullOrWhiteSpace(result.Channel))
            logger.Info($"Channel: {result.Channel}");
        logger.Info($"Immutable ref: {(result.ImmutableRef ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(result.UpdatedUtc))
            logger.Info($"Updated UTC: {result.UpdatedUtc}");

        if (result.DriftDetected)
        {
            foreach (var reason in result.DriftReasons)
                logger.Warn(reason);
        }

        return success ? 0 : 1;
    }
}
