#pragma warning disable 1591
using System.Text;

namespace PowerForge;

public sealed class WorkspaceValidationService
{
    private readonly IProcessRunner _processRunner;

    public WorkspaceValidationService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public WorkspaceValidationProfileSummary[] ListProfiles(WorkspaceValidationSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        return (spec.Profiles ?? Array.Empty<WorkspaceValidationProfile>())
            .Select(p => new WorkspaceValidationProfileSummary
            {
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                Features = (p.Features ?? Array.Empty<string>()).ToArray()
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public WorkspaceValidationPlan Plan(WorkspaceValidationSpec spec, string? configPath, WorkspaceValidationRequest request)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var projectRoot = ResolveProjectRoot(spec, configPath);
        var profile = ResolveProfile(spec, request.ProfileName);
        var activeFeatures = ResolveFeatures(spec, profile, request);
        var variables = BuildVariables(spec, projectRoot, profile, request);
        var steps = ExpandSteps(spec, projectRoot, profile, activeFeatures, variables);

        return new WorkspaceValidationPlan
        {
            ProjectRoot = projectRoot,
            ProfileName = profile.Name,
            Configuration = request.Configuration ?? "Release",
            ActiveFeatures = activeFeatures.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray(),
            Steps = steps.ToArray()
        };
    }

    public string[] Validate(WorkspaceValidationSpec spec, string? configPath, WorkspaceValidationRequest request)
    {
        var errors = new List<string>();
        WorkspaceValidationPlan? plan = null;

        try
        {
            plan = Plan(spec, configPath, request);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return errors.ToArray();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
                errors.Add("Workspace validation step is missing Id.");
            else if (!seen.Add(step.Id))
                errors.Add($"Workspace validation step id '{step.Id}' is duplicated.");

            if (string.IsNullOrWhiteSpace(step.Executable))
                errors.Add($"Workspace validation step '{step.Id}' resolved to an empty executable.");
        }

        return errors.ToArray();
    }

    public async Task<WorkspaceValidationResult> RunAsync(
        WorkspaceValidationSpec spec,
        string? configPath,
        WorkspaceValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var plan = Plan(spec, configPath, request);
        var results = new List<WorkspaceValidationStepResult>();

        foreach (var step in plan.Steps)
        {
            if (!string.IsNullOrWhiteSpace(step.RequiredPath) && !File.Exists(step.RequiredPath) && !Directory.Exists(step.RequiredPath))
            {
                if (step.ContinueOnMissingRequiredPath)
                {
                    results.Add(new WorkspaceValidationStepResult
                    {
                        Step = step,
                        Skipped = true,
                        Succeeded = true,
                        SkipReason = $"Required path not found: {step.RequiredPath}"
                    });
                    continue;
                }

                return new WorkspaceValidationResult
                {
                    Plan = plan,
                    Succeeded = false,
                    ErrorMessage = BuildFailureMessage(step, 1, null, $"Required path not found: {step.RequiredPath}"),
                    Steps = results.Append(new WorkspaceValidationStepResult
                    {
                        Step = step,
                        ExitCode = 1,
                        Succeeded = false,
                        StdErr = $"Required path not found: {step.RequiredPath}"
                    }).ToArray()
                };
            }

            var process = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    step.Executable,
                    step.WorkingDirectory,
                    step.Arguments,
                    TimeSpan.FromHours(8),
                    step.EnvironmentVariables,
                    request.CaptureOutput,
                    request.CaptureError),
                cancellationToken).ConfigureAwait(false);

            var stepResult = new WorkspaceValidationStepResult
            {
                Step = step,
                ExitCode = process.ExitCode,
                Succeeded = process.Succeeded,
                StdOut = process.StdOut,
                StdErr = process.StdErr
            };
            results.Add(stepResult);

            if (!process.Succeeded)
            {
                return new WorkspaceValidationResult
                {
                    Plan = plan,
                    Succeeded = false,
                    ErrorMessage = BuildFailureMessage(step, process.ExitCode, process.StdOut, process.StdErr),
                    Steps = results.ToArray()
                };
            }
        }

        return new WorkspaceValidationResult
        {
            Plan = plan,
            Succeeded = true,
            Steps = results.ToArray()
        };
    }

