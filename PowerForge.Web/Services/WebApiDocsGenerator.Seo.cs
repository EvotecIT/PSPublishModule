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
        var itemLabel = options.Type == ApiDocsType.PowerShell ? "cmdlets" : "types";
        var description =
            $"Browse {title} for {documentedTypeCount:N0} documented {itemLabel}, with signatures, parameters, examples, related APIs, inheritance details, and source links.";

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
        var description = string.IsNullOrWhiteSpace(summary)
            ? $"Explore {name} in {title}, including syntax, members, parameters, examples, related APIs, inheritance details, and source links."
            : $"{name}: {summary.TrimEnd('.', '!', '?')}. Review syntax, members, parameters, examples, related APIs, and source links in {title}.";

        return FitApiSeoDescription(description);
    }

    private static string BuildApiSuiteSeoDescription(
        string title,
        int entryCount)
    {
        var normalizedTitle = NormalizeApiSeoText(title);
        var description =
            $"Browse {normalizedTitle} across {entryCount:N0} API references, with searchable symbols, curated guidance, coverage signals, related samples, and source links.";

        return FitApiSeoDescription(description);
    }

    private static string NormalizeApiSeoText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = StripCrefTokens(value);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = System.Web.HttpUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static string FitApiSeoDescription(string value)
    {
        var description = NormalizeApiSeoText(value);
        if (description.Length < ApiSeoDescriptionMinimumLength)
        {
            description = $"{description.TrimEnd('.', '!', '?')}. Includes current signatures, parameters, examples, related APIs, and source links.";
        }

        if (description.Length <= ApiSeoDescriptionMaximumLength)
            return description;

        var limit = ApiSeoDescriptionMaximumLength - 1;
        var wordBoundary = description.LastIndexOf(' ', limit);
        if (wordBoundary < ApiSeoDescriptionMinimumLength)
            wordBoundary = limit;

        return description[..wordBoundary].TrimEnd(' ', ',', ';', ':', '-', '.', '!', '?') + ".";
    }
}
