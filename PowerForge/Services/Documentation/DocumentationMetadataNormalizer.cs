using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Converts the raw PowerShell-host snapshot into the stable documentation model
/// consumed by the Markdown and MAML writers.
/// </summary>
internal static class DocumentationMetadataNormalizer
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes parameter defaults and reconciles runtime and authored output metadata.
    /// </summary>
    public static void Normalize(DocumentationExtractionPayload payload)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));

        foreach (var command in payload.Commands ?? new List<DocumentationCommandHelp>())
        {
            if (command is null) continue;
            NormalizeParameters(command);
            NormalizeOutputs(command);
        }
    }

    private static void NormalizeParameters(DocumentationCommandHelp command)
    {
        foreach (var parameter in command.Parameters ?? new List<DocumentationParameterHelp>())
        {
            if (parameter is null) continue;

            parameter.Aliases = DistinctNonBlank(parameter.Aliases);
            parameter.PossibleValues = DistinctNonBlank(parameter.PossibleValues);

            if (parameter.HasMetadataDefault)
            {
                var help = !string.IsNullOrEmpty(parameter.MetadataDefaultHelpCodeUnits)
                    ? PowerShellDefaultValueFormatter.DecodeUtf16CodeUnits(parameter.MetadataDefaultHelpCodeUnits)
                    : parameter.MetadataDefaultHelp;
                parameter.DefaultValue = !string.IsNullOrWhiteSpace(help)
                    ? PowerShellDefaultValueFormatter.NeedsEncoding(help)
                        ? PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
                        {
                            Kind = "String",
                            Text = help
                        })
                        : help!
                    : PowerShellDefaultValueFormatter.Format(parameter.MetadataDefaultValue);
            }

            parameter.MetadataDefaultHelp = null;
            parameter.MetadataDefaultHelpCodeUnits = null;
            parameter.MetadataDefaultValue = null;
            parameter.HasMetadataDefault = false;
        }
    }

    private static void NormalizeOutputs(DocumentationCommandHelp command)
    {
        var authoredOutputs = command.AuthoredOutputs ?? new List<DocumentationTypeHelp>();
        var runtimeOutputs = DeduplicateByIdentity(command.RuntimeOutputs ?? new List<DocumentationTypeHelp>());
        if (authoredOutputs.Count == 0 && runtimeOutputs.Count == 0)
        {
            command.Outputs ??= new List<DocumentationTypeHelp>();
            return;
        }

        var authoredIndex = BuildIndex(authoredOutputs);
        var runtimeIndex = BuildIndex(runtimeOutputs);
        var outputs = new List<DocumentationTypeHelp>();
        var seenIdentities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var runtimeOutput in runtimeOutputs)
        {
            var identity = GetIdentity(runtimeOutput);
            if (identity.Length == 0 || !seenIdentities.Add(identity)) continue;

            var description = string.Empty;
            var matched = false;
            foreach (var key in GetKeys(runtimeOutput))
            {
                if (!authoredIndex.ByKey.TryGetValue(key, out var authoredOutput) ||
                    authoredIndex.Counts[key] != 1 ||
                    runtimeIndex.Counts[key] != 1)
                    continue;

                if (HasConflictingQualifiedIdentity(identity, GetIdentity(authoredOutput)))
                    continue;

                description = authoredOutput.Description ?? string.Empty;
                matched = true;
                break;
            }

            if (!matched && TryGetUniqueCaseInsensitiveMatch(
                    GetKeys(runtimeOutput), authoredIndex, runtimeIndex, out var foldedAuthoredOutput) &&
                !HasConflictingQualifiedIdentity(identity, GetIdentity(foldedAuthoredOutput)))
            {
                description = foldedAuthoredOutput.Description ?? string.Empty;
            }

            outputs.Add(Copy(runtimeOutput, description));
        }

        var allowHelpOnlyOutputs =
            !string.Equals(command.CommandType, "Cmdlet", StringComparison.OrdinalIgnoreCase) ||
            runtimeOutputs.Count == 0;

        if (allowHelpOnlyOutputs)
        {
            foreach (var authoredOutput in authoredOutputs)
            {
                var identity = GetIdentity(authoredOutput);
                if (identity.Length == 0) continue;

                if (string.Equals(command.CommandType, "Cmdlet", StringComparison.OrdinalIgnoreCase) &&
                    runtimeOutputs.Count == 0 &&
                    string.Equals(identity, "System.Object", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(authoredOutput.Description))
                    continue;

                var matchesRuntime = false;
                foreach (var key in GetKeys(authoredOutput))
                {
                    if (!runtimeIndex.ByKey.TryGetValue(key, out var runtimeOutput) ||
                        authoredIndex.Counts[key] != 1 ||
                        runtimeIndex.Counts[key] != 1)
                        continue;

                    if (HasConflictingQualifiedIdentity(GetIdentity(runtimeOutput), identity))
                        continue;

                    matchesRuntime = true;
                    break;
                }

                if (!matchesRuntime && TryGetUniqueCaseInsensitiveMatch(
                        GetKeys(authoredOutput), runtimeIndex, authoredIndex, out var foldedRuntimeOutput) &&
                    !HasConflictingQualifiedIdentity(GetIdentity(foldedRuntimeOutput), identity))
                {
                    matchesRuntime = true;
                }

                if (matchesRuntime || !seenIdentities.Add(identity)) continue;
                outputs.Add(Copy(authoredOutput, authoredOutput.Description ?? string.Empty));
            }
        }

        command.Outputs = outputs;
        command.AuthoredOutputs = new List<DocumentationTypeHelp>();
        command.RuntimeOutputs = new List<DocumentationTypeHelp>();
    }

    private static List<DocumentationTypeHelp> DeduplicateByIdentity(IEnumerable<DocumentationTypeHelp> values)
    {
        var result = new List<DocumentationTypeHelp>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null) continue;
            var identity = GetIdentity(value);
            if (identity.Length == 0 || !seen.Add(identity)) continue;
            result.Add(value);
        }
        return result;
    }

    private static TypeIndex BuildIndex(IEnumerable<DocumentationTypeHelp> values)
    {
        var index = new TypeIndex();
        foreach (var value in values)
        {
            if (value is null) continue;
            var foldedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in GetKeys(value))
            {
                if (!index.Counts.TryGetValue(key, out var count))
                {
                    index.Counts[key] = 1;
                    index.ByKey[key] = value;
                }
                else
                {
                    index.Counts[key] = count + 1;
                }

                if (!foldedKeys.Add(key)) continue;
                if (!index.FoldedCounts.TryGetValue(key, out var foldedCount))
                {
                    index.FoldedCounts[key] = 1;
                    index.ByFoldedKey[key] = value;
                }
                else
                {
                    index.FoldedCounts[key] = foldedCount + 1;
                }
            }
        }
        return index;
    }

    private static bool TryGetUniqueCaseInsensitiveMatch(
        IEnumerable<string> keys,
        TypeIndex candidates,
        TypeIndex sources,
        out DocumentationTypeHelp match)
    {
        foreach (var key in keys)
        {
            if (IsQualified(key) ||
                !candidates.ByFoldedKey.TryGetValue(key, out var candidate) ||
                candidates.FoldedCounts[key] != 1 ||
                sources.FoldedCounts[key] != 1)
                continue;

            match = candidate;
            return true;
        }

        match = null!;
        return false;
    }

    private static List<string> GetKeys(DocumentationTypeHelp value)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AddCandidate(value.Name, preserveWhitespace: false);
        AddCandidate(value.ClrTypeName, preserveWhitespace: false);
        AddCandidate(value.CanonicalTypeName, preserveWhitespace: true);
        return keys.ToList();

        void AddCandidate(string? candidate, bool preserveWhitespace)
        {
            var canonical = preserveWhitespace && !string.IsNullOrWhiteSpace(candidate)
                ? candidate!
                : Canonicalize(candidate);
            if (canonical.Length == 0) return;
            keys.Add(canonical);

            var genericIndex = canonical.IndexOf('[');
            var baseName = genericIndex >= 0 ? canonical.Substring(0, genericIndex) : canonical;
            var genericSuffix = genericIndex >= 0 ? canonical.Substring(genericIndex) : string.Empty;
            var separatorIndex = Math.Max(baseName.LastIndexOf('.'), baseName.LastIndexOf('+'));
            if (separatorIndex >= 0 && separatorIndex < baseName.Length - 1)
                keys.Add(baseName.Substring(separatorIndex + 1) + genericSuffix);
        }
    }

    private static string GetIdentity(DocumentationTypeHelp value)
    {
        if (!string.IsNullOrWhiteSpace(value.CanonicalTypeName))
            return value.CanonicalTypeName!;
        foreach (var candidate in new[] { value.ClrTypeName, value.Name })
        {
            var identity = Canonicalize(candidate);
            if (identity.Length > 0) return identity;
        }
        return string.Empty;
    }

    private static string Canonicalize(string? candidate)
        => string.IsNullOrWhiteSpace(candidate)
            ? string.Empty
            : Whitespace.Replace(candidate!.Trim(), string.Empty);

    private static bool HasConflictingQualifiedIdentity(string left, string right)
        => !left.Equals(right, StringComparison.Ordinal) &&
           IsQualified(left) &&
           IsQualified(right);

    private static bool IsQualified(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity)) return false;
        var genericIndex = identity.IndexOf('[');
        var baseName = genericIndex >= 0 ? identity.Substring(0, genericIndex) : identity;
        return baseName.IndexOf('.') >= 0 || baseName.IndexOf('+') >= 0;
    }

    private static List<string> DistinctNonBlank(IEnumerable<string>? values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? Array.Empty<string>())
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) continue;
            result.Add(normalized);
        }
        return result;
    }

    private static DocumentationTypeHelp Copy(DocumentationTypeHelp source, string description)
        => new()
        {
            Name = source.Name ?? string.Empty,
            ClrTypeName = source.ClrTypeName ?? string.Empty,
            CanonicalTypeName = source.CanonicalTypeName ?? string.Empty,
            Description = description
        };

    private sealed class TypeIndex
    {
        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DocumentationTypeHelp> ByKey { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> FoldedCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DocumentationTypeHelp> ByFoldedKey { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