    private static string BuildFailureMessage(WorkspaceValidationPreparedStep step, int exitCode, string? stdOut, string? stdErr)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(step.FailureContext))
            lines.Add(step.FailureContext!);

        lines.Add($"Step '{step.Name}' failed with exit code {exitCode}.");
        lines.Add($"Command: {step.DisplayCommand}");

        var detail = BuildFailureDetail(stdErr, stdOut);
        if (!string.IsNullOrWhiteSpace(detail))
            lines.Add($"Detail: {detail}");

        if (!string.IsNullOrWhiteSpace(step.FailureHint))
            lines.Add($"Hint: {step.FailureHint}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string? BuildFailureDetail(string? stdErr, string? stdOut)
    {
        var stderrLines = ExtractFailureLines(stdErr);
        var stdoutLines = ExtractFailureLines(stdOut);
        var detailLines = new List<string>();

        if (stderrLines.Count > 0)
            detailLines.AddRange(stderrLines);
        else if (stdoutLines.Count > 0)
            detailLines.AddRange(stdoutLines);

        if (detailLines.Count == 0)
            return null;

        return string.Join(Environment.NewLine, detailLines.Take(6));
    }

    private static List<string> ExtractFailureLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var lines = text!
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !ShouldSkipFailureLine(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return lines;
    }

    private static bool ShouldSkipFailureLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        return line.StartsWith("At ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("+ CategoryInfo", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("CategoryInfo", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("+ FullyQualifiedErrorId", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("FullyQualifiedErrorId", StringComparison.OrdinalIgnoreCase);
    }

    private static WorkspaceValidationProfile ResolveProfile(WorkspaceValidationSpec spec, string? requestedProfile)
    {
        var profiles = spec.Profiles ?? Array.Empty<WorkspaceValidationProfile>();
        if (profiles.Length == 0)
        {
            return new WorkspaceValidationProfile
            {
                Name = string.IsNullOrWhiteSpace(requestedProfile) ? "default" : requestedProfile!.Trim()
            };
        }

        var name = string.IsNullOrWhiteSpace(requestedProfile) ? profiles[0].Name : requestedProfile!.Trim();
        var match = profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new InvalidOperationException($"Workspace validation profile '{name}' was not found.");

        return match;
    }

    private static string ResolveProjectRoot(WorkspaceValidationSpec spec, string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(spec.ProjectRoot))
        {
            var root = spec.ProjectRoot!;
            if (!Path.IsPathRooted(root) && !string.IsNullOrWhiteSpace(configPath))
            {
                var baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
                if (!string.IsNullOrWhiteSpace(baseDir))
                    return Path.GetFullPath(Path.Combine(baseDir!, root));
            }

            return Path.GetFullPath(root);
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
            if (!string.IsNullOrWhiteSpace(baseDir))
                return baseDir!;
        }

        return Directory.GetCurrentDirectory();
    }

    private static HashSet<string> ResolveFeatures(WorkspaceValidationSpec spec, WorkspaceValidationProfile profile, WorkspaceValidationRequest request)
    {
        var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in spec.DefaultFeatures ?? Array.Empty<string>())
            AddFeature(features, feature);
        foreach (var feature in profile.Features ?? Array.Empty<string>())
            AddFeature(features, feature);
        foreach (var feature in request.EnabledFeatures ?? Array.Empty<string>())
            AddFeature(features, feature);
        foreach (var feature in request.DisabledFeatures ?? Array.Empty<string>())
            RemoveFeature(features, feature);

        return features;
    }

    private static void AddFeature(HashSet<string> features, string? feature)
    {
        if (!string.IsNullOrWhiteSpace(feature))
        {
            var value = feature!;
            features.Add(value.Trim());
        }
    }

    private static void RemoveFeature(HashSet<string> features, string? feature)
    {
        if (!string.IsNullOrWhiteSpace(feature))
        {
            var value = feature!;
            features.Remove(value.Trim());
        }
    }

    private static Dictionary<string, string?> BuildVariables(WorkspaceValidationSpec spec, string projectRoot, WorkspaceValidationProfile profile, WorkspaceValidationRequest request)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["projectRoot"] = projectRoot,
            ["repoRoot"] = projectRoot,
            ["profile"] = profile.Name,
            ["configuration"] = string.IsNullOrWhiteSpace(request.Configuration) ? "Release" : request.Configuration,
            ["testimoXRoot"] = string.IsNullOrWhiteSpace(request.TestimoXRoot) ? null : Path.GetFullPath(request.TestimoXRoot),
        };

        values["testimoXRootDir"] = EnsureTrailingSeparator(values["testimoXRoot"]);

        foreach (var entry in spec.Variables)
            values[entry.Key] = ExpandTokensOrdinal(entry.Value, values);
        foreach (var entry in profile.Variables)
            values[entry.Key] = ExpandTokensOrdinal(entry.Value, values);
        foreach (var entry in request.Variables)
            values[entry.Key] = ExpandTokensOrdinal(entry.Value, values);

        return values;
    }

    private static string? EnsureTrailingSeparator(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return path!.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
               path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static WorkspaceValidationPreparedStep[] ExpandSteps(
        WorkspaceValidationSpec spec,
        string projectRoot,
        WorkspaceValidationProfile profile,
        HashSet<string> activeFeatures,
        Dictionary<string, string?> variables)
    {
        var prepared = new List<WorkspaceValidationPreparedStep>();
        foreach (var step in spec.Steps ?? Array.Empty<WorkspaceValidationStep>())
        {
            if (!IsStepIncluded(step, profile, activeFeatures))
                continue;

            var items = step.Items is { Length: > 0 } ? step.Items : new[] { string.Empty };
            var frameworks = step.Frameworks is { Length: > 0 } ? step.Frameworks : new[] { string.Empty };

            foreach (var item in items)
            foreach (var framework in frameworks)
            {
                var local = new Dictionary<string, string?>(variables, StringComparer.OrdinalIgnoreCase)
                {
                    ["item"] = item,
                    ["framework"] = framework
                };

                var args = (step.Arguments ?? Array.Empty<string>())
                    .Select(arg => ExpandTokensOrdinal(arg, local))
                    .Where(arg => !string.IsNullOrWhiteSpace(arg))
                    .Cast<string>()
                    .ToArray();

                var executable = step.Kind == WorkspaceValidationStepKind.DotNet
                    ? "dotnet"
                    : ExpandTokensOrdinal(step.Executable, local);

                if (string.IsNullOrWhiteSpace(executable))
                    throw new InvalidOperationException($"Workspace validation step '{step.Id}' is missing Executable.");

                var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(local["testimoXRootDir"]))
                {
                    environment["TESTIMOX_ROOT"] = local["testimoXRootDir"];
                    environment["TestimoXRoot"] = local["testimoXRootDir"];
                }
                foreach (var entry in step.EnvironmentVariables ?? new Dictionary<string, string?>())
                    environment[entry.Key] = ExpandTokensOrdinal(entry.Value, local);

                var displayName = ExpandTokensOrdinal(step.Name, local)
                    ?? step.Id;

                prepared.Add(new WorkspaceValidationPreparedStep
                {
                    Id = BuildExpandedId(step.Id, item, framework),
                    Name = string.IsNullOrWhiteSpace(displayName) ? step.Id : displayName!,
                    Kind = step.Kind,
                    Executable = executable!,
                    Arguments = args,
                    WorkingDirectory = ResolveWorkingDirectory(projectRoot, ExpandTokensOrdinal(step.WorkingDirectory, local)),
                    DisplayCommand = BuildDisplayCommand(executable!, args),
                    FailureContext = ExpandTokensOrdinal(step.FailureContext, local),
                    FailureHint = ExpandTokensOrdinal(step.FailureHint, local),
                    RequiredPath = ResolveOptionalPath(projectRoot, ExpandTokensOrdinal(step.RequiredPath, local)),
                    ContinueOnMissingRequiredPath = step.ContinueOnMissingRequiredPath,
                    EnvironmentVariables = environment
                });
            }
        }

        return prepared.ToArray();
    }

    private static bool IsStepIncluded(WorkspaceValidationStep step, WorkspaceValidationProfile profile, HashSet<string> activeFeatures)
    {
        if (step.Profiles is { Length: > 0 } &&
            !step.Profiles.Any(p => string.Equals(p, profile.Name, StringComparison.OrdinalIgnoreCase)))
            return false;

        foreach (var feature in step.RequiredFeatures ?? Array.Empty<string>())
        {
            if (!activeFeatures.Contains(feature))
                return false;
        }

        return true;
    }

    private static string BuildExpandedId(string id, string item, string framework)
    {
        var parts = new List<string> { id };
        if (!string.IsNullOrWhiteSpace(item))
            parts.Add(SanitizeIdPart(item));
        if (!string.IsNullOrWhiteSpace(framework))
            parts.Add(SanitizeIdPart(framework));
        return string.Join(":", parts);
    }

    private static string SanitizeIdPart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static string ResolveWorkingDirectory(string projectRoot, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return projectRoot;

        return Path.GetFullPath(Path.IsPathRooted(workingDirectory)
            ? workingDirectory
            : Path.Combine(projectRoot, workingDirectory));
    }

    private static string? ResolveOptionalPath(string projectRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(projectRoot, path));
    }

    private static string? ExpandTokensOrdinal(string? input, IReadOnlyDictionary<string, string?> variables)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var output = input!;
        foreach (var entry in variables)
        {
            output = ReplaceOrdinalIgnoreCase(output, "{" + entry.Key + "}", entry.Value ?? string.Empty);
        }

        return output;
    }

    private static string ReplaceOrdinalIgnoreCase(string input, string search, string replacement)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(search))
            return input;

        var index = input.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return input;

        var builder = new StringBuilder(input.Length);
        var last = 0;
        while (index >= 0)
        {
            builder.Append(input, last, index - last);
            builder.Append(replacement);
            last = index + search.Length;
            index = input.IndexOf(search, last, StringComparison.OrdinalIgnoreCase);
        }

        builder.Append(input, last, input.Length - last);
        return builder.ToString();
    }

    private static string BuildDisplayCommand(string executable, IReadOnlyList<string> arguments)
    {
        var parts = new List<string> { executable };
        foreach (var arg in arguments)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            parts.Add(arg.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
                ? $"\"{arg.Replace("\"", "\\\"")}\""
                : arg);
        }

        return string.Join(" ", parts);
    }
}
#pragma warning restore 1591
