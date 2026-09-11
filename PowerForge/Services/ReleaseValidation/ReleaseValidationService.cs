using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Reusable final-artifact validation and isolated product smoke-test execution.</summary>
public sealed partial class ReleaseValidationService
{
    private readonly IProcessRunner _processRunner;
    /// <summary>Creates a validator using the shared process runner.</summary>
    public ReleaseValidationService(IProcessRunner? processRunner = null)
        => _processRunner = new ReleaseValidationProcessRunner(processRunner);

    /// <summary>Serializes the same typed contract produced by the PowerShell DSL.</summary>
    public static string Serialize(ReleaseValidationSpec spec)
        => JsonSerializer.Serialize(spec, ReleaseValidationJsonContext.Default.ReleaseValidationSpec);

    /// <summary>Serializes validation evidence without reflection-based metadata.</summary>
    public static string SerializeReport(ReleaseValidationReport report)
        => JsonSerializer.Serialize(report, ReleaseValidationJsonContext.Default.ReleaseValidationReport);

    /// <summary>Checks final artifacts and executes configured probes without publication.</summary>
    public async Task<ReleaseValidationReport> RunAsync(ReleaseValidationSpec spec, string? configPath = null,
        ReleaseValidationRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        cancellationToken.ThrowIfCancellationRequested();
        request ??= new ReleaseValidationRequest();
        var report = new ReleaseValidationReport { Version = request.Version ?? string.Empty };
        try
        {
            if (spec.SchemaVersion != 1) throw new InvalidOperationException("Unsupported release validation schema version.");
            if (!HasContracts(spec))
                throw new InvalidOperationException("Release validation requires at least one artifact or command contract.");
            var basePath = configPath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            var root = Path.GetFullPath(request.ProjectRoot ?? Path.Combine(basePath, spec.ProjectRoot));
            ValidateVariableNames(request.Variables.Keys);
            var variables = new Dictionary<string, string>(request.Variables, StringComparer.OrdinalIgnoreCase)
            { ["ProjectRoot"] = root, ["Version"] = report.Version };
            var packages = spec.Packages is null ? new Dictionary<string, PackageInspection>(StringComparer.OrdinalIgnoreCase)
                : await ValidatePackagesAsync(spec.Packages, variables, report, cancellationToken).ConfigureAwait(false);
            foreach (var module in spec.Modules)
                await ValidateModuleAsync(module, variables, report, cancellationToken).ConfigureAwait(false);
            if (spec.CliArtifacts is not null)
                await ValidateCliArtifactsAsync(spec.CliArtifacts, variables, report, request.StagedAssets,
                    request.PublishPlan, cancellationToken).ConfigureAwait(false);
            foreach (var consumer in spec.Consumers)
                await ValidateConsumerAsync(consumer, packages, spec.Packages?.SameVersion ?? true, variables, report, cancellationToken).ConfigureAwait(false);
            foreach (var tool in spec.Tools)
                await ValidateToolAsync(tool, variables, report, cancellationToken).ConfigureAwait(false);
            foreach (var command in spec.Commands)
                await RunCommandAsync(command, variables, report, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { report.Errors.Add(ex.Message); }
        cancellationToken.ThrowIfCancellationRequested();
        return report;
    }

    /// <summary>Identifies whether any artifact or command contract remains after release-lane selection.</summary>
    internal static bool HasContracts(ReleaseValidationSpec spec)
        => spec.Packages is not null || spec.Modules.Length > 0 || spec.CliArtifacts is not null ||
           spec.Consumers.Length > 0 || spec.Tools.Length > 0 || spec.Commands.Length > 0;

    /// <summary>Runs one command with the same bounded capture and assertions used by release contracts.</summary>
    public async Task<ProcessRunResult> RunCommandAsync(ReleaseCommandValidation command,
        IReadOnlyDictionary<string, string>? variables = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ProjectRoot"] = Environment.CurrentDirectory };
        if (variables is not null)
        {
            ValidateVariableNames(variables.Keys);
            foreach (var variable in variables) values[variable.Key] = variable.Value;
        }
        values["ProjectRoot"] = Path.GetFullPath(values["ProjectRoot"]);
        return await RunCommandAsync(command, values, new ReleaseValidationReport(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProcessRunResult> RunCommandAsync(ReleaseCommandValidation command,
        IReadOnlyDictionary<string, string> variables, ReleaseValidationReport report, CancellationToken cancellationToken,
        bool expandVariables = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expectedJsonKind = ValidateCommandContract(command);
        if (!IsCommandApplicable(command))
        {
            cancellationToken.ThrowIfCancellationRequested();
            report.Checks.Add($"{command.Name}: not applicable on {CurrentPlatform}.");
            return new ProcessRunResult(0, string.Empty, string.Empty, command.FileName, TimeSpan.Zero, false);
        }
        // Generated commands already carry literal paths; only user-authored templates are expanded.
        string Value(string value) => expandVariables ? Expand(value, variables) : value;
        var workingDirectory = NormalizePath(command.WorkingDirectory is null ? variables["ProjectRoot"] : Value(command.WorkingDirectory), variables);
        var environment = command.Environment.ToDictionary(p => p.Key, p => p.Value is null ? null : Value(p.Value));
        var executable = Value(command.FileName);
        if (executable.IndexOfAny(new[] { '/', '\\' }) >= 0) executable = NormalizePath(executable, variables);
        var result = await _processRunner.RunAsync(new ProcessRunRequest(executable,
            workingDirectory, command.Arguments.Select(Value).ToArray(),
            TimeSpan.FromSeconds(command.TimeoutSeconds), environment), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.TimedOut || result.StandardOutputLimitExceeded || result.StandardErrorLimitExceeded ||
            (command.ExpectedExitCode.HasValue && result.ExitCode != command.ExpectedExitCode.Value))
            throw new InvalidOperationException($"{command.Name} failed (exit {result.ExitCode}, timed out: {result.TimedOut}, output limit exceeded: {result.StandardOutputLimitExceeded || result.StandardErrorLimitExceeded}).\n{result.StdErr}\n{result.StdOut}");
        if (command.ExpectedOutput is not null && !string.Equals(result.StdOut.Trim(), Value(command.ExpectedOutput), StringComparison.Ordinal))
            throw new InvalidOperationException($"{command.Name}: standard output did not match the expected value.");
        foreach (var text in command.OutputContains)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.StdOut.IndexOf(Value(text), StringComparison.Ordinal) < 0)
                throw new InvalidOperationException($"{command.Name}: output did not contain '{text}'.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (command.OutputJsonKind is not null || command.MinimumJsonItems.HasValue)
        {
            using var json = JsonDocument.Parse(result.StdOut);
            if (expectedJsonKind.HasValue && json.RootElement.ValueKind != expectedJsonKind.Value)
                throw new InvalidOperationException($"{command.Name}: JSON output did not match kind '{command.OutputJsonKind}'.");
            if (command.MinimumJsonItems.HasValue && (json.RootElement.ValueKind != JsonValueKind.Array ||
                json.RootElement.GetArrayLength() < command.MinimumJsonItems.Value))
                throw new InvalidOperationException($"{command.Name}: JSON output did not contain the required array items.");
        }
        foreach (var file in command.NonEmptyFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizePath(Value(file), variables);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidOperationException($"{command.Name}: expected nonempty file '{path}'.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        report.Checks.Add(command.Name);
        return result;
    }

    private static JsonValueKind? ValidateCommandContract(ReleaseCommandValidation command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (command.TimeoutSeconds <= 0 || string.IsNullOrWhiteSpace(command.FileName))
            throw new InvalidOperationException($"{command.Name}: executable and positive timeout are required.");
        if (command.Arguments is null || command.OutputContains is null || command.NonEmptyFiles is null ||
            command.Platforms is null || command.Environment is null ||
            command.Arguments.Concat(command.OutputContains).Concat(command.NonEmptyFiles).Any(value => value is null))
            throw new InvalidOperationException($"{command.Name}: command collections and their string items must not be null.");
        if (command.MinimumJsonItems < 0)
            throw new InvalidOperationException($"{command.Name}: MinimumJsonItems must not be negative.");
        if (command.OutputJsonKind is null) return null;
        if (!Enum.TryParse<JsonValueKind>(command.OutputJsonKind, true, out var kind) ||
            kind == JsonValueKind.Undefined || !Enum.IsDefined(typeof(JsonValueKind), kind) ||
            !string.Equals(kind.ToString(), command.OutputJsonKind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{command.Name}: unknown JSON output kind '{command.OutputJsonKind}'.");
        if (command.MinimumJsonItems.HasValue && kind != JsonValueKind.Array)
            throw new InvalidOperationException($"{command.Name}: MinimumJsonItems requires Array output.");
        return kind;
    }

    private static bool IsCommandApplicable(ReleaseCommandValidation command)
    {
        foreach (var platform in command.Platforms)
        {
            if (!string.Equals(platform, "Windows", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(platform, "Linux", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(platform, "OSX", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{command.Name}: unknown platform '{platform}'. Use Windows, Linux, or OSX.");
        }
        return command.Platforms.Length == 0 || command.Platforms.Contains(CurrentPlatform, StringComparer.OrdinalIgnoreCase);
    }

    private static string CurrentPlatform => FrameworkCompatibility.IsWindows() ? "Windows" :
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) ? "OSX" : "Linux";
    private const string VariableNamePattern = @"[A-Za-z_][A-Za-z0-9_]*";
    private static void ValidateVariableNames(IEnumerable<string> names)
    {
        foreach (var name in names)
            if (!Regex.IsMatch(name, "^" + VariableNamePattern + @"(?![\s\S])", RegexOptions.None, TimeSpan.FromSeconds(1)))
                throw new InvalidOperationException($"Invalid validation variable name '{name}'. Use letters, digits, and underscores, starting with a letter or underscore.");
    }
    private static string Expand(string value, IReadOnlyDictionary<string, string> variables)
        => Regex.Replace(value, @"\{(" + VariableNamePattern + @")\}", match =>
            variables.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement :
                throw new InvalidOperationException($"Missing validation variable {match.Value}."), RegexOptions.None, TimeSpan.FromSeconds(1));
    private static string Resolve(string value, IReadOnlyDictionary<string, string> variables)
        => NormalizePath(Expand(value, variables), variables);
    private static string NormalizePath(string value, IReadOnlyDictionary<string, string> variables)
    {
        var expanded = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(variables["ProjectRoot"], expanded));
    }
    private static bool Matches(string value, string pattern)
    {
        var expression = Regex.Escape(pattern.Replace('\\', '/')).Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
        return Regex.IsMatch(value.Replace('\\', '/'), "^" + expression + "$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
