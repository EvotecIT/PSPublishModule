namespace PowerForge.Web;

internal static class SocialCardMetricNormalizer
{
    public static IReadOnlyList<SocialCardMetricSpec> Normalize(IEnumerable<SocialCardMetricSpec>? metrics)
    {
        if (metrics is null)
            return Array.Empty<SocialCardMetricSpec>();

        return metrics
            .Select(static metric => new SocialCardMetricSpec
            {
                Icon = Trim(metric.Icon, 24),
                Value = Trim(metric.Value, 16),
                Label = Trim(metric.Label, 24),
                Color = Trim(metric.Color, 32)
            })
            .Where(static metric => !string.IsNullOrWhiteSpace(metric.Value) || !string.IsNullOrWhiteSpace(metric.Label))
            .Take(5)
            .ToList();
    }

    internal static string Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return maxLength <= 3
            ? normalized[..maxLength]
            : normalized[..(maxLength - 3)] + "...";
    }
}
