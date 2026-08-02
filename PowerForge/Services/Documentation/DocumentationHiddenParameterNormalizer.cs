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

        var hiddenPipelineTypes = parameters
            .Where(parameter => parameter is not null && parameter.DontShow && AcceptsPipelineInput(parameter))
            .Select(parameter => parameter.Type ?? string.Empty)
            .Where(type => type.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var visiblePipelineTypes = parameters
            .Where(parameter => parameter is not null && !parameter.DontShow && AcceptsPipelineInput(parameter))
            .Select(parameter => parameter.Type ?? string.Empty)
            .Where(type => type.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (hiddenPipelineTypes.Count > 0)
        {
            command.RuntimeInputs = (command.RuntimeInputs ?? new List<DocumentationTypeHelp>())
                .Where(input => input is not null &&
                                (!MatchesParameterType(input, hiddenPipelineTypes) ||
                                 MatchesParameterType(input, visiblePipelineTypes)))
                .ToList();
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

    private static bool MatchesParameterType(DocumentationTypeHelp input, ISet<string> parameterTypes)
        => parameterTypes.Contains(input.Name ?? string.Empty) ||
           parameterTypes.Contains(input.ClrTypeName ?? string.Empty);

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
