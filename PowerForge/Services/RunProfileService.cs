using System.Text;

namespace PowerForge;

/// <summary>
/// Resolves, prepares, and executes reusable run profiles.
/// </summary>
public sealed class RunProfileService
{
    private readonly IProcessRunner _processRunner;
    private readonly IPowerShellRunner _powerShellRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunProfileService"/> class.
    /// </summary>
    /// <param name="processRunner">Optional process runner override.</param>
    /// <param name="powerShellRunner">Optional PowerShell runner override.</param>
    public RunProfileService(
        IProcessRunner? processRunner = null,
        IPowerShellRunner? powerShellRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
        _powerShellRunner = powerShellRunner ?? new PowerShellRunner(_processRunner);
    }

    /// <summary>
    /// Returns the available run-profile summaries from the provided specification.
    /// </summary>
    /// <param name="spec">Run-profile specification.</param>
    /// <returns>Sorted run-profile summaries.</returns>
    public RunProfileSummary[] ListProfiles(RunProfileSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        return (spec.Profiles ?? Array.Empty<RunProfile>())
            .Select(p => new RunProfileSummary
            {
                Name = p.Name ?? string.Empty,
                Kind = p.Kind,
                Description = p.Description,
                Example = p.Example,
                Framework = p.Framework
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Resolves a run target into a fully prepared command without executing it.
    /// </summary>
    /// <param name="spec">Run-profile specification.</param>
    /// <param name="configPath">Optional source config path used for relative path resolution.</param>
    /// <param name="request">Execution request and runtime overrides.</param>
    /// <returns>Prepared command metadata.</returns>
    public RunProfilePreparedCommand Prepare(RunProfileSpec spec, string? configPath, RunProfileExecutionRequest request)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.TargetName))
            throw new ArgumentException("Run target name is required.", nameof(request));

        var projectRoot = ResolveProjectRoot(spec, configPath);
        var profile = (spec.Profiles ?? Array.Empty<RunProfile>())
            .FirstOrDefault(p => string.Equals(p.Name, request.TargetName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            throw new InvalidOperationException($"Run target '{request.TargetName}' was not found.");

        return profile.Kind switch
        {
            RunProfileKind.Project => PrepareProject(projectRoot, profile, request),
            RunProfileKind.Script => PrepareScript(projectRoot, profile, request),
            RunProfileKind.Command => PrepareCommand(projectRoot, profile, request),
            _ => throw new InvalidOperationException($"Unsupported run profile kind: {profile.Kind}")
        };
    }

    /// <summary>
    /// Executes the requested run target and returns the observed process result.
    /// </summary>
    /// <param name="spec">Run-profile specification.</param>
    /// <param name="configPath">Optional source config path used for relative path resolution.</param>
    /// <param name="request">Execution request and runtime overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result including exit code and captured output.</returns>
    public async Task<RunProfileExecutionResult> RunAsync(
        RunProfileSpec spec,
        string? configPath,
        RunProfileExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var projectRoot = ResolveProjectRoot(spec, configPath);
        var profile = (spec.Profiles ?? Array.Empty<RunProfile>())
            .FirstOrDefault(p => string.Equals(p.Name, request.TargetName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            throw new InvalidOperationException($"Run target '{request.TargetName}' was not found.");

        var prepared = Prepare(spec, configPath, request);

        if (profile.Kind == RunProfileKind.Script)
        {
            var scriptPath = ResolvePath(projectRoot, profile.Path, "script path");
            var scriptArguments = ExtractScriptArguments(prepared.Arguments);
            var result = _powerShellRunner.Run(new PowerShellRunRequest(
                scriptPath,
                scriptArguments,
                TimeSpan.FromHours(8),
                preferPwsh: profile.PreferPwsh,
                workingDirectory: prepared.WorkingDirectory,
                environmentVariables: ResolveEnvironmentVariables(projectRoot, profile, request),
                executableOverride: null,
                captureOutput: prepared.CaptureOutput,
                captureError: prepared.CaptureError));

            return new RunProfileExecutionResult
            {
                PreparedCommand = prepared,
                ExitCode = result.ExitCode,
                StdOut = result.StdOut,
                StdErr = result.StdErr,
                Executable = string.IsNullOrWhiteSpace(result.Executable) ? prepared.Executable : result.Executable,
                TimedOut = false
            };
        }

        var processResult = await _processRunner.RunAsync(
            new ProcessRunRequest(
                prepared.Executable,
                prepared.WorkingDirectory,
                prepared.Arguments,
                TimeSpan.FromHours(8),
                ResolveEnvironmentVariables(projectRoot, profile, request),
                prepared.CaptureOutput,
                prepared.CaptureError),
            cancellationToken).ConfigureAwait(false);

        return new RunProfileExecutionResult
        {
            PreparedCommand = prepared,
            ExitCode = processResult.ExitCode,
            StdOut = processResult.StdOut,
            StdErr = processResult.StdErr,
            Executable = processResult.Executable,
            TimedOut = processResult.TimedOut
        };
    }

    private static RunProfilePreparedCommand PrepareProject(string projectRoot, RunProfile profile, RunProfileExecutionRequest request)
    {
        var projectPath = ResolvePath(projectRoot, profile.ProjectPath, "project path");
        var workingDirectory = ResolveWorkingDirectory(projectRoot, profile.WorkingDirectory);
        var effectiveFramework = ResolveFramework(profile, request);

        var arguments = new List<string>
        {
            "run",
            "--project",
            projectPath,
            "-c",
            request.Configuration
        };

        if (!string.IsNullOrWhiteSpace(effectiveFramework))
        {
            var framework = effectiveFramework!;
            arguments.Add("--framework");
            arguments.Add(framework);
        }

        if (profile.NoLaunchProfile)
            arguments.Add("--no-launch-profile");

        if (request.NoBuild)
            arguments.Add("--no-build");
        if (request.NoRestore)
            arguments.Add("--no-restore");

        foreach (var property in profile.MsBuildProperties ?? new Dictionary<string, string?>())
        {
            var expandedValue = ApplyTokens(projectRoot, profile, request, property.Value);
            arguments.Add(string.IsNullOrWhiteSpace(expandedValue)
                ? $"/p:{property.Key}"
                : $"/p:{property.Key}={expandedValue}");
        }

        if (request.IncludePrivateToolPacks && profile.PassIncludePrivateToolPacks)
        {
            arguments.Add("/p:IncludePrivateToolPacks=true");
            if (!string.IsNullOrWhiteSpace(request.TestimoXRoot))
            {
                arguments.Add($"/p:TestimoXRoot={EnsureTrailingSeparator(Path.GetFullPath(request.TestimoXRoot))}");
            }
        }

        var runtimeArgs = new List<string>();
        runtimeArgs.AddRange(ExpandArguments(projectRoot, profile, request, profile.Arguments));
        AppendCommonForwardedArguments(runtimeArgs, profile, request);
        AppendDirectExtraArgs(runtimeArgs, profile, request);
        if (runtimeArgs.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(runtimeArgs);
        }

        return new RunProfilePreparedCommand
        {
            TargetName = profile.Name,
            Kind = profile.Kind,
            Description = profile.Description,
            WorkingDirectory = workingDirectory,
            Executable = "dotnet",
            Arguments = arguments.ToArray(),
            DisplayCommand = BuildDisplayCommand("dotnet", arguments),
            CaptureOutput = request.CaptureOutput,
            CaptureError = request.CaptureError
        };
    }

    private static RunProfilePreparedCommand PrepareScript(string projectRoot, RunProfile profile, RunProfileExecutionRequest request)
    {
        var scriptPath = ResolvePath(projectRoot, profile.Path, "script path");
        var workingDirectory = ResolveWorkingDirectory(projectRoot, profile.WorkingDirectory);
        var scriptArguments = new List<string>();
        var effectiveFramework = ResolveFramework(profile, request);

        if (profile.PassConfiguration)
        {
            scriptArguments.Add("-Configuration");
            scriptArguments.Add(request.Configuration ?? string.Empty);
        }

        if (profile.PassFramework && !string.IsNullOrWhiteSpace(effectiveFramework))
        {
            var framework = effectiveFramework!;
            scriptArguments.Add("-Framework");
            scriptArguments.Add(framework);
        }

        if (profile.PassNoBuild && request.NoBuild)
            scriptArguments.Add("-NoBuild");
        if (profile.PassNoRestore && request.NoRestore)
            scriptArguments.Add("-NoRestore");

        scriptArguments.AddRange(ExpandArguments(projectRoot, profile, request, profile.Arguments));
        AppendCommonForwardedArguments(scriptArguments, profile, request);
        AppendDirectExtraArgs(scriptArguments, profile, request);

        var processArguments = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        };
        processArguments.AddRange(scriptArguments);

        var executable = profile.PreferPwsh
            ? (IsWindows() ? "pwsh.exe" : "pwsh")
            : (IsWindows() ? "powershell.exe" : "pwsh");

        return new RunProfilePreparedCommand
        {
            TargetName = profile.Name,
            Kind = profile.Kind,
            Description = profile.Description,
            WorkingDirectory = workingDirectory,
            Executable = executable,
            Arguments = processArguments.ToArray(),
            DisplayCommand = BuildDisplayCommand(executable, processArguments),
            CaptureOutput = request.CaptureOutput,
            CaptureError = request.CaptureError
        };
    }

    private static RunProfilePreparedCommand PrepareCommand(string projectRoot, RunProfile profile, RunProfileExecutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(profile.Executable))
            throw new InvalidOperationException($"Run target '{profile.Name}' is missing Executable.");

        var workingDirectory = ResolveWorkingDirectory(projectRoot, profile.WorkingDirectory);
        var executable = ApplyTokens(projectRoot, profile, request, profile.Executable);
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException($"Run target '{profile.Name}' resolved to an empty executable.");
        var resolvedExecutable = executable!;

        var arguments = new List<string>(ExpandArguments(projectRoot, profile, request, profile.Arguments));
        AppendCommonForwardedArguments(arguments, profile, request);
        AppendDirectExtraArgs(arguments, profile, request);

        return new RunProfilePreparedCommand
        {
            TargetName = profile.Name,
            Kind = profile.Kind,
            Description = profile.Description,
            WorkingDirectory = workingDirectory,
            Executable = resolvedExecutable,
            Arguments = arguments.ToArray(),
            DisplayCommand = BuildDisplayCommand(resolvedExecutable, arguments),
            CaptureOutput = request.CaptureOutput,
            CaptureError = request.CaptureError
        };
    }

    private static void AppendCommonForwardedArguments(List<string> arguments, RunProfile profile, RunProfileExecutionRequest request)
    {
        if (profile.PassAllowRoot)
        {
            foreach (var root in request.AllowRoot ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(root))
                    arguments.AddRange(new[] { "-AllowRoot", root });
            }
        }

        if (profile.PassIncludePrivateToolPacks && request.IncludePrivateToolPacks)
            arguments.Add("-IncludePrivateToolPacks");

        if (profile.PassTestimoXRoot && !string.IsNullOrWhiteSpace(request.TestimoXRoot))
        {
            var testimoXRoot = request.TestimoXRoot!;
            arguments.Add("-TestimoXRoot");
            arguments.Add(testimoXRoot);
        }

        if (profile.PassExtraArgs && (request.ExtraArgs?.Length ?? 0) > 0)
        {
            var extraArgs = request.ExtraArgs ?? Array.Empty<string>();
            arguments.Add("-ExtraArgs");
            arguments.AddRange(extraArgs.Where(arg => !string.IsNullOrWhiteSpace(arg)));
        }
    }

    private static void AppendDirectExtraArgs(List<string> arguments, RunProfile profile, RunProfileExecutionRequest request)
    {
        if (!profile.PassExtraArgsDirect || (request.ExtraArgs?.Length ?? 0) == 0)
            return;

        foreach (var extraArg in request.ExtraArgs ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(extraArg))
                arguments.Add(extraArg);
        }
    }

