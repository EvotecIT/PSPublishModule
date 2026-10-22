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
        if (!string.IsNullOrWhiteSpace(options.IndexDescription))
            return options.IndexDescription.Trim();
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
            : $"{name}: {CompleteApiSeoSentence(summary)} Review {referenceDetails} in {title}.";

        return FitApiSeoDescription(WebSiteBuilder.StripMatchingTitlePrefix(name, description));
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
            description = $"{CompleteApiSeoSentence(description)} Includes current reference details, navigation context, and practical implementation guidance for the documented API.";
        }

        if (description.Length <= ApiSeoDescriptionMaximumLength)
            return description.TrimEnd(',', ':');

        // Punctuation must end a sentence, rather than a version, URL, or .NET name.
        var sentenceEnd = 0;
        foreach (Match sentence in Regex.Matches(description, """[.!?][\p{Pe}\p{Pf}"']*(?=\s|$)""", RegexOptions.CultureInvariant, RegexTimeout))
        {
            var end = sentence.Index + sentence.Length;
            if (end > ApiSeoDescriptionMaximumLength)
                break;
            if (IsApiTechnicalPostfixPunctuation(description, sentence, sentenceEnd))
                continue;
            // Common prose abbreviations and dotted initialisms continue the sentence.
            if (description[sentence.Index] == '.' && Regex.IsMatch(description[..(sentence.Index + 1)],
                @"(?:\b(?:[a-z]\.){2,}|\b(?:etc|vs|dr|mr|mrs|ms|prof|sr|jr|st|mt|fig|eq|no|approx|cf|viz|al)\.)\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout))
                continue;
            sentenceEnd = end;
        }
        if (sentenceEnd > 0)
            return description[..sentenceEnd];

        var limit = ApiSeoDescriptionMaximumLength - 1;
        var wordBoundary = description.LastIndexOf(' ', limit);
        if (wordBoundary < 0)
            wordBoundary = ClampToUnicodeScalarBoundary(description, limit);

        var truncated = description[..wordBoundary].TrimEnd(' ', ',', ';', ':', '-', '.', '!', '?');
        // Repeat to remove chains such as "in the" without inventing a sentence ending.
        while (true)
        {
            var cleaned = Regex.Replace(truncated,
                @"(?<!\S)(?:a|an|the|and|or|but|nor|for|as|at|by|in|of|on|to|up|via|with|within|without|about|above|across|after|against|along|among|around|before|behind|below|beneath|beside|between|beyond|despite|down|during|except|from|inside|into|near|off|onto|out|outside|over|past|per|since|through|throughout|toward|towards|under|underneath|until|upon|versus|including)\z",
                string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)
                .TrimEnd(' ', ',', ';', ':', '-');
            if (cleaned == truncated)
            {
                if (truncated.Length > 0)
                    return truncated + "…";

                // An article before a long identifier must not hide all useful content.
                var tokenStart = wordBoundary + 1;
                var tokenEnd = description.IndexOf(' ', tokenStart);
                var token = tokenEnd < 0 ? description[tokenStart..] : description[tokenStart..tokenEnd];
                return token[..ClampToUnicodeScalarBoundary(token, limit)] + "…";
            }
            truncated = cleaned;
        }
    }

    // Add sentence punctuation without stripping nullable or automatic-variable markers.
    private static string CompleteApiSeoSentence(string value)
    {
        var ending = Regex.Match(value, """[.!?][\p{Pe}\p{Pf}"']*\z""", RegexOptions.CultureInvariant, RegexTimeout);
        return ending.Success && !IsApiTechnicalPostfixPunctuation(value, ending, 0)
            ? value
            : value + ".";
    }

    // Plain-text API summaries mix prose with nullable types and postfix expressions.
    // Require technical-token evidence so ordinary prose questions still terminate.
    private static bool IsApiTechnicalPostfixPunctuation(string description, Match sentence, int sentenceStart)
    {
        var punctuation = description[sentence.Index];
        if (punctuation is not ('?' or '!'))
            return false;

        var prefix = description[..sentence.Index];
        var token = Regex.Match(prefix, @"[\w.$<>\[\]`()]+\z", RegexOptions.CultureInvariant, RegexTimeout).Value;
        if (punctuation == '?' && (token == "$" || prefix.EndsWith("${", StringComparison.Ordinal)))
            return true;

        var following = description.AsSpan(sentence.Index + sentence.Length).TrimStart();
        if ((!following.IsEmpty && !char.IsLower(following[0])) || token.Length == 0)
            return false;

        var proseQuestion = punctuation == '?' && Regex.IsMatch(description[sentenceStart..sentence.Index],
            """^[\s\p{Ps}\p{Pi}"']*(?:who|what|when|where|why|how|which|whose|is|are|am|was|were|can|could|do|does|did|will|would|shall|should|may|might|must|has|have|had)\b""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        if (proseQuestion)
        {
            // A preposition can attach a further clause to a type inside a question.
            var attachedContinuation = !following.IsEmpty && Regex.IsMatch(following.ToString(),
                @"^(?:with|without|to|from|of|as|in|on|by|within|through|into|under|over)\b",
                RegexOptions.CultureInvariant, RegexTimeout);
            if (!attachedContinuation)
                return false;
        }

        if (GetCSharpKeyword(NormalizeCSharpPrimitiveType(token)) is not null ||
            token is "nint" or "nuint" ||
            (punctuation == '!' && (token is "null" or "default" or "value")) ||
            (token.Length == 1 && char.IsUpper(token[0])) ||
            token.Any(c => c is '.' or '_' or '<' or '>' or '[' or ']' or '(' or ')' or '`'))
            return true;

        var typeStyleIdentifier = token.Any(char.IsUpper) && token.Any(char.IsLower);
        return typeStyleIdentifier && (punctuation == '?' || !following.IsEmpty);
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
