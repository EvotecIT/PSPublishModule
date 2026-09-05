using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Executes a script in an exact external PowerShell host and returns a normalized, portable semantic observation.
/// The runner does not load compiler implementation assemblies into the oracle host.
/// </summary>
public sealed class PowerShellCompilationSemanticOracleRunner
{
    private const string WrapperResourceName = "PowerForge.PowerShell.Compilation.SemanticOracle.Observe.ps1";

    /// <summary>Executes one black-box observation in the external host named by the selected profile.</summary>
    public PowerShellCompilationSemanticOracleEnvelope Observe(PowerShellCompilationSemanticOracleRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (!File.Exists(request.ScriptPath)) throw new FileNotFoundException("Semantic-oracle script was not found.", request.ScriptPath);
        if (request.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(request.TimeoutSeconds));
        var profile = PowerShellCompilationSemanticOracleCatalog.Get(request.ProfileId);
        var culture = string.IsNullOrWhiteSpace(request.Culture) ? "en-US" : request.Culture.Trim();
        _ = System.Globalization.CultureInfo.GetCultureInfo(culture);
        var executionSurface = NormalizeHostBackedSurface(request.ExecutionSurface);
        var expectedHostArtifact = NormalizeExpectedHostArtifact(request.ExpectedHostArtifactSha256);
        var hostExecutable = ResolveHostExecutable(profile, request.HostExecutablePath);
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeSemanticOracle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        PowerShellCompilationSemanticWindowsProcessObserver? processObserver = null;
        var observationTimeout = TimeSpan.FromSeconds(request.TimeoutSeconds);
        var observationStopwatch = Stopwatch.StartNew();
        try
        {
            var wrapperPath = Path.Combine(root, "Observe.ps1");
            var configPath = Path.Combine(root, "request.json");
            var outputPath = Path.Combine(root, "observation.json");
            var readyPath = Path.Combine(root, "process-observer.ready");
            var gatePath = Path.Combine(root, "process-observer.gate");
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                processObserver = new PowerShellCompilationSemanticWindowsProcessObserver(
                    PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems);
            File.WriteAllText(wrapperPath, LoadWrapperSource(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var config = new
            {
                request.ProfileId,
                ScriptPath = Path.GetFullPath(request.ScriptPath),
                Arguments = request.Arguments ?? Array.Empty<string>(),
                ObservedPropertyNames = NormalizePropertyNames(request.ObservedPropertyNames),
                Culture = culture,
                FileSystemRoot = string.IsNullOrWhiteSpace(request.FileSystemRoot) ? string.Empty : Path.GetFullPath(request.FileSystemRoot),
                ExecutionSurface = executionSurface,
                FeatureSwitches = profile.FeatureSwitches,
                ProcessObserverReadyPath = processObserver is null ? string.Empty : readyPath,
                ProcessObserverGatePath = processObserver is null ? string.Empty : gatePath,
                OutputPath = outputPath
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(config), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var processRequest = new ProcessRunRequest(
                hostExecutable,
                root,
                new[] { "-NoProfile", "-NonInteractive", "-File", wrapperPath, "-ConfigPath", configPath },
                Remaining(observationTimeout, observationStopwatch));
            if (processObserver is not null)
            {
                processRequest.SetStartedProcessBoundary(processId =>
                {
                    processObserver.Attach(processId);
                    WaitForObserverReady(readyPath, Remaining(observationTimeout, observationStopwatch));
                    processObserver.BeginAuthoredObservation(Remaining(observationTimeout, observationStopwatch));
                    File.WriteAllText(gatePath, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                });
            }
            var run = new ProcessRunner().RunAsync(processRequest)
                .GetAwaiter()
                .GetResult();
            if (run.TimedOut)
                throw new TimeoutException($"Semantic oracle '{profile.ProfileId}' exceeded {request.TimeoutSeconds} seconds.");
            if (run.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidOperationException($"Semantic oracle '{profile.ProfileId}' failed with exit code {run.ExitCode}. {Bound(run.StdErr)}");

            var envelope = JsonSerializer.Deserialize<PowerShellCompilationSemanticOracleEnvelope>(File.ReadAllText(outputPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Semantic oracle produced an empty observation.");
            envelope.ProcessEffects = processObserver?.Complete(Remaining(observationTimeout, observationStopwatch)) ??
                Array.Empty<PowerShellCompilationSemanticProcessEffectObservation>();
            ValidateHost(profile, envelope, culture, executionSurface, expectedHostArtifact);
            return envelope;
        }
        finally
        {
            processObserver?.Dispose();
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void WaitForObserverReady(string path, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException("The semantic-oracle host did not reach its process-observation start gate.");
            Thread.Sleep(10);
        }
    }

    private static TimeSpan Remaining(TimeSpan timeout, Stopwatch stopwatch)
    {
        var remaining = timeout - stopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException("The semantic-oracle observation exceeded its requested timeout before authored source started.");
        return remaining;
    }

    private static string LoadWrapperSource()
    {
        using var stream = typeof(PowerShellCompilationSemanticOracleRunner).Assembly.GetManifestResourceStream(WrapperResourceName)
            ?? throw new InvalidOperationException($"Embedded semantic-oracle wrapper '{WrapperResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ResolveHostExecutable(PowerShellCompilationSemanticOracleProfile profile, string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath)) return profile.HostExecutable;
        var fullPath = Path.GetFullPath(requestedPath!.Trim());
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The exact semantic-oracle host executable was not found.", fullPath);
        return fullPath;
    }

    private static string NormalizeExpectedHostArtifact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value!.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("ExpectedHostArtifactSha256 must be a 64-character hexadecimal SHA-256 value.", nameof(PowerShellCompilationSemanticOracleRequest.ExpectedHostArtifactSha256));
        return normalized;
    }

    private static string NormalizeHostBackedSurface(string? value)
    {
        var requested = string.IsNullOrWhiteSpace(value) ? "Interpreted" : value!.Trim();
        if (!Enum.TryParse<PowerShellCompilationSemanticExecutionSurface>(requested, ignoreCase: true, out var surface) ||
            (surface != PowerShellCompilationSemanticExecutionSurface.Interpreted &&
             surface != PowerShellCompilationSemanticExecutionSurface.Hybrid))
            throw new ArgumentException("An external semantic-oracle observation must use the Interpreted or Hybrid execution surface.", nameof(value));
        return surface.ToString();
    }

    private static string[] NormalizePropertyNames(IEnumerable<string>? names)
    {
        var normalized = (names ?? Array.Empty<string>())
            .Select(static name => name?.Trim() ?? string.Empty)
            .Where(static name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var forbidden = normalized.FirstOrDefault(static name =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SessionId", StringComparison.OrdinalIgnoreCase));
        if (forbidden is not null)
            throw new ArgumentException($"Portable semantic observations forbid sensitive or live-runtime property '{forbidden}'.", nameof(names));
        return normalized;
    }

    private static void ValidateHost(
        PowerShellCompilationSemanticOracleProfile profile,
        PowerShellCompilationSemanticOracleEnvelope envelope,
        string expectedCulture,
        string expectedExecutionSurface,
        string expectedHostArtifact)
    {
        if (envelope.SchemaVersion != 3)
            throw new InvalidOperationException($"Unsupported semantic-oracle envelope schema {envelope.SchemaVersion}.");
        if (!string.Equals(profile.ProfileId, envelope.ProfileId, StringComparison.Ordinal))
            throw new InvalidOperationException("Semantic oracle returned the wrong profile identity.");
        if (!string.Equals(expectedExecutionSurface, envelope.ExecutionSurface, StringComparison.Ordinal))
            throw new InvalidOperationException("Semantic oracle returned the wrong execution-surface identity.");
        var artifact = PowerShellCompilationSemanticHostArtifactService.Normalize(envelope.HostArtifact
            ?? throw new InvalidOperationException("Semantic oracle did not report its exact host artifact."));
        PowerShellCompilationSemanticHostArtifactService.EnsureMatchesProfile(artifact, profile, expectedCulture);
        if (expectedHostArtifact.Length > 0 && !string.Equals(expectedHostArtifact, artifact.IdentitySha256, StringComparison.Ordinal))
            throw new InvalidOperationException($"Semantic oracle host artifact '{artifact.IdentitySha256}' does not match the required identity '{expectedHostArtifact}'.");
        ValidateEnvelopeMirror(envelope, artifact);
        PowerShellCompilationSemanticOracleEnvelopeValidator.Validate(envelope, profile.ProfileId);
    }

    private static void ValidateEnvelopeMirror(
        PowerShellCompilationSemanticOracleEnvelope envelope,
        PowerShellCompilationSemanticHostArtifact artifact)
    {
        if (!string.Equals(envelope.HostVersion, artifact.HostVersion, StringComparison.Ordinal) ||
            !string.Equals(envelope.PowerShellEdition, artifact.PowerShellEdition, StringComparison.Ordinal) ||
            !string.Equals(envelope.OperatingSystem, artifact.OperatingSystem, StringComparison.Ordinal) ||
            !string.Equals(envelope.Architecture, artifact.Architecture, StringComparison.Ordinal) ||
            !string.Equals(envelope.Culture, artifact.Culture, StringComparison.Ordinal))
            throw new InvalidOperationException("Semantic-oracle envelope identity does not match its integrity-bound host artifact.");
    }

    private static string Bound(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= 4096 ? value : value.Substring(value.Length - 4096);
}