    private static string[] ExpandArguments(string projectRoot, RunProfile profile, RunProfileExecutionRequest request, IEnumerable<string>? arguments)
    {
        return (arguments ?? Array.Empty<string>())
            .Select(arg => ApplyTokens(projectRoot, profile, request, arg) ?? string.Empty)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string?>? ResolveEnvironmentVariables(string projectRoot, RunProfile profile, RunProfileExecutionRequest request)
    {
        if (profile.EnvironmentVariables is null || profile.EnvironmentVariables.Count == 0)
            return null;

        return profile.EnvironmentVariables.ToDictionary(
            kvp => kvp.Key,
            kvp => ApplyTokens(projectRoot, profile, request, kvp.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveProjectRoot(RunProfileSpec spec, string? configPath)
    {
        var configDirectory = string.IsNullOrWhiteSpace(configPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

        if (!string.IsNullOrWhiteSpace(spec.ProjectRoot))
            return ResolvePath(configDirectory, spec.ProjectRoot, "project root");

        return configDirectory;
    }

    private static string ResolveWorkingDirectory(string projectRoot, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return projectRoot;

        return ResolvePath(projectRoot, workingDirectory, "working directory");
    }

    private static string ResolvePath(string basePath, string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Run profile is missing {label}.");

        var fullPath = Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(basePath, path));

        return fullPath;
    }

    private static string? ResolveFramework(RunProfile profile, RunProfileExecutionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Framework))
            return request.Framework!.Trim();

        if (string.Equals(profile.Framework, "project-defined", StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(profile.Framework) ? null : profile.Framework!.Trim();
    }

    private static string? ApplyTokens(string projectRoot, RunProfile profile, RunProfileExecutionRequest request, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var effectiveFramework = ResolveFramework(profile, request) ?? string.Empty;
        var expanded = value ?? string.Empty;
        expanded = ReplaceOrdinalIgnoreCase(expanded, "{projectRoot}", projectRoot);
        expanded = ReplaceOrdinalIgnoreCase(expanded, "{repoRoot}", projectRoot);
        expanded = ReplaceOrdinalIgnoreCase(expanded, "{configuration}", request.Configuration ?? string.Empty);
        expanded = ReplaceOrdinalIgnoreCase(expanded, "{framework}", effectiveFramework);
        expanded = ReplaceOrdinalIgnoreCase(expanded, "{target}", profile.Name ?? string.Empty);
        expanded = ReplaceOrdinalIgnoreCase(expanded, "{testimoxRoot}", request.TestimoXRoot ?? string.Empty);
        return expanded;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string[] ExtractScriptArguments(string[] processArguments)
    {
        var index = Array.FindIndex(processArguments, arg => string.Equals(arg, "-File", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= processArguments.Length)
            return Array.Empty<string>();

        return processArguments.Skip(index + 2).ToArray();
    }

    private static string BuildDisplayCommand(string executable, IEnumerable<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(executable);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (value.IndexOfAny(new[] { ' ', '\t', '"', '\'' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static bool IsWindows()
    {
        return Path.DirectorySeparatorChar == '\\';
    }

    private static string ReplaceOrdinalIgnoreCase(string input, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
            return input;

        var startIndex = 0;
        var matchIndex = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        if (matchIndex < 0)
            return input;

        var builder = new StringBuilder(input.Length + Math.Max(0, newValue.Length - oldValue.Length) * 2);
        while (matchIndex >= 0)
        {
            builder.Append(input, startIndex, matchIndex - startIndex);
            builder.Append(newValue);
            startIndex = matchIndex + oldValue.Length;
            matchIndex = input.IndexOf(oldValue, startIndex, StringComparison.OrdinalIgnoreCase);
        }

        builder.Append(input, startIndex, input.Length - startIndex);
        return builder.ToString();
    }
}
