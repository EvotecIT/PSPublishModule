using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

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
            NormalizeBindableIdentities(command);
            NormalizeParameterSetIdentities(command);
            RestoreTypeIdentityText(command.Inputs);
            RestoreTypeIdentityText(command.Outputs);
            RestoreTypeIdentityText(command.AuthoredOutputs);
            RestoreTypeIdentityText(command.RuntimeOutputs);
            NormalizeParameters(command);
            NormalizeOutputs(command);
        }

        NormalizeXmlBoundText(payload);
    }

    private static void NormalizeXmlBoundText(DocumentationExtractionPayload payload)
    {
        payload.ModuleName = Display(payload.ModuleName);
        payload.ModuleVersion = DisplayNullable(payload.ModuleVersion);
        payload.ModuleGuid = DisplayNullable(payload.ModuleGuid);
        payload.ModuleDescription = DisplayNullable(payload.ModuleDescription);
        payload.HelpInfoUri = DisplayNullable(payload.HelpInfoUri);
        payload.ProjectUri = DisplayNullable(payload.ProjectUri);

        foreach (var command in payload.Commands ?? new List<DocumentationCommandHelp>())
        {
            if (command is null) continue;
            command.Name = DocumentationIdentityTextFormatter.PreserveBindable(command.Name, "Command name");
            command.CommandType = Display(command.CommandType);
            command.Synopsis = Display(command.Synopsis);
            command.Description = Display(command.Description);

            foreach (var syntax in command.Syntax ?? new List<DocumentationSyntaxHelp>())
            {
                if (syntax is null) continue;
                syntax.Text = Display(syntax.Text);
            }

            foreach (var parameter in command.Parameters ?? new List<DocumentationParameterHelp>())
            {
                if (parameter is null) continue;
                parameter.Type = Display(parameter.Type);
                parameter.Description = Display(parameter.Description);
                parameter.PossibleValues = DisplayList(parameter.PossibleValues);
                parameter.Position = Display(parameter.Position);
                parameter.DefaultValue = Display(parameter.DefaultValue);
                parameter.PipelineInput = Display(parameter.PipelineInput);

            }

            foreach (var example in command.Examples ?? new List<DocumentationExampleHelp>())
            {
                if (example is null) continue;
                example.Title = Display(example.Title);
                example.Introduction = Display(example.Introduction);
                example.Code = Display(example.Code);
                example.Remarks = Display(example.Remarks);
            }

            NormalizeTypeText(command.Inputs, encodeLineBreaks: false);
            NormalizeTypeText(command.Outputs, encodeLineBreaks: true);

            foreach (var link in command.RelatedLinks ?? new List<DocumentationLinkHelp>())
            {
                if (link is null) continue;
                link.Text = Display(link.Text);
                link.Uri = Display(link.Uri);
            }

            foreach (var note in command.Notes ?? new List<DocumentationNoteHelp>())
            {
                if (note is null) continue;
                note.Title = Display(note.Title);
                note.Text = Display(note.Text);
            }
        }
    }

    private static void NormalizeTypeText(IEnumerable<DocumentationTypeHelp>? values, bool encodeLineBreaks)
    {
        foreach (var value in values ?? Array.Empty<DocumentationTypeHelp>())
        {
            if (value is null) continue;
            if (!value.IdentityTextNormalized)
            {
                value.Name = encodeLineBreaks
                    ? DocumentationIdentityTextFormatter.FormatOutputType(value.Name)
                    : DocumentationIdentityTextFormatter.Format(value.Name);
                value.ClrTypeName = encodeLineBreaks
                    ? DocumentationIdentityTextFormatter.FormatOutputType(value.ClrTypeName)
                    : DocumentationIdentityTextFormatter.Format(value.ClrTypeName);
                value.CanonicalTypeName = encodeLineBreaks
                    ? DocumentationIdentityTextFormatter.FormatOutputType(value.CanonicalTypeName)
                    : DocumentationIdentityTextFormatter.Format(value.CanonicalTypeName);
                value.IdentityTextNormalized = true;
            }
            value.RuntimeIdentity = string.Empty;
            value.Description = Display(value.Description);
        }
    }

    private static void RestoreTypeIdentityText(IEnumerable<DocumentationTypeHelp>? values)
    {
        foreach (var value in values ?? Array.Empty<DocumentationTypeHelp>())
        {
            if (value is null) continue;
            if (!string.IsNullOrEmpty(value.NameCodeUnits))
                value.Name = PowerShellDefaultValueFormatter.DecodeUtf16CodeUnits(value.NameCodeUnits);
            if (!string.IsNullOrEmpty(value.ClrTypeNameCodeUnits))
                value.ClrTypeName = PowerShellDefaultValueFormatter.DecodeUtf16CodeUnits(value.ClrTypeNameCodeUnits);
            if (!string.IsNullOrEmpty(value.CanonicalTypeNameCodeUnits))
                value.CanonicalTypeName = PowerShellDefaultValueFormatter.DecodeUtf16CodeUnits(value.CanonicalTypeNameCodeUnits);
            if (!string.IsNullOrEmpty(value.AssemblyQualifiedNameCodeUnits))
                value.AssemblyQualifiedName = PowerShellDefaultValueFormatter.DecodeUtf16CodeUnits(value.AssemblyQualifiedNameCodeUnits);
            value.LookupName = value.Name ?? string.Empty;
            value.LookupClrTypeName = value.ClrTypeName ?? string.Empty;
            value.NameCodeUnits = null;
            value.ClrTypeNameCodeUnits = null;
            value.CanonicalTypeNameCodeUnits = null;
            value.AssemblyQualifiedNameCodeUnits = null;
        }
    }

    private static List<string> DisplayList(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>()).Select(Display).ToList();

    private static void NormalizeBindableIdentities(DocumentationCommandHelp command)
    {
        command.Name = DocumentationIdentityTextFormatter.PreserveBindable(command.Name, "Command name");
        var parameters = command.Parameters ?? new List<DocumentationParameterHelp>();
        var reservedNames = new HashSet<string>(
            parameters.Where(parameter => parameter is not null &&
                                          DocumentationIdentityTextFormatter.IsXmlSafe(parameter.Name))
                .Select(parameter => parameter.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parameterIdentityMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            if (parameter is null) continue;
            var rawName = parameter.Name ?? string.Empty;
            if (DocumentationIdentityTextFormatter.IsXmlSafe(rawName))
            {
                parameter.Name = rawName;
                usedNames.Add(rawName);
            }
            else
            {
                parameter.Name = GetUniqueIdentityDisplay(
                    DocumentationIdentityTextFormatter.Format(rawName),
                    reservedNames,
                    usedNames);
            }

            parameterIdentityMap[rawName] = parameter.Name;

            parameter.Aliases = NormalizeAliases(parameter.Aliases);
        }

        NormalizeSyntaxParameterIdentities(command.Syntax, parameterIdentityMap);
    }

    private static void NormalizeSyntaxParameterIdentities(
        IEnumerable<DocumentationSyntaxHelp>? syntaxItems,
        IReadOnlyDictionary<string, string> parameterIdentityMap)
    {
        var rawNames = parameterIdentityMap.Keys
            .Where(name => name.Length > 0)
            .OrderByDescending(name => name.Length)
            .Select(Regex.Escape)
            .ToArray();
        if (rawNames.Length == 0) return;

        var matcher = new Regex(
            @"(?<prefix>^|[\s\[])-(?<name>" + string.Join("|", rawNames) + @")(?=$|[\s\]\[<>{}(),|:=])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);
        foreach (var syntax in syntaxItems ?? Array.Empty<DocumentationSyntaxHelp>())
        {
            if (syntax is null || string.IsNullOrEmpty(syntax.Text)) continue;
            syntax.Text = matcher.Replace(syntax.Text, match =>
                match.Groups["prefix"].Value + "-" + parameterIdentityMap[match.Groups["name"].Value]);
        }
    }

    private static void NormalizeParameterSetIdentities(DocumentationCommandHelp command)
    {
        var rawNames = new List<string>();
        if (command.DefaultParameterSet is not null) rawNames.Add(command.DefaultParameterSet);
        foreach (var syntax in command.Syntax ?? new List<DocumentationSyntaxHelp>())
            if (syntax is not null) rawNames.Add(syntax.Name ?? string.Empty);
        foreach (var parameter in command.Parameters ?? new List<DocumentationParameterHelp>())
        {
            if (parameter is null) continue;
            rawNames.AddRange(parameter.ParameterSets ?? new List<string>());
            rawNames.AddRange((parameter.ParameterSetRequired ?? new Dictionary<string, bool>()).Keys);
        }

        var reserved = new HashSet<string>(
            rawNames.Where(DocumentationIdentityTextFormatter.IsXmlSafe),
            StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawName in rawNames)
        {
            if (map.ContainsKey(rawName)) continue;
            var display = DocumentationIdentityTextFormatter.IsXmlSafe(rawName)
                ? rawName
                : GetUniqueIdentityDisplay(
                    DocumentationIdentityTextFormatter.Format(rawName), reserved, used);
            if (DocumentationIdentityTextFormatter.IsXmlSafe(rawName)) used.Add(display);
            map.Add(rawName, display);
        }

        string Map(string? value)
        {
            var raw = value ?? string.Empty;
            return map.TryGetValue(raw, out var display) ? display : raw;
        }

        if (command.DefaultParameterSet is not null)
            command.DefaultParameterSet = Map(command.DefaultParameterSet);
        foreach (var syntax in command.Syntax ?? new List<DocumentationSyntaxHelp>())
            if (syntax is not null) syntax.Name = Map(syntax.Name);
        foreach (var parameter in command.Parameters ?? new List<DocumentationParameterHelp>())
        {
            if (parameter is null) continue;
            parameter.ParameterSets = (parameter.ParameterSets ?? new List<string>()).Select(Map).ToList();
            var requiredBySet = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in parameter.ParameterSetRequired ?? new Dictionary<string, bool>())
            {
                var name = Map(entry.Key);
                requiredBySet[name] = entry.Value ||
                    (requiredBySet.TryGetValue(name, out var required) && required);
            }
            parameter.ParameterSetRequired = requiredBySet;
        }
    }

    private static List<string> NormalizeAliases(IEnumerable<string>? values)
    {
        var rawAliases = (values ?? Array.Empty<string>())
            .Where(alias => !string.IsNullOrEmpty(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var reserved = new HashSet<string>(
            rawAliases.Where(DocumentationIdentityTextFormatter.IsXmlSafe),
            StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(rawAliases.Count);
        foreach (var alias in rawAliases)
        {
            var display = alias;
            if (!DocumentationIdentityTextFormatter.IsXmlSafe(alias))
            {
                display = PowerShellDefaultValueFormatter.FormatDisplayText(alias);
                if (reserved.Contains(display) || used.Contains(display))
                {
                    display = GetUniqueIdentityDisplay(
                        PowerShellDefaultValueFormatter.FormatString(alias, preserveCharacterType: false),
                        reserved,
                        used);
                    result.Add(display);
                    continue;
                }
            }

            if (used.Add(display)) result.Add(display);
        }
        return result;
    }

    private static string GetUniqueIdentityDisplay(
        string baseDisplay,
        HashSet<string> reserved,
        HashSet<string> used)
    {
        var display = baseDisplay;
        var suffix = 1;
        while (reserved.Contains(display) || !used.Add(display))
        {
            display = baseDisplay + " [encoded " +
                      suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            suffix++;
        }
        return display;
    }

    private static string Display(string? value)
        => PowerShellDefaultValueFormatter.FormatDisplayText(value ?? string.Empty);

    private static string? DisplayNullable(string? value)
        => value is null ? null : Display(value);

    private static void NormalizeParameters(DocumentationCommandHelp command)
    {
        foreach (var parameter in command.Parameters ?? new List<DocumentationParameterHelp>())
        {
            if (parameter is null) continue;

            parameter.Aliases = (parameter.Aliases ?? new List<string>())
                .Where(alias => !string.IsNullOrEmpty(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!parameter.PossibleValuesNormalized)
            {
                parameter.PossibleValues = MergePossibleValues(
                    parameter.PossibleValues,
                    parameter.EnumPossibleValues,
                    parameter.HasValidateSet,
                    parameter.ValidateSetCaseSensitive);
                parameter.EnumPossibleValues = new List<string>();
                parameter.HasValidateSet = false;
                parameter.ValidateSetCaseSensitive = false;
                parameter.PossibleValuesNormalized = true;
            }

            if (parameter.HasMetadataDefault)
            {
                var help = !string.IsNullOrEmpty(parameter.MetadataDefaultHelpCodeUnits)
                    ? PowerShellDefaultValueFormatter.DecodeUtf16CodeUnits(parameter.MetadataDefaultHelpCodeUnits)
                    : parameter.MetadataDefaultHelp;
                parameter.DefaultValue = !string.IsNullOrWhiteSpace(help)
                    ? PowerShellDefaultValueFormatter.FormatDisplayText(help!)
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
        var runtimeOutputs = DeduplicateRuntimeOutputs(command.RuntimeOutputs ?? new List<DocumentationTypeHelp>());
        if (authoredOutputs.Count == 0 && runtimeOutputs.Count == 0)
        {
            command.Outputs ??= new List<DocumentationTypeHelp>();
            return;
        }

        var authoredIndex = BuildIndex(authoredOutputs);
        var runtimeIndex = BuildIndex(runtimeOutputs);
        var outputs = new List<DocumentationTypeHelp>();
        var runtimeIdentityCounts = runtimeOutputs
            .Select(GetIdentity)
            .Where(identity => identity.Length > 0)
            .GroupBy(identity => identity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var seenIdentities = new HashSet<string>(StringComparer.Ordinal);
        var seenRuntimeIdentities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var runtimeOutput in runtimeOutputs)
        {
            var identity = GetIdentity(runtimeOutput);
            var runtimeIdentity = GetRuntimeIdentity(runtimeOutput);
            if (identity.Length == 0 || runtimeIdentity.Length == 0 || !seenRuntimeIdentities.Add(runtimeIdentity))
                continue;
            seenIdentities.Add(identity);

            var description = string.Empty;
            var displayName = runtimeOutput.Name ?? string.Empty;
            var displayClrTypeName = runtimeOutput.ClrTypeName ?? string.Empty;
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
                if (!string.IsNullOrWhiteSpace(authoredOutput.Name))
                    displayName = authoredOutput.Name;
                matched = true;
                break;
            }

            if (!matched && TryGetUniqueCaseInsensitiveMatch(
                    GetKeys(runtimeOutput), authoredIndex, runtimeIndex, out var foldedAuthoredOutput) &&
                !HasConflictingQualifiedIdentity(identity, GetIdentity(foldedAuthoredOutput)))
            {
                description = foldedAuthoredOutput.Description ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(foldedAuthoredOutput.Name))
                    displayName = foldedAuthoredOutput.Name;
            }

            if (runtimeIdentityCounts.TryGetValue(identity, out var identityCount) &&
                identityCount > 1 &&
                !string.IsNullOrWhiteSpace(runtimeOutput.AssemblyQualifiedName))
            {
                displayName = runtimeOutput.AssemblyQualifiedName!;
                displayClrTypeName = runtimeOutput.AssemblyQualifiedName!;
            }

            outputs.Add(Copy(runtimeOutput, description, displayName, displayClrTypeName));
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

    private static List<DocumentationTypeHelp> DeduplicateRuntimeOutputs(IEnumerable<DocumentationTypeHelp> values)
    {
        var result = new List<DocumentationTypeHelp>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null) continue;
            var identity = GetRuntimeIdentity(value);
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
        if (!string.IsNullOrWhiteSpace(value.CanonicalTypeName))
        {
            AddCandidate(value.CanonicalTypeName, preserveWhitespace: true);
        }
        else
        {
            AddCandidate(value.Name, preserveWhitespace: false);
            AddCandidate(value.ClrTypeName, preserveWhitespace: false);
        }
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

    private static string GetRuntimeIdentity(DocumentationTypeHelp value)
        => !string.IsNullOrWhiteSpace(value.RuntimeIdentity)
            ? value.RuntimeIdentity
            : GetIdentity(value);

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

    private static List<string> MergePossibleValues(
        IEnumerable<string>? metadataValues,
        IEnumerable<string>? enumValues,
        bool hasValidateSet,
        bool validateSetCaseSensitive)
    {
        var metadataComparer = validateSetCaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        if (hasValidateSet)
        {
            var authoredValues = DistinctPreserved(metadataValues, metadataComparer);
            var displays = authoredValues
                .Select(PowerShellDefaultValueFormatter.FormatDisplayText)
                .ToList();
            var displayCounts = new Dictionary<string, int>(metadataComparer);
            foreach (var display in displays)
                displayCounts[display] = displayCounts.TryGetValue(display, out var count) ? count + 1 : 1;
            var candidates = authoredValues.Select((value, index) =>
            {
                var needsFallback = displayCounts[displays[index]] > 1;
                var display = needsFallback
                    ? PowerShellDefaultValueFormatter.FormatString(value, preserveCharacterType: false)
                    : displays[index];
                return new KeyValuePair<string, bool>(display, needsFallback);
            }).ToList();
            var reservedDisplays = new HashSet<string>(
                candidates.Where(candidate => !candidate.Value).Select(candidate => candidate.Key),
                metadataComparer);
            var usedDisplays = new HashSet<string>(metadataComparer);
            var authoredResult = new List<string>(candidates.Count);
            foreach (var candidate in candidates)
            {
                var display = candidate.Key;
                if (candidate.Value)
                {
                    var baseDisplay = display;
                    var suffix = 1;
                    while (reservedDisplays.Contains(display) || !usedDisplays.Add(display))
                    {
                        display = baseDisplay + " [encoded " +
                                  suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                        suffix++;
                    }
                }
                else
                {
                    usedDisplays.Add(display);
                }
                authoredResult.Add(display);
            }
            return authoredResult;
        }

        var result = DistinctNonBlank(metadataValues, metadataComparer)
            .Where(IsXmlSafePossibleValue)
            .ToList();

        var seen = new HashSet<string>(result, StringComparer.Ordinal);
        foreach (var value in DistinctNonBlank(enumValues, StringComparer.Ordinal)
                     .Where(IsXmlSafePossibleValue))
        {
            if (seen.Add(value)) result.Add(value);
        }
        return result;
    }

    private static bool IsXmlSafePossibleValue(string value)
    {
        try
        {
            XmlConvert.VerifyXmlChars(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static List<string> DistinctNonBlank(
        IEnumerable<string>? values,
        IEqualityComparer<string> comparer)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(comparer);
        foreach (var value in values ?? Array.Empty<string>())
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) continue;
            result.Add(normalized);
        }
        return result;
    }

    private static List<string> DistinctPreserved(
        IEnumerable<string>? values,
        IEqualityComparer<string> comparer)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(comparer);
        foreach (var value in values ?? Array.Empty<string>())
        {
            var exact = value ?? string.Empty;
            if (seen.Add(exact)) result.Add(exact);
        }
        return result;
    }

    private static DocumentationTypeHelp Copy(
        DocumentationTypeHelp source,
        string description,
        string? name = null,
        string? clrTypeName = null)
        => new()
        {
            Name = name ?? source.Name ?? string.Empty,
            LookupName = source.LookupName ?? string.Empty,
            ClrTypeName = clrTypeName ?? source.ClrTypeName ?? string.Empty,
            LookupClrTypeName = source.LookupClrTypeName ?? string.Empty,
            CanonicalTypeName = source.CanonicalTypeName ?? string.Empty,
            RuntimeIdentity = string.Empty,
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
