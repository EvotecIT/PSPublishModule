using System.Collections;
using System.Globalization;
using System.Reflection;

namespace PowerForge;

/// <summary>Creates schema-3 semantic observations directly from runtime-free CLR values.</summary>
public static class PowerShellCompilationSemanticRuntimeFreeObserver
{
    /// <summary>Observes one Strict or hand-written CLR result without loading or starting PowerShell.</summary>
    public static PowerShellCompilationSemanticOracleEnvelope Observe(
        string profileId,
        PowerShellCompilationSemanticExecutionSurface executionSurface,
        object? value,
        IEnumerable<string>? observedPropertyNames = null,
        CultureInfo? culture = null,
        int? exitCode = null,
        IEnumerable<string>? fileSystemEffects = null,
        PowerShellCompilationSemanticEncodingObservation? encoding = null,
        CancellationToken cancellationToken = default)
    {
        _ = PowerShellCompilationSemanticOracleCatalog.Get(profileId);
        if (executionSurface is not PowerShellCompilationSemanticExecutionSurface.Strict and
            not PowerShellCompilationSemanticExecutionSurface.HandWrittenClr)
            throw new ArgumentOutOfRangeException(nameof(executionSurface), "A runtime-free observation must use the Strict or HandWrittenClr surface.");

        culture ??= CultureInfo.GetCultureInfo("en-US");
        var propertyNames = NormalizePropertyNames(observedPropertyNames, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var success = FlattenPipeline(value, cancellationToken)
            .Select((item, index) => ObserveValue(item, index + 1, propertyNames, culture))
            .ToArray();
        var envelope = new PowerShellCompilationSemanticOracleEnvelope
        {
            ProfileId = profileId,
            ExecutionSurface = executionSurface.ToString(),
            OperatingSystem = GetOperatingSystem(),
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            Culture = culture.Name,
            Success = success,
            SuccessState = success.Length == 0 ? "NoOutput" : "Output",
            ExitCode = exitCode,
            FileSystemEffects = BoundedStrings(fileSystemEffects, nameof(fileSystemEffects)),
            Encoding = encoding ?? CreateEncoding(),
            ProcessState = new PowerShellCompilationSemanticProcessStateObservation(),
            ProcessEffects = Array.Empty<PowerShellCompilationSemanticProcessEffectObservation>()
        };
        PowerShellCompilationSemanticOracleEnvelopeValidator.Validate(envelope, profileId);
        return envelope;
    }

    private static IEnumerable<object> FlattenPipeline(object? value, CancellationToken cancellationToken)
    {
        if (value is null)
            yield break;
        if (value is string or IDictionary || value is not IEnumerable enumerable)
        {
            yield return value;
            yield break;
        }

        var count = 0;
        foreach (var item in enumerable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            if (count > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems)
                throw new InvalidOperationException($"Runtime-free success output exceeds the {PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems}-item observation limit.");
            if (item is not null)
                yield return item;
        }
    }

    private static PowerShellCompilationSemanticValueObservation ObserveValue(
        object value,
        int sequence,
        ISet<string> propertyNames,
        CultureInfo culture)
    {
        var shape = InspectShape(value);
        return new PowerShellCompilationSemanticValueObservation
        {
            Sequence = sequence,
            Value = Format(value, culture),
            TypeName = value.GetType().FullName ?? value.GetType().Name,
            ValueState = "Value",
            EnumerationState = shape.State,
            CollectionCardinality = shape.Cardinality,
            ElementTypeNames = shape.ElementTypes,
            Properties = ObserveProperties(value, propertyNames, culture)
        };
    }

    private static PowerShellCompilationSemanticPropertyObservation[] ObserveProperties(
        object value,
        ISet<string> propertyNames,
        CultureInfo culture)
    {
        if (propertyNames.Count == 0)
            return Array.Empty<PowerShellCompilationSemanticPropertyObservation>();

        var observations = new List<PowerShellCompilationSemanticPropertyObservation>();
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var name = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                if (propertyNames.Contains(name))
                {
                    if (observations.Count >= PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems)
                        throw new InvalidOperationException("Observed property count exceeds the semantic observation limit.");
                    observations.Add(ObserveProperty(name, entry.Value, culture));
                }
            }
            return observations.ToArray();
        }

        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0 || !propertyNames.Contains(property.Name))
                continue;
            if (observations.Count >= PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems)
                throw new InvalidOperationException("Observed property count exceeds the semantic observation limit.");
            observations.Add(ObserveProperty(property.Name, property.GetValue(value), culture));
        }
        return observations.ToArray();
    }

    private static PowerShellCompilationSemanticPropertyObservation ObserveProperty(string name, object? value, CultureInfo culture)
    {
        if (value is null)
        {
            return new PowerShellCompilationSemanticPropertyObservation
            {
                Name = name,
                IsNull = true,
                ValueState = "Null",
                EnumerationState = "Scalar"
            };
        }

        var shape = InspectShape(value);
        return new PowerShellCompilationSemanticPropertyObservation
        {
            Name = name,
            Value = Format(value, culture),
            TypeName = value.GetType().FullName ?? value.GetType().Name,
            ValueState = "Value",
            EnumerationState = shape.State,
            CollectionCardinality = shape.Cardinality,
            ElementTypeNames = shape.ElementTypes
        };
    }

    private static ValueShape InspectShape(object value)
    {
        if (value is string)
            return new ValueShape("Scalar", null, Array.Empty<string>());
        if (value is IDictionary dictionary)
        {
            EnsureBoundedCount(dictionary.Count);
            return new ValueShape("Dictionary", dictionary.Count, GetElementTypes(dictionary.Values));
        }
        if (value is ICollection collection)
        {
            EnsureBoundedCount(collection.Count);
            return new ValueShape("Collection", collection.Count, GetElementTypes(collection));
        }
        if (value is IEnumerable)
            return new ValueShape("Collection", null, Array.Empty<string>());
        return new ValueShape("Scalar", null, Array.Empty<string>());
    }

    private static string[] GetElementTypes(IEnumerable values)
    {
        var types = new List<string>();
        var count = 0;
        foreach (var item in values)
        {
            count++;
            EnsureBoundedCount(count);
            var type = item?.GetType().FullName ?? "Null";
            if (!types.Contains(type, StringComparer.Ordinal))
                types.Add(type);
        }
        return types.ToArray();
    }

    private static string Format(object value, CultureInfo culture)
    {
        var result = value is IFormattable formattable
            ? formattable.ToString(null, culture) ?? string.Empty
            : Convert.ToString(value, culture) ?? string.Empty;
        if (result.Length > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationTextCharacters)
            throw new InvalidOperationException($"Runtime-free value text exceeds the {PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationTextCharacters}-character observation limit.");
        return result;
    }

    private static ISet<string> NormalizePropertyNames(IEnumerable<string>? names, CancellationToken cancellationToken)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceCount = 0;
        foreach (var sourceName in names ?? Array.Empty<string>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourceCount++;
            if (sourceCount > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems)
                throw new ArgumentException("Observed property names exceed the semantic observation limit.", nameof(names));
            var name = sourceName?.Trim() ?? string.Empty;
            if (name.Length == 0) continue;
            if (name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SessionId", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Portable semantic observations forbid sensitive or live-runtime property '{name}'.", nameof(names));
            normalized.Add(name);
        }
        return normalized;
    }

    private static PowerShellCompilationSemanticEncodingObservation CreateEncoding()
        => new()
        {
            ConsoleInput = TryGetEncoding(static () => Console.InputEncoding),
            ConsoleOutput = TryGetEncoding(static () => Console.OutputEncoding),
            ObservationFile = "utf-8"
        };

    private static string TryGetEncoding(Func<System.Text.Encoding> factory)
    {
        try { return factory().WebName; }
        catch (IOException) { return string.Empty; }
        catch (PlatformNotSupportedException) { return string.Empty; }
    }

    private static string GetOperatingSystem()
        => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "Windows"
            : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) ? "Linux"
            : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) ? "macOS"
            : "Unknown";

    private static string[] BoundedStrings(IEnumerable<string>? values, string parameterName)
    {
        var result = new List<string>();
        foreach (var value in values ?? Array.Empty<string>())
        {
            if (result.Count >= PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems)
                throw new ArgumentException($"Semantic observation exceeds the {PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems}-item limit.", parameterName);
            if (value is null || value.Length > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationTextCharacters)
                throw new ArgumentException("Semantic observation contains a null or oversized string.", parameterName);
            result.Add(value);
        }
        return result.ToArray();
    }

    private static void EnsureBoundedCount(int count)
    {
        if (count > PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems)
            throw new InvalidOperationException($"Runtime-free collection exceeds the {PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems}-item observation limit.");
    }

    private sealed class ValueShape
    {
        internal ValueShape(string state, int? cardinality, string[] elementTypes)
        {
            State = state;
            Cardinality = cardinality;
            ElementTypes = elementTypes;
        }

        internal string State { get; }
        internal int? Cardinality { get; }
        internal string[] ElementTypes { get; }
    }
}
