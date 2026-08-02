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

    private static string StripSyntaxParameter(string? text, string name)
    {
        var value = text ?? string.Empty;
        var escaped = Regex.Escape(name);
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
