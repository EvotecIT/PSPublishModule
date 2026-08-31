namespace PowerForge;

/// <summary>Validates schema-3 semantic observations before comparison or promotion.</summary>
public static class PowerShellCompilationSemanticOracleEnvelopeValidator
{
    /// <summary>Maximum values or records accepted in any one bounded observation collection.</summary>
    public const int MaximumObservationItems = 1024;

    /// <summary>Maximum characters accepted in one portable observation string.</summary>
    public const int MaximumObservationTextCharacters = 65536;

    /// <summary>Validates schema, profile identity, surface identity, boundedness, and value-shape invariants.</summary>
    public static void Validate(
        PowerShellCompilationSemanticOracleEnvelope envelope,
        string? expectedProfileId = null)
    {
        if (envelope is null) throw new ArgumentNullException(nameof(envelope));
        if (envelope.SchemaVersion != 3)
            throw new InvalidOperationException($"Semantic observations require envelope schema 3, not {envelope.SchemaVersion}.");
        var profile = PowerShellCompilationSemanticOracleCatalog.Get(envelope.ProfileId);
        var expectedProfile = expectedProfileId?.Trim() ?? string.Empty;
        if (expectedProfile.Length > 0 &&
            !profile.ProfileId.Equals(expectedProfile, StringComparison.Ordinal))
            throw new InvalidOperationException($"Semantic observation profile '{profile.ProfileId}' does not match expected profile '{expectedProfileId}'.");
        if (!Enum.TryParse<PowerShellCompilationSemanticExecutionSurface>(envelope.ExecutionSurface?.Trim(), true, out var surface) ||
            !Enum.IsDefined(typeof(PowerShellCompilationSemanticExecutionSurface), surface))
            throw new InvalidOperationException($"Unknown semantic execution surface '{envelope.ExecutionSurface}'.");

        RequireText(envelope.OperatingSystem, "OperatingSystem");
        RequireText(envelope.Architecture, "Architecture");
        RequireText(envelope.Culture, "Culture");
        if (!profile.OperatingSystem.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
            !profile.OperatingSystem.Equals(envelope.OperatingSystem, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Semantic observation operating system conflicts with its selected profile.");
        if (!profile.Architecture.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
            !profile.Architecture.Equals(envelope.Architecture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Semantic observation architecture conflicts with its selected profile.");
        if (!profile.Culture.Equals("invariant-per-case", StringComparison.OrdinalIgnoreCase) &&
            !profile.Culture.Equals(envelope.Culture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Semantic observation culture conflicts with its selected profile.");

        var runtimeFree = surface is PowerShellCompilationSemanticExecutionSurface.Strict or
            PowerShellCompilationSemanticExecutionSurface.HandWrittenClr;
        if (runtimeFree)
        {
            if (envelope.HostArtifact is not null || !string.IsNullOrWhiteSpace(envelope.HostVersion) ||
                !string.IsNullOrWhiteSpace(envelope.PowerShellEdition))
                throw new InvalidOperationException("Runtime-free semantic observations cannot carry PowerShell host identity.");
        }
        else
        {
            var host = PowerShellCompilationSemanticHostArtifactService.Normalize(envelope.HostArtifact
                ?? throw new InvalidOperationException("Host-backed semantic observations require an exact host artifact."));
            PowerShellCompilationSemanticHostArtifactService.EnsureMatchesProfile(host, profile);
            if (!host.OperatingSystem.Equals(envelope.OperatingSystem, StringComparison.OrdinalIgnoreCase) ||
                !host.Architecture.Equals(envelope.Architecture, StringComparison.OrdinalIgnoreCase) ||
                !host.Culture.Equals(envelope.Culture, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Semantic envelope identity differs from its exact host artifact.");
        }

        var success = RequireArray(envelope.Success, "Success");
        var expectedSuccessState = success.Length == 0 ? "NoOutput" : "Output";
        if (!expectedSuccessState.Equals(envelope.SuccessState, StringComparison.Ordinal))
            throw new InvalidOperationException($"SuccessState '{envelope.SuccessState}' contradicts success cardinality {success.Length}.");
        foreach (var value in success)
            ValidateValue(value ?? throw new InvalidOperationException("Success observations cannot contain null entries."));

        RequireStrings(envelope.Information, "Information");
        RequireStrings(envelope.Warnings, "Warnings");
        RequireStrings(envelope.Verbose, "Verbose");
        RequireStrings(envelope.Debug, "Debug");
        RequireStrings(envelope.Errors, "Errors");
        RequireStrings(envelope.FileSystemEffects, "FileSystemEffects");
        var streams = RequireArray(envelope.StreamRecords, "StreamRecords");
        var errors = RequireArray(envelope.ErrorRecords, "ErrorRecords");
        ValidateSequences(success.Select(static item => item.Sequence), "Success");
        ValidateSequences(streams.Select(static item => item?.Sequence ?? 0), "StreamRecords");
        ValidateSequences(errors.Select(static item => item?.Sequence ?? 0), "ErrorRecords");
        var allSequences = success.Select(static item => item.Sequence)
            .Concat(streams.Select(static item => item?.Sequence ?? 0))
            .Concat(errors.Select(static item => item?.Sequence ?? 0))
            .ToArray();
        if (allSequences.Distinct().Count() != allSequences.Length)
            throw new InvalidOperationException("Semantic stream sequence numbers must be globally unique.");
        foreach (var stream in streams)
        {
            if (stream is null) throw new InvalidOperationException("Stream observations cannot contain null entries.");
            RequireText(stream.Stream, "StreamRecords.Stream");
            RequireBounded(stream.Message, "StreamRecords.Message", allowEmpty: true);
            RequireBounded(stream.TypeName, "StreamRecords.TypeName", allowEmpty: true);
            RequireStrings(stream.Tags, "StreamRecords.Tags");
        }
        foreach (var error in errors)
        {
            if (error is null) throw new InvalidOperationException("Error observations cannot contain null entries.");
            RequireBounded(error.Message, "ErrorRecords.Message", allowEmpty: true);
            RequireBounded(error.FullyQualifiedErrorId, "ErrorRecords.FullyQualifiedErrorId", allowEmpty: true);
            RequireBounded(error.Category, "ErrorRecords.Category", allowEmpty: true);
            RequireBounded(error.ExceptionTypeName, "ErrorRecords.ExceptionTypeName", allowEmpty: true);
            RequireBounded(error.TargetTypeName, "ErrorRecords.TargetTypeName", allowEmpty: true);
        }

        if (envelope.Encoding is null) throw new InvalidOperationException("Semantic observations require encoding evidence.");
        RequireBounded(envelope.Encoding.ConsoleInput, "Encoding.ConsoleInput", allowEmpty: true);
        RequireBounded(envelope.Encoding.ConsoleOutput, "Encoding.ConsoleOutput", allowEmpty: true);
        RequireBounded(envelope.Encoding.PowerShellOutput, "Encoding.PowerShellOutput", allowEmpty: true);
        RequireText(envelope.Encoding.ObservationFile, "Encoding.ObservationFile");
        RequireBounded(envelope.Encoding.NativeArgumentPassing, "Encoding.NativeArgumentPassing", allowEmpty: true);
        if (envelope.ProcessState is null) throw new InvalidOperationException("Semantic observations require process-state evidence.");
        var processEffects = RequireArray(envelope.ProcessEffects, "ProcessEffects");
        if (processEffects.Length != 0)
            throw new InvalidOperationException("Schema-3 promotion does not accept process-effect entries until launches are directly observed and sequenced.");
    }

    private static void ValidateValue(PowerShellCompilationSemanticValueObservation value)
    {
        if (value.Sequence <= 0) throw new InvalidOperationException("Success observation sequences must be positive.");
        ValidateShape(value.ValueState, value.IsNull, value.IsAutomationNull, value.Value, value.TypeName,
            value.EnumerationState, value.CollectionCardinality, value.ElementTypeNames, "Success");
        var properties = RequireArray(value.Properties, "Success.Properties");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties)
        {
            if (property is null) throw new InvalidOperationException("Property observations cannot contain null entries.");
            RequireText(property.Name, "Success.Properties.Name");
            if (!names.Add(property.Name)) throw new InvalidOperationException($"Duplicate observed property '{property.Name}'.");
            ValidateShape(property.ValueState, property.IsNull, property.IsAutomationNull, property.Value, property.TypeName,
                property.EnumerationState, property.CollectionCardinality, property.ElementTypeNames, $"Property '{property.Name}'");
        }
    }

    private static void ValidateShape(
        string valueState,
        bool isNull,
        bool isAutomationNull,
        string value,
        string typeName,
        string enumerationState,
        int? cardinality,
        string[] elementTypes,
        string path)
    {
        if (valueState is not "Value" and not "Null" and not "AutomationNull")
            throw new InvalidOperationException($"{path} has unknown value state '{valueState}'.");
        if (enumerationState is not "Scalar" and not "Collection" and not "Dictionary")
            throw new InvalidOperationException($"{path} has unknown enumeration state '{enumerationState}'.");
        RequireBounded(value, path + ".Value", allowEmpty: true);
        RequireBounded(typeName, path + ".TypeName", allowEmpty: true);
        var types = RequireArray(elementTypes, path + ".ElementTypeNames");
        foreach (var elementType in types) RequireText(elementType, path + ".ElementTypeNames");
        if (types.Distinct(StringComparer.Ordinal).Count() != types.Length)
            throw new InvalidOperationException($"{path} element type identities must be distinct and ordered by first occurrence.");
        if (valueState == "Null")
        {
            if (!isNull || isAutomationNull || value.Length != 0 || typeName.Length != 0 ||
                enumerationState != "Scalar" || cardinality is not null || types.Length != 0)
                throw new InvalidOperationException($"{path} null flags or shape are contradictory.");
            return;
        }
        if (valueState == "AutomationNull")
        {
            if (isNull || !isAutomationNull || value.Length != 0 || enumerationState != "Scalar" ||
                cardinality is not null || types.Length != 0)
                throw new InvalidOperationException($"{path} AutomationNull flags or shape are contradictory.");
            return;
        }
        if (isNull || isAutomationNull || string.IsNullOrWhiteSpace(typeName))
            throw new InvalidOperationException($"{path} value flags or type identity are contradictory.");
        if (enumerationState == "Scalar")
        {
            if (cardinality is not null || types.Length != 0)
                throw new InvalidOperationException($"{path} scalar shape cannot carry collection evidence.");
        }
        else if (cardinality is < 0 or > MaximumObservationItems)
        {
            throw new InvalidOperationException($"{path} collection cardinality is outside the bounded schema-3 range.");
        }
        else if (cardinality is null && types.Length != 0 || cardinality == 0 && types.Length != 0 || cardinality > 0 && types.Length == 0)
        {
            throw new InvalidOperationException($"{path} collection cardinality contradicts its element-type evidence.");
        }
    }

    private static T[] RequireArray<T>(T[]? values, string path)
    {
        if (values is null) throw new InvalidOperationException($"{path} cannot be null.");
        if (values.Length > MaximumObservationItems)
            throw new InvalidOperationException($"{path} exceeds the {MaximumObservationItems}-item semantic observation limit.");
        return values;
    }

    private static void RequireStrings(string[]? values, string path)
    {
        foreach (var value in RequireArray(values, path))
            RequireBounded(value, path, allowEmpty: true);
    }

    private static void RequireText(string? value, string path) => RequireBounded(value, path, allowEmpty: false);

    private static void RequireBounded(string? value, string path, bool allowEmpty)
    {
        if (value is null || !allowEmpty && string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{path} requires a non-null{(allowEmpty ? string.Empty : ", non-empty")} value.");
        if (value.Length > MaximumObservationTextCharacters)
            throw new InvalidOperationException($"{path} exceeds the {MaximumObservationTextCharacters}-character semantic observation limit.");
    }

    private static void ValidateSequences(IEnumerable<int> values, string path)
    {
        var prior = 0;
        foreach (var value in values)
        {
            if (value <= prior) throw new InvalidOperationException($"{path} sequences must be positive and strictly increasing.");
            prior = value;
        }
    }
}
