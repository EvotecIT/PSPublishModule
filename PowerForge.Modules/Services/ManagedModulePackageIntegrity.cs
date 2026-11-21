namespace PowerForge;

internal static class ManagedModulePackageIntegrity
{
    public static void VerifyDownload(ManagedModuleDownloadResult download, string? expectedSha256)
    {
        var expected = NormalizeSha256(expectedSha256);
        if (expected is null)
            return;

        var actual = NormalizeSha256(download.PackageSha256);
        if (actual is null)
            throw new ManagedModulePackageIntegrityException(
                download.Name,
                download.Version,
                expected,
                download.PackageSha256,
                download.PackagePath);

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new ManagedModulePackageIntegrityException(
                download.Name,
                download.Version,
                expected,
                actual,
                download.PackagePath);
    }

    public static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value!.Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("sha256:".Length).Trim();

        normalized = new string(normalized.Where(static value => value != ' ').ToArray());
        if (normalized.Length != 64 || !normalized.All(IsHex))
            throw new ArgumentException("ExpectedPackageSha256 must be a 64-character SHA256 hexadecimal hash.", nameof(value));

        return normalized.ToLowerInvariant();
    }

    private static bool IsHex(char value)
        => value is >= '0' and <= '9' ||
           value is >= 'a' and <= 'f' ||
           value is >= 'A' and <= 'F';
}
