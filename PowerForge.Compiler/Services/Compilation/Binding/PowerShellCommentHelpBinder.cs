using System.Management.Automation.Language;
using System.Runtime.CompilerServices;

namespace PowerForge;

/// <summary>
/// Copies parser-owned comment help into stable compiler metadata.
/// </summary>
internal static class PowerShellCommentHelpBinder
{
    // PowerShell otherwise reparses the complete source for each function's help.
    // Keep its native token cache with the immutable syntax tree, without retaining
    // documents after their compilation has finished.
    private static readonly ConditionalWeakTable<Ast, Dictionary<Ast, Token[]>> TokenCaches = new();

    internal static PowerShellBoundHelpMetadata? Bind(FunctionDefinitionAst function, PowerShellSymbolId? symbol = null)
    {
        // Synthetic bodies have no authored function declaration for PowerShell's comment-help token search.
        if (function is null || symbol is { Kind: not PowerShellSymbolKind.Function }) return null;
        Ast root = function;
        while (root.Parent is { } parent) root = parent;
        var tokens = TokenCaches.GetValue(root, static _ => new Dictionary<Ast, Token[]>());
        lock (tokens)
            return Bind(function.GetHelpContent(tokens));
    }

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
