namespace PowerForge.Web;

internal static partial class WebVisualStoryCssAnimationValidator
{
    private readonly record struct CascadedDeclaration(
        string Property,
        string Value,
        bool Important,
        int Specificity,
        int SourceOrder);

    internal static IReadOnlySet<string> GetEffectiveAnimationNamesForMatchingSelectors(
        string css,
        IReadOnlyList<string?> inlineStyles,
        Func<string, int, bool> selectorMatches)
    {
        ArgumentNullException.ThrowIfNull(inlineStyles);
        ArgumentNullException.ThrowIfNull(selectorMatches);
        var normalizedCss = RemoveUnsupportedConditionalRuleBlocks(RemoveComments(css));
        var names = new HashSet<string>(StringComparer.Ordinal);
        var rules = GetStyleRules(normalizedCss);
        for (var elementIndex = 0; elementIndex < inlineStyles.Count; elementIndex++)
        {
            var declarations = new List<CascadedDeclaration>();
            for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                var rule = rules[ruleIndex];
                var specificity = -1;
                foreach (var selector in SplitTopLevel(rule.Selector, ','))
                {
                    if (selectorMatches(selector, elementIndex))
                        specificity = Math.Max(specificity, GetSimpleSelectorSpecificity(selector));
                }
                if (specificity < 0)
                    continue;
                AddCascadedDeclarations(declarations, rule.Declarations, specificity, ruleIndex);
            }

            if (!string.IsNullOrWhiteSpace(inlineStyles[elementIndex]))
                AddCascadedDeclarations(declarations, inlineStyles[elementIndex]!, 1_000_000, rules.Count);
            var effectiveCss = string.Join(
                ";",
                declarations
                    .OrderBy(static declaration => declaration.Important)
                    .ThenBy(static declaration => declaration.Specificity)
                    .ThenBy(static declaration => declaration.SourceOrder)
                    .Select(static declaration => declaration.Property + ":" + declaration.Value));
            foreach (var name in GetEffectiveAnimationNamesFromDeclaration(effectiveCss))
                names.Add(name);
        }
        return names;
    }

    private static void AddCascadedDeclarations(
        List<CascadedDeclaration> destination,
        string declarations,
        int specificity,
        int sourceOrder)
    {
        foreach (var declaration in SplitTopLevel(declarations, ';'))
        {
            var separator = FindTopLevelSeparator(declaration, ':');
            if (separator <= 0)
                continue;
            var property = declaration.Substring(0, separator).Trim();
            if (!property.StartsWith("animation", StringComparison.OrdinalIgnoreCase))
                continue;
            var rawValue = declaration.Substring(separator + 1).Trim();
            const string important = "!important";
            var isImportant = rawValue.EndsWith(important, StringComparison.OrdinalIgnoreCase);
            var value = isImportant
                ? rawValue.Substring(0, rawValue.Length - important.Length).TrimEnd()
                : rawValue;
            if (value.Length > 0)
                destination.Add(new CascadedDeclaration(property, value, isImportant, specificity, sourceOrder));
        }
    }

    private static int GetSimpleSelectorSpecificity(string selector)
    {
        var ids = 0;
        var classes = 0;
        var elements = 0;
        var token = selector.Trim();
        for (var index = 0; index < token.Length; index++)
        {
            if (token[index] == '#')
                ids++;
            else if (token[index] == '.')
                classes++;
            else if (index == 0 && token[index] != '*')
                elements = 1;
        }
        return ids * 100 + classes * 10 + elements;
    }
}
