using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace PowerForge;

/// <summary>Executes a certified Strict executable and converts its typed ABI/output into a schema-3 oracle envelope.</summary>
public sealed class PowerShellCompilationSemanticRuntimeFreeArtifactObserver
{
    private const string ObservationProtocol = "PowerForge.StrictObservation/1";
    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
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
        var artifactPath = RequireCertifiedExecutable(profileId, build, out var method);
        var manifest = build.Manifest!;
        culture ??= CultureInfo.GetCultureInfo("en-US");
        RequireObservableOutputContract(method, culture);
        var workingDirectory = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("The Strict executable has no working directory.");
        var request = new ProcessRunRequest(
            artifactPath,
            workingDirectory,
            arguments ?? Array.Empty<string>(),
            TimeSpan.FromSeconds(timeoutSeconds),
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["POWERFORGE_SEMANTIC_OBSERVATION_PROTOCOL"] = ObservationProtocol,
                ["POWERFORGE_SEMANTIC_OBSERVATION_CULTURE"] = culture.Name
            })
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

        var encoding = new PowerShellCompilationSemanticEncodingObservation
        {
            ConsoleInput = "utf-8",
            ConsoleOutput = "utf-8",
            ObservationFile = "utf-8"
        };
        var envelope = run.ExitCode == 0
            ? CreateFramedEnvelope(profileId, ParseSuccessOutput(run.StdOut, method), culture, run.ExitCode, encoding)
            : PowerShellCompilationSemanticRuntimeFreeObserver.Observe(
                profileId,
                PowerShellCompilationSemanticExecutionSurface.Strict,
                value: null,
                culture: culture,
                exitCode: run.ExitCode,
                encoding: encoding);
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

    private static string RequireCertifiedExecutable(
        string profileId,
        PowerShellCompilationBuildResult build,
        out PowerShellCompilationAbiMethod entryPoint)
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
        var path = Path.GetFullPath(build.ArtifactPath!);
        if (!File.Exists(path))
            throw new FileNotFoundException("The Strict executable artifact was not found.", path);
        if (!PowerShellCompilationPathSafety.PathEquals(path, manifest.ArtifactPath))
            throw new InvalidOperationException("The Strict executable path differs from its compiler manifest.");
        ValidateArtifactInventory(path, manifest);
        entryPoint = SelectEntryPoint(manifest.PublicAbi);
        ValidateEmbeddedPublicAbi(manifest, manifest.PublicAbi!.Sha256);
        return path;
    }

    internal static PowerShellCompilationAbiMethod SelectEntryPoint(PowerShellCompilationAbiManifest? publicAbi)
    {
        ValidateCanonicalPublicAbi(publicAbi);
        var entries = publicAbi?.Methods
            .Where(static method =>
                method.ClrName.Equals("Invoke", StringComparison.Ordinal) &&
                method.PowerShellName.Equals("<script>", StringComparison.Ordinal))
            .ToArray() ?? Array.Empty<PowerShellCompilationAbiMethod>();
        return entries.Length == 1
            ? entries[0]
            : throw new InvalidOperationException(
                "A Strict executable observation requires exactly one compiler-generated '<script>' ABI method named 'Invoke'.");
    }

    private static void ValidateCanonicalPublicAbi(PowerShellCompilationAbiManifest? publicAbi)
    {
        if (publicAbi is null || publicAbi.Methods is null || publicAbi.Methods.Any(static method => method is null))
            throw new InvalidOperationException("A Strict executable observation requires a complete public ABI manifest.");
        string computed;
        try
        {
            computed = PowerShellCompilationAbiBuilder.ComputeSha256(
                PowerShellCompilationAbiBuilder.GetNormalizedText(publicAbi));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            throw new InvalidOperationException("The Strict executable public ABI manifest is malformed.", exception);
        }
        if (!computed.Equals(publicAbi.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Strict executable public ABI manifest failed its canonical hash check.");
    }

    internal static void ValidateEmbeddedPublicAbi(PowerShellCompilationArtifactManifest manifest, string expectedSha256)
    {
        var values = (manifest.Files ?? Array.Empty<PowerShellCompilationArtifactFile>())
            .Where(static file => file is not null &&
                                  (Path.GetExtension(file.Path).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetExtension(file.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(static file => ReadAssemblyMetadataValues(file.Path, "PowerForge.PublicAbiSha256"))
            .ToArray();
        if (values.Length != 1 || !values[0].Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The Strict executable public ABI hash is missing, ambiguous, or differs from its embedded assembly metadata.");
    }

    private static IEnumerable<string> ReadAssemblyMetadataValues(string path, string key)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata) yield break;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) yield break;
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (!GetAttributeTypeName(reader, attribute.Constructor)
                    .Equals("System.Reflection.AssemblyMetadataAttribute", StringComparison.Ordinal))
                continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) continue;
            var attributeKey = blob.ReadSerializedString();
            var attributeValue = blob.ReadSerializedString();
            if (key.Equals(attributeKey, StringComparison.Ordinal) && attributeValue is not null)
                yield return attributeValue;
        }
    }

    private static string GetAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        var type = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };
        return type.Kind switch
        {
            HandleKind.TypeReference => GetTypeName(reader, reader.GetTypeReference((TypeReferenceHandle)type)),
            HandleKind.TypeDefinition => GetTypeName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)type)),
            _ => string.Empty
        };
    }

    private static string GetTypeName(MetadataReader reader, TypeReference type)
        => reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);

    private static string GetTypeName(MetadataReader reader, TypeDefinition type)
        => reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);

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
        if (method.OutputValueStates.Length == 0 ||
            method.OutputValueStates.Any(static state => state is not "Known" and not "Unknown" and not "Null"))
            throw new InvalidOperationException(
                $"The framed Strict observation protocol does not support ABI states [{string.Join(",", method.OutputValueStates)}].");
        if (method.OutputValueStates.Contains("Null", StringComparer.Ordinal) && !method.CanProduceNull && !method.Nullable)
            throw new InvalidOperationException("The Strict executable ABI reports a null output state without a nullable contract.");
        var typeName = cardinality.Equals("Collection", StringComparison.OrdinalIgnoreCase)
            ? method.CollectionElementType
            : method.ReturnType;
        if (!IsFramedScalarType(typeName))
            throw new InvalidOperationException($"The framed Strict observation protocol does not support ABI type '{typeName}'.");
    }

    private static bool IsFramedScalarType(string typeName)
    {
        typeName = GetFramedScalarTypeName(typeName);
        return typeName is "System.Boolean" or "System.Byte" or "System.SByte" or "System.Int16" or
            "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" or
            "System.Single" or "System.Double" or "System.Decimal" or "System.Char" or "System.String";
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream)
            .Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static PowerShellCompilationSemanticValueObservation[] ParseSuccessOutput(
        string output,
        PowerShellCompilationAbiMethod method)
    {
        var lines = SplitLines(output);
        if (lines.Length < 2)
            throw new InvalidDataException("The Strict executable did not emit a complete framed semantic observation.");
        var begin = ParseFrame(lines[0]);
        var end = ParseFrame(lines[lines.Length - 1]);
        if (begin.State != "BEGIN" || begin.TypeName.Length != 0 || begin.Value.Length != 0)
            throw new InvalidDataException("The Strict semantic observation has an invalid begin frame.");
        if (end.State != "END" || end.TypeName.Length != 0 ||
            !int.TryParse(end.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredCount) || declaredCount < 0)
            throw new InvalidDataException("The Strict semantic observation has an invalid end frame.");
        var valueLines = lines.Skip(1).Take(lines.Length - 2).ToArray();
        if (declaredCount != valueLines.Length)
            throw new InvalidDataException("The Strict semantic observation frame count is inconsistent.");
        if (declaredCount > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems)
            throw new InvalidDataException("The Strict executable exceeded the bounded success-output cardinality.");

        var cardinality = method.OutputCardinality;
        if (cardinality.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            if (valueLines.Length != 0)
                throw new InvalidDataException("The Strict executable emitted success output contrary to its ABI cardinality.");
            return Array.Empty<PowerShellCompilationSemanticValueObservation>();
        }
        if (cardinality.Equals("Scalar", StringComparison.OrdinalIgnoreCase) && valueLines.Length != 1)
            throw new InvalidDataException($"The Strict executable emitted {valueLines.Length} framed values for a scalar ABI result.");
        if (!cardinality.Equals("Scalar", StringComparison.OrdinalIgnoreCase) &&
            !cardinality.Equals("Collection", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported Strict executable ABI cardinality '{cardinality}'.");

        var expectedType = cardinality.Equals("Collection", StringComparison.OrdinalIgnoreCase)
            ? method.CollectionElementType
            : method.ReturnType;
        expectedType = GetFramedScalarTypeName(expectedType);
        var result = new PowerShellCompilationSemanticValueObservation[valueLines.Length];
        for (var index = 0; index < valueLines.Length; index++)
        {
            var frame = ParseFrame(valueLines[index]);
            if (frame.State == "NULL")
            {
                if (frame.TypeName.Length != 0 || frame.Value.Length != 0 || !method.CanProduceNull && !method.Nullable)
                    throw new InvalidDataException("The Strict executable emitted a null frame contrary to its ABI contract.");
                result[index] = new PowerShellCompilationSemanticValueObservation
                {
                    Sequence = index + 1,
                    IsNull = true,
                    ValueState = "Null",
                    EnumerationState = "Scalar"
                };
                continue;
            }
            if (frame.State != "VALUE" || !frame.TypeName.Equals(expectedType, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"The Strict executable emitted framed type '{frame.TypeName}' for ABI type '{expectedType}'.");
            result[index] = new PowerShellCompilationSemanticValueObservation
            {
                Sequence = index + 1,
                Value = frame.Value,
                TypeName = frame.TypeName,
                ValueState = "Value",
                EnumerationState = "Scalar"
            };
        }
        return result;
    }

    private static string GetFramedScalarTypeName(string typeName)
    {
        const string nullablePrefix = "System.Nullable`1[[";
        if (!typeName.StartsWith(nullablePrefix, StringComparison.Ordinal))
            return typeName;
        var separator = typeName.IndexOf(',', nullablePrefix.Length);
        if (separator <= nullablePrefix.Length)
            throw new InvalidDataException($"The Strict executable ABI contains malformed nullable type '{typeName}'.");
        return typeName.Substring(nullablePrefix.Length, separator - nullablePrefix.Length);
    }

    private static SemanticFrame ParseFrame(string line)
    {
        var fields = line.Split('|');
        if (fields.Length != 4 || !fields[0].Equals(ObservationProtocol, StringComparison.Ordinal))
            throw new InvalidDataException("The Strict executable emitted an invalid semantic observation frame.");
        return new SemanticFrame(fields[1], DecodeFramePayload(fields[2]), DecodeFramePayload(fields[3]));
    }

    private static string DecodeFramePayload(string payload)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The Strict semantic observation contains invalid base64 payload.", exception);
        }
        string value;
        try
        {
            value = StrictUtf8.GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException exception)
        {
            throw new InvalidDataException("The Strict semantic observation contains invalid UTF-8 payload.", exception);
        }
        if (value.Length > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationTextCharacters)
            throw new InvalidDataException("The Strict semantic observation contains an oversized frame payload.");
        return value;
    }

    private static PowerShellCompilationSemanticOracleEnvelope CreateFramedEnvelope(
        string profileId,
        PowerShellCompilationSemanticValueObservation[] success,
        CultureInfo culture,
        int exitCode,
        PowerShellCompilationSemanticEncodingObservation encoding)
    {
        var envelope = new PowerShellCompilationSemanticOracleEnvelope
        {
            ProfileId = profileId,
            ExecutionSurface = PowerShellCompilationSemanticExecutionSurface.Strict.ToString(),
            OperatingSystem = GetOperatingSystem(),
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            Culture = culture.Name,
            Success = success,
            SuccessState = success.Length == 0 ? "NoOutput" : "Output",
            ExitCode = exitCode,
            Encoding = encoding,
            ProcessState = new PowerShellCompilationSemanticProcessStateObservation(),
            ProcessEffects = Array.Empty<PowerShellCompilationSemanticProcessEffectObservation>()
        };
        PowerShellCompilationSemanticOracleEnvelopeValidator.Validate(envelope, profileId);
        return envelope;
    }

    private static string GetOperatingSystem()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return "Windows";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)) return "Linux";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)) return "macOS";
        return "Unknown";
    }

    private static string[] SplitLines(string value)
    {
        if (string.IsNullOrEmpty(value))
            return Array.Empty<string>();
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        return lines.Length > 0 && lines[lines.Length - 1].Length == 0 ? lines.Take(lines.Length - 1).ToArray() : lines;
    }

    private sealed record SemanticFrame(string State, string TypeName, string Value);
}
