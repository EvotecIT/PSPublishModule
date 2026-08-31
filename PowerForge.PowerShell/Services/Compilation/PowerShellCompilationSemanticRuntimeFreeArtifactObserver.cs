using System.Globalization;
using System.Security.Cryptography;

namespace PowerForge;

/// <summary>Executes a certified Strict executable and converts its typed ABI/output into a schema-3 oracle envelope.</summary>
public sealed class PowerShellCompilationSemanticRuntimeFreeArtifactObserver
{
    private readonly IProcessRunner _processRunner;

    /// <summary>Creates an observer using the default structured process runner.</summary>
    public PowerShellCompilationSemanticRuntimeFreeArtifactObserver()
        : this(new ProcessRunner())
    {
    }

    /// <summary>Creates an observer using an explicit process boundary.</summary>
    public PowerShellCompilationSemanticRuntimeFreeArtifactObserver(IProcessRunner processRunner)
        => _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    /// <summary>Executes one runtime-free Strict executable observation.</summary>
    public PowerShellCompilationSemanticOracleEnvelope Observe(
        string profileId,
        PowerShellCompilationBuildResult build,
        IReadOnlyList<string>? arguments = null,
        CultureInfo? culture = null,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        if (build is null) throw new ArgumentNullException(nameof(build));
        if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        var artifactPath = RequireCertifiedExecutable(profileId, build);
        var manifest = build.Manifest!;
        var method = manifest.PublicAbi!.Methods[0];
        culture ??= CultureInfo.GetCultureInfo("en-US");
        RequireObservableOutputContract(method, culture);
        var workingDirectory = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("The Strict executable has no working directory.");
        var request = new ProcessRunRequest(
            artifactPath,
            workingDirectory,
            arguments ?? Array.Empty<string>(),
            TimeSpan.FromSeconds(timeoutSeconds))
        {
            MaxCapturedOutputCharacters = PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationTextCharacters
        };
        var run = _processRunner.RunAsync(request, cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (run.TimedOut)
            throw new TimeoutException($"Runtime-free semantic observation exceeded {timeoutSeconds} seconds.");
        if (run.StandardOutputLimitExceeded || run.StandardErrorLimitExceeded)
            throw new InvalidDataException("Runtime-free semantic observation exceeded the bounded process-output limit.");
        if (run.StdOut is null || run.StdErr is null ||
            run.StdOut.Length > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationTextCharacters ||
            run.StdErr.Length > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationTextCharacters)
            throw new InvalidDataException("Runtime-free semantic observation returned null or oversized process output.");

        object? value = null;
        if (run.ExitCode == 0)
            value = ParseSuccessOutput(run.StdOut, method, culture);
        var envelope = PowerShellCompilationSemanticRuntimeFreeObserver.Observe(
            profileId,
            PowerShellCompilationSemanticExecutionSurface.Strict,
            value,
            culture: culture,
            exitCode: run.ExitCode,
            encoding: new PowerShellCompilationSemanticEncodingObservation
            {
                ConsoleInput = "utf-8",
                ConsoleOutput = "utf-8",
                ObservationFile = "utf-8"
            });
        if (run.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(run.StdErr) ? $"Strict executable exited with code {run.ExitCode}." : run.StdErr.Trim();
            envelope.Errors = new[] { message };
            envelope.ErrorRecords = new[]
            {
                new PowerShellCompilationSemanticErrorObservation
                {
                    Sequence = 1,
                    Message = message,
                    FullyQualifiedErrorId = "PowerForge.StrictExecutable.ExitCode",
                    Category = "NotSpecified",
                    ExceptionTypeName = typeof(InvalidOperationException).FullName!,
                    IsTerminating = true
                }
            };
        }
        return envelope;
    }

    private static string RequireCertifiedExecutable(string profileId, PowerShellCompilationBuildResult build)
    {
        if (!build.Succeeded || string.IsNullOrWhiteSpace(build.ArtifactPath) || build.Manifest is null)
            throw new InvalidOperationException("A successful Strict build result is required for runtime-free observation.");
        var manifest = build.Manifest;
        if (manifest.Kind != PowerShellCompilationArtifactKind.Executable || manifest.Mode != PowerShellCompilationMode.Strict)
            throw new InvalidOperationException("Runtime-free artifact observation requires a Strict executable.");
        if (manifest.RequiresPowerShellRuntime || manifest.UsesPowerShellRuntimeFallback || manifest.ContainsEmbeddedPowerShellSource ||
            manifest.AllowsPowerShellRuntimeEvaluation || !manifest.DependencyClosureVerified || manifest.SemanticProfile?.RuntimeFree != true)
            throw new InvalidOperationException("The executable is not certified as a complete runtime-free Strict artifact.");
        PowerShellCompilationArtifactEvidence.Validate(manifest);
        var target = PowerShellCompilationTargetContractService.Normalize(
            manifest.TargetContract ?? throw new InvalidOperationException("The Strict executable has no canonical target contract."));
        if (!target.SemanticProfileId.Equals(profileId, StringComparison.Ordinal) ||
            target.ArtifactKind != manifest.Kind || target.Mode != manifest.Mode || target.AllowsPowerShellRuntimeEvaluation)
            throw new InvalidOperationException("The Strict executable target contract does not match the selected semantic profile or manifest.");
        if (manifest.PublicAbi?.Methods.Length != 1)
            throw new InvalidOperationException("A Strict executable observation requires exactly one typed entry-point ABI method.");
        var path = Path.GetFullPath(build.ArtifactPath!);
        if (!File.Exists(path))
            throw new FileNotFoundException("The Strict executable artifact was not found.", path);
        if (!PowerShellCompilationPathSafety.PathEquals(path, manifest.ArtifactPath))
            throw new InvalidOperationException("The Strict executable path differs from its compiler manifest.");
        ValidateArtifactInventory(path, manifest);
        return path;
    }

    private static void ValidateArtifactInventory(string artifactPath, PowerShellCompilationArtifactManifest manifest)
    {
        var root = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("The Strict executable has no artifact root.");
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(root, "The Strict executable artifact root traverses a symbolic link or junction.");
        var files = manifest.Files ?? Array.Empty<PowerShellCompilationArtifactFile>();
        if (files.Length == 0) throw new InvalidOperationException("The Strict executable manifest records no artifact inventory.");
        var paths = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        PowerShellCompilationArtifactFile? primary = null;
        foreach (var file in files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path))
                throw new InvalidOperationException("The Strict executable manifest contains incomplete artifact-file evidence.");
            var path = Path.GetFullPath(file.Path);
            if (!File.Exists(path)) throw new FileNotFoundException("A Strict executable artifact file is missing.", path);
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "A Strict executable artifact file traverses a symbolic link or junction.");
            if (!paths.Add(path)) throw new InvalidOperationException($"The Strict executable manifest repeats artifact file '{path}'.");
            var info = new FileInfo(path);
            var hash = ComputeSha256(path);
            if (info.Length != file.SizeBytes || !hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Strict executable artifact file '{path}' differs from its compiler evidence.");
            if (PowerShellCompilationPathSafety.PathEquals(path, artifactPath)) primary = file;
        }
        if (primary is null) throw new InvalidOperationException("The Strict executable is absent from its authenticated artifact inventory.");
        if (!primary.Sha256.Equals(manifest.ArtifactSha256, StringComparison.OrdinalIgnoreCase) ||
            primary.SizeBytes != manifest.ArtifactSizeBytes)
            throw new InvalidOperationException("The Strict executable primary artifact identity differs from its compiler manifest.");
    }

    private static void RequireObservableOutputContract(PowerShellCompilationAbiMethod method, CultureInfo culture)
    {
        _ = culture;
        var cardinality = method.OutputCardinality;
        if (cardinality.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            if (method.OutputValueStates.Length != 0 || method.CanProduceNull || method.Nullable)
                throw new InvalidOperationException("No-output ABI evidence is internally contradictory.");
            return;
        }
        if (!cardinality.Equals("Scalar", StringComparison.OrdinalIgnoreCase) &&
            !cardinality.Equals("Collection", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported Strict executable ABI cardinality '{cardinality}'.");
        var hasLineSafeState = method.OutputValueStates.Length == 1 &&
            method.OutputValueStates[0] is "Known" or "Unknown";
        if (method.CanProduceNull || method.Nullable || !hasLineSafeState)
            throw new InvalidOperationException(
                $"Line-based Strict observation rejects nullable or sentinel-bearing ABI output; a framed protocol is required. " +
                $"States=[{string.Join(",", method.OutputValueStates)}], CanProduceNull={method.CanProduceNull}, Nullable={method.Nullable}.");
        var typeName = cardinality.Equals("Collection", StringComparison.OrdinalIgnoreCase)
            ? method.CollectionElementType
            : method.ReturnType;
        if (!IsLineSafeInvariantType(typeName))
            throw new InvalidOperationException($"Line-based Strict observation rejects ABI type '{typeName}'; a framed protocol is required.");
    }

    private static bool IsLineSafeInvariantType(string typeName)
        => typeName is "System.Boolean" or "System.Byte" or "System.SByte" or "System.Int16" or
            "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64";

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream)
            .Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static object? ParseSuccessOutput(string output, PowerShellCompilationAbiMethod method, CultureInfo culture)
    {
        var lines = SplitLines(output);
        if (method.OutputCardinality.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            if (lines.Length != 0)
                throw new InvalidDataException("The Strict executable emitted success output contrary to its ABI cardinality.");
            return null;
        }

        if (method.OutputCardinality.Equals("Scalar", StringComparison.OrdinalIgnoreCase))
        {
            if (lines.Length != 1)
                throw new InvalidDataException($"The Strict executable emitted {lines.Length} lines for a scalar ABI result.");
            return ParseValue(lines[0], method.ReturnType, culture);
        }

        if (!method.OutputCardinality.Equals("Collection", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported Strict executable ABI cardinality '{method.OutputCardinality}'.");
        if (lines.Length > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems)
            throw new InvalidDataException("The Strict executable exceeded the bounded success-output cardinality.");
        return lines.Select(line => ParseValue(line, method.CollectionElementType, culture)).ToArray();
    }

    private static string[] SplitLines(string value)
    {
        if (string.IsNullOrEmpty(value))
            return Array.Empty<string>();
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        return lines.Length > 0 && lines[lines.Length - 1].Length == 0 ? lines.Take(lines.Length - 1).ToArray() : lines;
    }

    private static object ParseValue(string value, string typeName, CultureInfo culture)
        => typeName switch
        {
            "System.String" => value,
            "System.Boolean" => bool.Parse(value),
            "System.Byte" => byte.Parse(value, NumberStyles.Integer, culture),
            "System.SByte" => sbyte.Parse(value, NumberStyles.Integer, culture),
            "System.Int16" => short.Parse(value, NumberStyles.Integer, culture),
            "System.UInt16" => ushort.Parse(value, NumberStyles.Integer, culture),
            "System.Int32" => int.Parse(value, NumberStyles.Integer, culture),
            "System.UInt32" => uint.Parse(value, NumberStyles.Integer, culture),
            "System.Int64" => long.Parse(value, NumberStyles.Integer, culture),
            "System.UInt64" => ulong.Parse(value, NumberStyles.Integer, culture),
            _ => throw new InvalidDataException($"Runtime-free semantic observation does not support ABI value type '{typeName}'.")
        };
}
