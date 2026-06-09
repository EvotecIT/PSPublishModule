using System;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static string TrimLeadingRelativeSegments(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        while (normalized.StartsWith("../", StringComparison.Ordinal) || normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized.StartsWith("../", StringComparison.Ordinal)
                ? normalized[3..]
                : normalized[2..];
        }

        return normalized.Trim('/');
    }
}
