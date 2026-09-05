using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Copies parser-owned comment help into stable compiler metadata.
/// </summary>
internal static class PowerShellCommentHelpBinder
{
    internal static PowerShellBoundHelpMetadata? Bind(FunctionDefinitionAst function)
        => Bind(function?.GetHelpContent());

    internal static PowerShellBoundHelpMetadata? Bind(CommentHelpInfo? help)
    {
        if (help is null) return null;
        return new PowerShellBoundHelpMetadata(
            Normalize(help.Synopsis),
            Normalize(help.Description),
            Normalize(help.Notes),
            help.Parameters.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static pair => pair.Key, static pair => Normalize(pair.Value), StringComparer.OrdinalIgnoreCase),
            Normalize(help.Examples),
            Normalize(help.Links),
            Normalize(help.Inputs),
            Normalize(help.Outputs));
    }

    private static string[] Normalize(IEnumerable<string> values)
        => values.Select(Normalize).Where(static value => value.Length > 0).ToArray();

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim();
}
