namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>Resolves the publish matrix once for planning and final-artifact verification.</summary>
    internal static DotNetPublishTargetCombination[] ResolveTargetCombinations(DotNetPublishTarget t, DotNetPublishSpec spec)
    {
        var defaultsRids = (spec.DotNet.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matrixDefaultRids = NormalizeStrings(spec.Matrix?.Runtimes);
        var matrixDefaultFrameworks = NormalizeStrings(spec.Matrix?.Frameworks);
        var matrixDefaultStyles = NormalizeStyles(spec.Matrix?.Styles);
        var frameworks = NormalizeStrings(t.Publish.Frameworks);
        if (frameworks.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(t.Publish.Framework))
                frameworks = new[] { t.Publish.Framework.Trim() };
            else if (matrixDefaultFrameworks.Length > 0)
                frameworks = matrixDefaultFrameworks;
        }
        if (frameworks.Length == 0)
            throw new ArgumentException($"Target.Publish.Framework is required for '{t.Name}' (or set Target.Publish.Frameworks/Matrix.Frameworks).", nameof(spec));

        var rids = NormalizeStrings(t.Publish.Runtimes);
        if (t.SupportedRuntimes is null || t.SupportedRuntimes.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Target '{t.Name}' SupportedRuntimes must contain only nonblank runtime names.", nameof(spec));
        var supportedRuntimes = NormalizeStrings(t.SupportedRuntimes);
        if (rids.Length == 0) rids = supportedRuntimes;
        if (rids.Length == 0) rids = matrixDefaultRids;
        if (rids.Length == 0) rids = defaultsRids;
        if (rids.Length == 0)
            throw new ArgumentException($"No runtimes provided for target '{t.Name}'. Set Target.Publish.Runtimes, Matrix.Runtimes or DotNet.Runtimes.", nameof(spec));

        var styles = NormalizeStyles(t.Publish.Styles);
        if (styles.Length == 0 && matrixDefaultStyles.Length > 0)
            styles = matrixDefaultStyles;
        if (styles.Length == 0)
            styles = new[] { t.Publish.Style };

        var combos = BuildPublishCombos(t.Name.Trim(), frameworks, rids, styles, spec.Matrix);
        if (supportedRuntimes.Length > 0)
        {
            var unsupported = combos.Select(combo => combo.Runtime).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(runtime => !supportedRuntimes.Contains(runtime, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (unsupported.Length > 0)
                throw new ArgumentException($"Target '{t.Name}' does not support runtime(s): {string.Join(", ", unsupported)}.", nameof(spec));
        }
        if (combos.Length == 0)
            throw new ArgumentException($"No publish combinations resolved for target '{t.Name}'. Check Matrix include/exclude filters.", nameof(spec));

        return combos;
    }

    private static DotNetPublishTargetCombination[] BuildPublishCombos(
        string targetName,
        string[] frameworks,
        string[] runtimes,
        DotNetPublishStyle[] styles,
        DotNetPublishMatrix? matrix)
    {
        var combos = new List<DotNetPublishTargetCombination>();
        foreach (var framework in frameworks)
        {
            foreach (var runtime in runtimes)
            {
                foreach (var style in styles)
                {
                    combos.Add(new DotNetPublishTargetCombination
                    {
                        Framework = framework,
                        Runtime = runtime,
                        Style = style
                    });
                }
            }
        }

        var include = matrix?.Include ?? Array.Empty<DotNetPublishMatrixRule>();
        if (include.Length > 0)
        {
            combos = combos
                .Where(c => include.Any(rule => RuleMatches(targetName, c, rule)))
                .ToList();
        }

        var exclude = matrix?.Exclude ?? Array.Empty<DotNetPublishMatrixRule>();
        if (exclude.Length > 0)
        {
            combos = combos
                .Where(c => !exclude.Any(rule => RuleMatches(targetName, c, rule)))
                .ToList();
        }

        return combos
            .OrderBy(c => c.Framework, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Runtime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Style.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool RuleMatches(string targetName, DotNetPublishTargetCombination combo, DotNetPublishMatrixRule? rule)
    {
        if (rule is null) return false;

        var targetPatterns = NormalizeStrings(rule.Targets);
        if (targetPatterns.Length > 0 && !targetPatterns.Any(p => WildcardMatch(targetName, p)))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Runtime) && !WildcardMatch(combo.Runtime, rule.Runtime!.Trim()))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Framework) && !WildcardMatch(combo.Framework, rule.Framework!.Trim()))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Style) && !WildcardMatch(combo.Style.ToString(), rule.Style!.Trim()))
            return false;

        return true;
    }

}
