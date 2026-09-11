using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Reusable final-artifact validation and isolated product smoke-test execution.</summary>
public sealed partial class ReleaseValidationService
{
    private readonly IProcessRunner _processRunner;
    /// <summary>Creates a validator using the shared process runner.</summary>
    public ReleaseValidationService(IProcessRunner? processRunner = null)
        => _processRunner = processRunner ?? new ProcessRunner();

    /// <summary>Loads a JSON validation contract.</summary>
    public static ReleaseValidationSpec Load(string configPath)
        => JsonSerializer.Deserialize(File.ReadAllText(configPath), ReleaseValidationJsonContext.Default.ReleaseValidationSpec)
            ?? throw new InvalidOperationException("Validation configuration is empty.");

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
            if (spec.Packages is null && spec.Modules.Length == 0 && spec.CliArtifacts is null &&
                spec.Consumers.Length == 0 && spec.Tools.Length == 0 && spec.Commands.Length == 0)
                throw new InvalidOperationException("Release validation requires at least one artifact or command contract.");
            var basePath = configPath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            var root = Path.GetFullPath(request.ProjectRoot ?? Path.Combine(basePath, spec.ProjectRoot));
            var variables = new Dictionary<string, string>(request.Variables, StringComparer.OrdinalIgnoreCase)
            { ["ProjectRoot"] = root, ["Version"] = report.Version };
            var packages = spec.Packages is null ? new Dictionary<string, PackageInspection>(StringComparer.OrdinalIgnoreCase)
                : await ValidatePackagesAsync(spec.Packages, variables, report, cancellationToken).ConfigureAwait(false);
            foreach (var module in spec.Modules)
                await ValidateModuleAsync(module, variables, report, cancellationToken).ConfigureAwait(false);
            if (spec.CliArtifacts is not null) ValidateCliArtifacts(spec.CliArtifacts, variables, report, request.StagedAssets);
            foreach (var consumer in spec.Consumers)
                await ValidateConsumerAsync(consumer, packages, variables, report, cancellationToken).ConfigureAwait(false);
            foreach (var tool in spec.Tools)
                await ValidateToolAsync(tool, variables, report, cancellationToken).ConfigureAwait(false);
            foreach (var command in spec.Commands)
                await RunCommandAsync(command, variables, report, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { report.Errors.Add(ex.Message); }
        return report;
    }

    /// <summary>Runs one command with the same bounded capture and assertions used by release contracts.</summary>
    public async Task<ProcessRunResult> RunCommandAsync(ReleaseCommandValidation command,
        IReadOnlyDictionary<string, string>? variables = null, CancellationToken cancellationToken = default)
    {
        var values = variables ?? new Dictionary<string, string> { ["ProjectRoot"] = Environment.CurrentDirectory };
        return await RunCommandAsync(command, values, new ReleaseValidationReport(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProcessRunResult> RunCommandAsync(ReleaseCommandValidation command,
        IReadOnlyDictionary<string, string> variables, ReleaseValidationReport report, CancellationToken cancellationToken)
    {
        if (command.Platforms.Length > 0 && !command.Platforms.Contains(CurrentPlatform, StringComparer.OrdinalIgnoreCase))
        {
            report.Checks.Add($"{command.Name}: not applicable on {CurrentPlatform}.");
            return new ProcessRunResult(0, string.Empty, string.Empty, command.FileName, TimeSpan.Zero, false);
        }
        if (command.TimeoutSeconds <= 0 || string.IsNullOrWhiteSpace(command.FileName))
            throw new InvalidOperationException($"{command.Name}: executable and positive timeout are required.");
        var workingDirectory = Resolve(command.WorkingDirectory ?? "{ProjectRoot}", variables);
        var environment = command.Environment.ToDictionary(p => p.Key, p => p.Value is null ? null : Expand(p.Value, variables));
        var executable = Expand(command.FileName, variables);
        if (executable.IndexOfAny(new[] { '/', '\\' }) >= 0) executable = Resolve(executable, variables);
        var result = await _processRunner.RunAsync(new ProcessRunRequest(executable,
            workingDirectory, command.Arguments.Select(a => Expand(a, variables)).ToArray(),
            TimeSpan.FromSeconds(command.TimeoutSeconds), environment), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.TimedOut || result.StandardOutputLimitExceeded || result.StandardErrorLimitExceeded ||
            (command.ExpectedExitCode.HasValue && result.ExitCode != command.ExpectedExitCode.Value))
            throw new InvalidOperationException($"{command.Name} failed (exit {result.ExitCode}, timed out: {result.TimedOut}).\n{result.StdErr}\n{result.StdOut}");
        if (command.ExpectedOutput is not null && !string.Equals(result.StdOut.Trim(), Expand(command.ExpectedOutput, variables), StringComparison.Ordinal))
            throw new InvalidOperationException($"{command.Name}: standard output did not match the expected value.");
        foreach (var text in command.OutputContains)
            if (result.StdOut.IndexOf(Expand(text, variables), StringComparison.Ordinal) < 0)
                throw new InvalidOperationException($"{command.Name}: output did not contain '{text}'.");
        if (command.OutputJsonKind is not null || command.MinimumJsonItems.HasValue)
        {
            using var json = JsonDocument.Parse(result.StdOut);
            if (command.OutputJsonKind is not null && (!Enum.TryParse<JsonValueKind>(command.OutputJsonKind, true, out var expectedKind) ||
                expectedKind == JsonValueKind.Undefined || json.RootElement.ValueKind != expectedKind))
                throw new InvalidOperationException($"{command.Name}: JSON output did not match kind '{command.OutputJsonKind}'.");
            if (command.MinimumJsonItems.HasValue && (command.MinimumJsonItems < 0 || json.RootElement.ValueKind != JsonValueKind.Array ||
                json.RootElement.GetArrayLength() < command.MinimumJsonItems.Value))
                throw new InvalidOperationException($"{command.Name}: JSON output did not contain the required array items.");
        }
        foreach (var file in command.NonEmptyFiles)
        {
            var path = Resolve(file, variables);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidOperationException($"{command.Name}: expected nonempty file '{path}'.");
        }
        report.Checks.Add(command.Name);
        return result;
    }

    private static string CurrentPlatform => FrameworkCompatibility.IsWindows() ? "Windows" :
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) ? "OSX" : "Linux";
    private static string Expand(string value, IReadOnlyDictionary<string, string> variables)
        => Regex.Replace(value, @"\{([A-Za-z][A-Za-z0-9]*)\}", match =>
            variables.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement :
                throw new InvalidOperationException($"Missing validation variable {match.Value}."), RegexOptions.None, TimeSpan.FromSeconds(1));
    private static string Resolve(string value, IReadOnlyDictionary<string, string> variables)
    {
        var expanded = Expand(value, variables).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(variables["ProjectRoot"], expanded));
    }
    private static bool Matches(string value, string pattern)
    {
        var expression = Regex.Escape(pattern.Replace('\\', '/')).Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
        return Regex.IsMatch(value.Replace('\\', '/'), "^" + expression + "$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
