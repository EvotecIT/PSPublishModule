using System.Text;

namespace PowerForge;

internal sealed class WingetSubmissionService
{
    private const string DefaultTokenEnvName = "WINGET_CREATE_GITHUB_TOKEN";

    private readonly ILogger _logger;
    private readonly IProcessRunner _processRunner;

    public WingetSubmissionService(ILogger? logger = null, IProcessRunner? processRunner = null)
    {
        _logger = logger ?? new NullLogger();
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public PowerForgeWingetSubmissionPlan Plan(
        PowerForgeReleaseWingetOptions winget,
        IReadOnlyList<PowerForgeWingetManifestArtifact> manifests,
        string configDirectory,
        PowerForgeReleaseRequest request)
    {
        if (winget is null)
            throw new ArgumentNullException(nameof(winget));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var submission = winget.Submission ?? new PowerForgeReleaseWingetSubmissionOptions();
        var enabled = request.SubmitWinget ?? (winget.Submit || submission.Enabled == true);
        var mode = request.WingetSubmitMode ?? submission.Mode;
        var toolPath = ChooseString(request.WingetSubmitToolPath, submission.ToolPath, "wingetcreate");
        var workingDirectory = ResolveWorkingDirectory(configDirectory);
        var timeoutSeconds = request.WingetSubmitTimeoutSeconds ?? submission.TimeoutSeconds;
        if (timeoutSeconds <= 0)
            throw new InvalidOperationException("Winget submission timeout must be greater than zero seconds.");

        var token = ResolveToken(submission, request, configDirectory);
        var allowInteractive = request.WingetSubmitAllowInteractiveAuthentication ?? submission.AllowInteractiveAuthentication;
        if (enabled && string.IsNullOrWhiteSpace(token) && !allowInteractive)
        {
            throw new InvalidOperationException(
                $"Winget submission requires a GitHub token. Set Winget.Submission.TokenEnvName (default {DefaultTokenEnvName}), TokenFilePath, Token, or enable AllowInteractiveAuthentication.");
        }

        var entries = enabled
            ? manifests.Select(manifest => BuildEntry(winget, submission, request, manifest, mode, token)).ToArray()
            : Array.Empty<PowerForgeWingetSubmissionEntryPlan>();

        if (enabled && entries.Length == 0)
            throw new InvalidOperationException("Winget submission was enabled, but no generated Winget manifests were available to submit.");

        return new PowerForgeWingetSubmissionPlan
        {
            Enabled = enabled,
            Mode = mode,
            ToolPath = toolPath,
            WorkingDirectory = workingDirectory,
            TimeoutSeconds = timeoutSeconds,
            UsesToken = !string.IsNullOrWhiteSpace(token),
            NoOpen = request.WingetSubmitNoOpen ?? submission.NoOpen,
            Entries = entries
        };
    }

    public PowerForgeWingetSubmissionResult Run(PowerForgeWingetSubmissionPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var results = new List<PowerForgeWingetSubmissionEntryResult>();
        if (!plan.Enabled)
        {
            return new PowerForgeWingetSubmissionResult
            {
                Succeeded = true,
                Entries = Array.Empty<PowerForgeWingetSubmissionEntryResult>()
            };
        }

        foreach (var entry in plan.Entries)
        {
            _logger.Info($"Submitting Winget manifest for {entry.PackageIdentifier} {entry.PackageVersion}.");
            var process = _processRunner.RunAsync(new ProcessRunRequest(
                plan.ToolPath,
                plan.WorkingDirectory,
                entry.Arguments,
                TimeSpan.FromSeconds(plan.TimeoutSeconds))).GetAwaiter().GetResult();

            var entryResult = new PowerForgeWingetSubmissionEntryResult
            {
                PackageIdentifier = entry.PackageIdentifier,
                PackageVersion = entry.PackageVersion,
                ManifestPath = entry.ManifestPath,
                RedactedArguments = entry.RedactedArguments,
                ExitCode = process.ExitCode,
                Succeeded = process.Succeeded,
                TimedOut = process.TimedOut,
                StdOut = process.StdOut,
                StdErr = process.StdErr
            };
            results.Add(entryResult);

            if (!entryResult.Succeeded)
            {
                var error = string.IsNullOrWhiteSpace(process.StdErr)
                    ? $"wingetcreate exited with code {process.ExitCode}."
                    : process.StdErr.Trim();
                return new PowerForgeWingetSubmissionResult
                {
                    Succeeded = false,
                    ErrorMessage = $"Winget submission failed for '{entry.PackageIdentifier}'. {error}",
                    Entries = results.ToArray()
                };
            }
        }

        return new PowerForgeWingetSubmissionResult
        {
            Succeeded = true,
            Entries = results.ToArray()
        };
    }

    private static PowerForgeWingetSubmissionEntryPlan BuildEntry(
        PowerForgeReleaseWingetOptions winget,
        PowerForgeReleaseWingetSubmissionOptions submission,
        PowerForgeReleaseRequest request,
        PowerForgeWingetManifestArtifact manifest,
        PowerForgeWingetSubmissionMode mode,
        string? token)
    {
        if (string.IsNullOrWhiteSpace(manifest.PackageIdentifier))
            throw new InvalidOperationException("Winget submission manifest is missing PackageIdentifier.");
        if (string.IsNullOrWhiteSpace(manifest.PackageVersion))
            throw new InvalidOperationException($"Winget submission manifest '{manifest.PackageIdentifier}' is missing PackageVersion.");
        if (string.IsNullOrWhiteSpace(manifest.ManifestPath) || !File.Exists(manifest.ManifestPath))
            throw new FileNotFoundException($"Winget manifest does not exist on disk: {manifest.ManifestPath}");

        var args = new List<string>();
        var redacted = new List<string>();
        switch (mode)
        {
            case PowerForgeWingetSubmissionMode.Update:
                BuildUpdateArguments(manifest, args, redacted);
                break;
            default:
                args.Add("submit");
                redacted.Add("submit");
                args.Add(manifest.ManifestPath);
                redacted.Add(manifest.ManifestPath);
                break;
        }

        AddCommonSubmissionArguments(submission, request, manifest, args, redacted, token);
        return new PowerForgeWingetSubmissionEntryPlan
        {
            PackageIdentifier = manifest.PackageIdentifier,
            PackageVersion = manifest.PackageVersion,
            ManifestPath = manifest.ManifestPath,
            InstallerUrls = manifest.InstallerUrls.ToArray(),
            Arguments = args.ToArray(),
            RedactedArguments = redacted.ToArray()
        };
    }

    private static void BuildUpdateArguments(
        PowerForgeWingetManifestArtifact manifest,
        List<string> args,
        List<string> redacted)
    {
        if (manifest.InstallerUrls.Length == 0)
            throw new InvalidOperationException($"Winget update submission for '{manifest.PackageIdentifier}' requires at least one installer URL.");

        args.Add("update");
        redacted.Add("update");
        args.Add(manifest.PackageIdentifier);
        redacted.Add(manifest.PackageIdentifier);
        args.Add("--urls");
        redacted.Add("--urls");
        foreach (var url in manifest.InstallerUrls)
        {
            args.Add(url);
            redacted.Add(url);
        }

        args.Add("--version");
        redacted.Add("--version");
        args.Add(manifest.PackageVersion);
        redacted.Add(manifest.PackageVersion);
        args.Add("--submit");
        redacted.Add("--submit");
    }

    private static void AddCommonSubmissionArguments(
        PowerForgeReleaseWingetSubmissionOptions submission,
        PowerForgeReleaseRequest request,
        PowerForgeWingetManifestArtifact manifest,
        List<string> args,
        List<string> redacted,
        string? token)
    {
        var prTitle = ChooseString(request.WingetSubmitPrTitle, submission.PullRequestTitle, string.Empty);
        if (!string.IsNullOrWhiteSpace(prTitle))
        {
            args.Add("--prtitle");
            redacted.Add("--prtitle");
            args.Add(ApplyTemplate(prTitle, manifest));
            redacted.Add(ApplyTemplate(prTitle, manifest));
        }

        var replace = request.WingetSubmitReplace ?? submission.Replace;
        if (replace)
        {
            args.Add("--replace");
            redacted.Add("--replace");
            var replaceVersion = ChooseString(request.WingetSubmitReplaceVersion, submission.ReplaceVersion, string.Empty);
            if (!string.IsNullOrWhiteSpace(replaceVersion))
            {
                args.Add(replaceVersion);
                redacted.Add(replaceVersion);
            }
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            args.Add("--token");
            redacted.Add("--token");
            args.Add(token!);
            redacted.Add("***");
        }

        var noOpen = request.WingetSubmitNoOpen ?? submission.NoOpen;
        if (noOpen)
        {
            args.Add("--no-open");
            redacted.Add("--no-open");
        }
    }

    private static string? ResolveToken(
        PowerForgeReleaseWingetSubmissionOptions submission,
        PowerForgeReleaseRequest request,
        string configDirectory)
    {
        if (!string.IsNullOrWhiteSpace(request.WingetSubmitToken))
            return request.WingetSubmitToken!.Trim();
        if (!string.IsNullOrWhiteSpace(submission.Token))
            return submission.Token!.Trim();

        var tokenFile = ChooseString(request.WingetSubmitTokenFilePath, submission.TokenFilePath, string.Empty);
        if (!string.IsNullOrWhiteSpace(tokenFile))
        {
            var fullPath = Path.IsPathRooted(tokenFile!)
                ? tokenFile!
                : Path.Combine(configDirectory, tokenFile!);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Winget submission token file was not found: {fullPath}");
            return File.ReadAllText(fullPath, Encoding.UTF8).Trim();
        }

        var tokenEnvName = ChooseString(request.WingetSubmitTokenEnvName, submission.TokenEnvName, DefaultTokenEnvName);
        return string.IsNullOrWhiteSpace(tokenEnvName)
            ? null
            : Environment.GetEnvironmentVariable(tokenEnvName!);
    }

    private static string ResolveWorkingDirectory(string configDirectory)
        => string.IsNullOrWhiteSpace(configDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(configDirectory);

    private static string ChooseString(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string ApplyTemplate(string template, PowerForgeWingetManifestArtifact manifest)
        => template
            .Replace("{PackageIdentifier}", manifest.PackageIdentifier)
            .Replace("{PackageVersion}", manifest.PackageVersion);
}
