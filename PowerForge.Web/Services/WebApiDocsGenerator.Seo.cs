using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private const int ApiSeoDescriptionMinimumLength = 120;
    private const int ApiSeoDescriptionMaximumLength = 160;

    private static string BuildApiIndexSeoDescription(
        WebApiDocsOptions options,
        int documentedTypeCount)
    {
        var title = NormalizeApiSeoText(options.Title);
        var count = FormatApiSeoCount(documentedTypeCount);
        var referenceEntryLabel = documentedTypeCount == 1
            ? "documented reference entry"
            : "documented reference entries";
        var typeLabel = documentedTypeCount == 1
            ? "documented type"
            : "documented types";
        var richTemplate = UsesRichApiDocsTemplate(options);
        var description = options.Type == ApiDocsType.PowerShell
            ? richTemplate
                ? $"Browse {title} for {count} {referenceEntryLabel}, with searchable syntax, parameters, pipeline details, and navigation across the API."
                : $"Browse {title} for {count} {referenceEntryLabel}, with command summaries, reference pages, and navigation across the available API."
            : richTemplate
                ? $"Browse {title} for {count} {typeLabel}, with searchable signatures, members, parameters, type relationships, and navigation across the API."
                : $"Browse {title} for {count} {typeLabel}, with summaries, generated reference pages, member details, and navigation across the available API.";

        return FitApiSeoDescription(description);
    }

    private static string BuildApiTypeSeoDescription(
        WebApiDocsOptions options,
        ApiTypeModel type,
        string displayName)
    {
        var title = NormalizeApiSeoText(options.Title);
        var name = NormalizeApiSeoText(displayName);
        var summary = NormalizeApiSeoText(type.Summary);
        var richTemplate = UsesRichApiDocsTemplate(options);
        var isAboutTopic = options.Type == ApiDocsType.PowerShell &&
                           string.Equals(type.Kind, "About", StringComparison.OrdinalIgnoreCase);
        var referenceDetails = isAboutTopic
            ? "the complete conceptual topic, related help context, and navigation"
            : options.Type == ApiDocsType.PowerShell
                ? richTemplate
                    ? "available syntax, parameters, pipeline guidance, and reference details"
                    : "command summaries, syntax details, and navigation"
                : richTemplate
                    ? "signatures, members, parameters, type relationships, and available reference details"
                    : "type summaries, signatures, member details, and navigation";
        var description = string.IsNullOrWhiteSpace(summary)
            ? $"Explore {name} in {title}, including {referenceDetails}."
            : $"{name}: {summary.TrimEnd('.', '!', '?')}. Review {referenceDetails} in {title}.";

        return FitApiSeoDescription(description);
    }

    private static string BuildApiSuiteSeoDescription(
        string title,
        ApiSuiteContext suite)
    {
        var normalizedTitle = NormalizeApiSeoText(title);
        var count = FormatApiSeoCount(suite.Entries.Count);
        var capabilities = new List<string>();
        if (!string.IsNullOrWhiteSpace(suite.SearchUrl))
            capabilities.Add("symbol search");
        if (!string.IsNullOrWhiteSpace(suite.NarrativeUrl))
            capabilities.Add("curated guidance");
        if (!string.IsNullOrWhiteSpace(suite.CoverageUrl))
            capabilities.Add("coverage signals");
        if (!string.IsNullOrWhiteSpace(suite.RelatedContentUrl))
            capabilities.Add("related guides and samples");
        if (!string.IsNullOrWhiteSpace(suite.XrefMapUrl))
            capabilities.Add("cross-project references");

        var description = capabilities.Count == 0
            ? $"Browse {normalizedTitle} across {count} API references, compare project scopes, and open each documented API from one cross-project landing page."
            : $"Browse {normalizedTitle} across {count} API references with {FormatApiSeoActions(capabilities)}. Compare project scopes and open each API from one landing page.";

        return FitApiSeoDescription(description);
    }

    private static string FormatApiSeoActions(IReadOnlyList<string> actions)
    {
        if (actions.Count == 1)
            return actions[0];
        if (actions.Count == 2)
            return $"{actions[0]} and {actions[1]}";

        return $"{string.Join(", ", actions.Take(actions.Count - 1))}, and {actions[^1]}";
    }

    private static string FormatApiSeoCount(int value)
        => value.ToString("N0", CultureInfo.InvariantCulture);

    private static bool UsesRichApiDocsTemplate(WebApiDocsOptions options)
    {
        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        return template is "docs" or "sidebar";
    }

    private static string NormalizeApiSeoText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = StripCrefTokens(value);
        text = System.Web.HttpUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static string FitApiSeoDescription(string value)
    {
        var description = NormalizeApiSeoText(value);
        if (description.Length < ApiSeoDescriptionMinimumLength)
        {
            description = $"{description.TrimEnd('.', '!', '?')}. Includes current reference details, navigation context, and practical implementation guidance for the documented API.";
        }

        if (description.Length <= ApiSeoDescriptionMaximumLength)
            return description;

        var limit = ApiSeoDescriptionMaximumLength - 1;
        var wordBoundary = description.LastIndexOf(' ', limit);
        if (wordBoundary < ApiSeoDescriptionMinimumLength)
            wordBoundary = ClampToUnicodeScalarBoundary(description, limit);

        var truncated = description[..wordBoundary].TrimEnd(' ', ',', ';', ':', '-', '.', '!', '?');
        var withoutDanglingWord = Regex.Replace(
            truncated,
            @"(?:,\s*)?\b(?:and|or|with|including|from|to)\z",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).TrimEnd(' ', ',', ';', ':', '-');
        if (withoutDanglingWord.Length + 1 >= ApiSeoDescriptionMinimumLength)
            return withoutDanglingWord + ".";

        var completed = withoutDanglingWord + " reference details";
        if (completed.Length <= limit)
            return completed + ".";

        return truncated + ".";
    }

    private static int ClampToUnicodeScalarBoundary(string value, int boundary)
    {
        var safeBoundary = Math.Clamp(boundary, 0, value.Length);
        if (safeBoundary > 0 &&
            safeBoundary < value.Length &&
            char.IsHighSurrogate(value[safeBoundary - 1]) &&
            char.IsLowSurrogate(value[safeBoundary]))
        {
            safeBoundary--;
        }

        return safeBoundary;
    }
}
