namespace PowerForge;

/// <summary>Validates full Git object identifiers for SHA-1 and SHA-256 repositories.</summary>
internal static class GitObjectId
{
    internal static bool IsFull(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value!.Length == 40 || value.Length == 64) &&
           value.All(Uri.IsHexDigit);

    internal static bool IsFullForObjectFormat(string? value, string objectFormat)
    {
        var expectedLength = objectFormat.Equals("sha1", StringComparison.OrdinalIgnoreCase)
            ? 40
            : objectFormat.Equals("sha256", StringComparison.OrdinalIgnoreCase)
                ? 64
                : throw new InvalidOperationException($"Unsupported Git object format '{objectFormat}'.");
        return !string.IsNullOrWhiteSpace(value) &&
               value!.Length == expectedLength &&
               value.All(Uri.IsHexDigit);
    }
}
