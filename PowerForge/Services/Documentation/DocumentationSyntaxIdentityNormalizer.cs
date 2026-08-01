using System;
using System.Linq;

namespace PowerForge;

internal static class DocumentationSyntaxIdentityNormalizer
{
    public static void Normalize(DocumentationExtractionPayload payload)
    {
        foreach (var command in payload.Commands)
        {
            var mappings = command.Parameters
                .Where(parameter => !string.IsNullOrEmpty(parameter.OriginalName) &&
                                    !string.Equals(parameter.OriginalName, parameter.Name, StringComparison.Ordinal))
                .OrderByDescending(parameter => parameter.OriginalName.Length)
                .ToArray();
            if (mappings.Length == 0) continue;

            foreach (var syntax in command.Syntax)
            {
                var text = syntax.Text ?? string.Empty;
                foreach (var parameter in mappings)
                {
                    text = ReplaceParameterToken(text, parameter.OriginalName, parameter.Name);
                }
                syntax.Text = text;
            }
        }
    }

    private static string ReplaceParameterToken(string text, string originalName, string displayName)
    {
        var originalToken = "-" + originalName;
        var displayToken = "-" + displayName;
        var searchFrom = 0;
        while (searchFrom < text.Length)
        {
            var index = text.IndexOf(originalToken, searchFrom, StringComparison.Ordinal);
            if (index < 0) break;

            var end = index + originalToken.Length;
            if (end == text.Length || char.IsWhiteSpace(text[end]) || text[end] == ']')
            {
                text = text.Substring(0, index) + displayToken + text.Substring(end);
                searchFrom = index + displayToken.Length;
            }
            else
            {
                searchFrom = end;
            }
        }
        return text;
    }
}
