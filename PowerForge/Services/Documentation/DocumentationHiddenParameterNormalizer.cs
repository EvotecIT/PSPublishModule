using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Removes parameters that PowerShell explicitly marks as hidden from public help.
/// </summary>
internal static class DocumentationHiddenParameterNormalizer
{
    public static void Normalize(DocumentationCommandHelp command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var parameters = command.Parameters ?? new List<DocumentationParameterHelp>();
        var hiddenNames = parameters
            .Where(parameter => parameter is not null && parameter.DontShow)
            .Select(parameter => parameter.Name ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (hiddenNames.Length == 0) return;

        var hiddenRuntimeTypes = parameters
            .Where(parameter => parameter is not null && parameter.DontShow && AcceptsPipelineInput(parameter))
            .SelectMany(parameter => new[] { parameter.RuntimeTypeName, parameter.RuntimeClrTypeName })
            .Where(type => type.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var visibleRuntimeTypes = parameters
            .Where(parameter => parameter is not null && !parameter.DontShow && AcceptsPipelineInput(parameter))
            .SelectMany(parameter => new[] { parameter.RuntimeTypeName, parameter.RuntimeClrTypeName })
            .Where(type => type.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var hiddenDisplayTypes = parameters
            .Where(parameter => parameter is not null && parameter.DontShow && AcceptsPipelineInput(parameter))
            .Select(parameter => parameter.Type ?? string.Empty)
            .Where(type => type.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var visibleDisplayTypes = parameters
            .Where(parameter => parameter is not null && !parameter.DontShow && AcceptsPipelineInput(parameter))
            .Select(parameter => parameter.Type ?? string.Empty)
            .Where(type => type.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (hiddenRuntimeTypes.Count > 0 || hiddenDisplayTypes.Count > 0)
        {
            command.Inputs = ExpandCollectedInputAggregates(command.Inputs, command.RuntimeInputs);
            command.Inputs = FilterPipelineInputs(
                command.Inputs,
                hiddenRuntimeTypes,
                visibleRuntimeTypes,
                hiddenDisplayTypes,
                visibleDisplayTypes);
            command.RuntimeInputs = FilterPipelineInputs(
                command.RuntimeInputs,
                hiddenRuntimeTypes,
                visibleRuntimeTypes,
                hiddenDisplayTypes,
                visibleDisplayTypes);
        }

        command.Parameters = parameters
            .Where(parameter => parameter is not null && !parameter.DontShow)
            .ToList();

        foreach (var syntax in command.Syntax ?? new List<DocumentationSyntaxHelp>())
        {
            if (syntax is null || string.IsNullOrEmpty(syntax.Text)) continue;
            foreach (var name in hiddenNames)
                syntax.Text = StripSyntaxParameter(syntax.Text, name);
        }

        foreach (var name in hiddenNames)
        {
            command.Synopsis = StripSyntaxParameter(command.Synopsis, name);
            command.Description = StripSyntaxParameter(command.Description, name);
        }
    }

    private static bool AcceptsPipelineInput(DocumentationParameterHelp parameter)
        => !string.IsNullOrWhiteSpace(parameter.PipelineInput) &&
           !parameter.PipelineInput.StartsWith("False", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesRuntimeType(DocumentationTypeHelp input, ISet<string> parameterTypes)
        => parameterTypes.Contains(input.ClrTypeName ?? string.Empty) ||
           parameterTypes.Contains(input.CanonicalTypeName ?? string.Empty);

    private static bool MatchesDisplayType(DocumentationTypeHelp input, ISet<string> parameterTypes)
        => parameterTypes.Contains(input.Name ?? string.Empty);

    private static bool HasExactRuntimeIdentity(DocumentationTypeHelp input)
        => !string.IsNullOrEmpty(input.ClrTypeName) || !string.IsNullOrEmpty(input.CanonicalTypeName);

    private static List<DocumentationTypeHelp> FilterPipelineInputs(
        IEnumerable<DocumentationTypeHelp>? inputs,
        ISet<string> hiddenRuntimeTypes,
        ISet<string> visibleRuntimeTypes,
        ISet<string> hiddenDisplayTypes,
        ISet<string> visibleDisplayTypes)
        => (inputs ?? Array.Empty<DocumentationTypeHelp>())
            .Where(input => input is not null &&
                            (!MatchesRuntimeType(input, hiddenRuntimeTypes) ||
                             MatchesRuntimeType(input, visibleRuntimeTypes)) &&
                            (!MatchesDisplayType(input, hiddenDisplayTypes) ||
                             MatchesDisplayType(input, visibleDisplayTypes) ||
                             HasExactRuntimeIdentity(input)))
            .ToList();

    private static List<DocumentationTypeHelp> ExpandCollectedInputAggregates(
        IEnumerable<DocumentationTypeHelp>? inputs,
        IReadOnlyList<DocumentationTypeHelp>? runtimeInputs)
    {
        var runtime = runtimeInputs ?? Array.Empty<DocumentationTypeHelp>();
        var expanded = new List<DocumentationTypeHelp>();
        foreach (var input in inputs ?? Array.Empty<DocumentationTypeHelp>())
        {
            if (input is not null &&
                DocumentationInputNormalizer.TryParsePowerShellInputAggregate(input, runtime, out var parsed))
                expanded.AddRange(parsed);
            else if (input is not null)
                expanded.Add(input);
        }
        return expanded;
    }

    private static string StripSyntaxParameter(string? text, string name)
    {
        var value = text ?? string.Empty;
        var escaped = Regex.Escape(name);
        value = Regex.Replace(
            value,
            @"\s*\[\[-" + escaped + @"\](?:\s+<[^>\r\n]+>)?\]",
            string.Empty,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        value = Regex.Replace(
            value,
            @"\s*\[-" + escaped + @"\](?:\s+<[^>\r\n]+>)?",
            string.Empty,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        value = Regex.Replace(
            value,
            @"\s*\[-" + escaped + @"(?:\s+<[^>\r\n]+>)?\]",
            string.Empty,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return Regex.Replace(
            value,
            @"\s+-" + escaped + @"(?:\s+<[^>\r\n]+>)?(?=$|\s|[\]\[{}(),|:=])",
            string.Empty,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
