using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Query-selector validation and display helpers for link-service redirects.</summary>
public static partial class WebLinkService
{
    private static readonly Regex SafeQueryParameterNameRegex = new(
        "^[a-z0-9][a-z0-9._~-]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static void ValidateSourceQuerySelector(LinkRedirectRule redirect, List<LinkValidationIssue> issues, string label)
    {
        if (!string.IsNullOrWhiteSpace(redirect.SourceQuery) &&
            !string.IsNullOrWhiteSpace(redirect.SourceQueryParameter))
        {
            AddIssue(
                issues,
                LinkValidationSeverity.Error,
                "PFLINK.REDIRECT.QUERY_SELECTOR_CONFLICT",
                "Redirect sourceQuery and sourceQueryParameter are mutually exclusive.",
                "redirect",
                label);
            return;
        }

        if (!string.IsNullOrWhiteSpace(redirect.SourceQueryParameter) &&
            !SafeQueryParameterNameRegex.IsMatch(redirect.SourceQueryParameter.Trim()))
        {
            AddIssue(
                issues,
                LinkValidationSeverity.Error,
                "PFLINK.REDIRECT.QUERY_PARAMETER",
                "Redirect sourceQueryParameter must be a URL-safe query parameter name.",
                "redirect",
                label);
        }
    }

    private static bool HasSourceQuerySelector(LinkRedirectRule redirect)
        => !string.IsNullOrWhiteSpace(redirect.SourceQuery) ||
           !string.IsNullOrWhiteSpace(redirect.SourceQueryParameter);

    private static string NormalizeSourceQueryParameter(string? parameter)
        => string.IsNullOrWhiteSpace(parameter) ? string.Empty : parameter.Trim();

    private static string BuildSourceQueryDisplay(LinkRedirectRule redirect)
    {
        if (!string.IsNullOrWhiteSpace(redirect.SourceQuery))
            return redirect.SourceQuery.Trim().TrimStart('?');

        var parameter = NormalizeSourceQueryParameter(redirect.SourceQueryParameter);
        return string.IsNullOrWhiteSpace(parameter) ? string.Empty : parameter + "=*";
    }

    private static IEnumerable<string> EnumerateSourceQueryParameterNames(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        foreach (var segment in query.Trim().TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = segment.IndexOf('=');
            var name = equalsIndex < 0 ? segment : segment[..equalsIndex];
            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
        }
    }
}
