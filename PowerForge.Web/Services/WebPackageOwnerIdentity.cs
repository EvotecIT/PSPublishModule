namespace PowerForge.Web;

/// <summary>Normalizes registry owner lists shared by catalog discovery and publication verification.</summary>
internal static class WebPackageOwnerIdentity
{
    private static readonly char[] Delimiters = [',', ';', '|', '/'];

    /// <summary>Splits a registry owner or author field into normalized identity values.</summary>
    public static IEnumerable<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var part in value.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }
}
