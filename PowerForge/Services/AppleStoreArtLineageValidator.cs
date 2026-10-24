namespace PowerForge;

/// <summary>Validates exact composed outputs and their membership in a raw capture inventory.</summary>
public static class AppleStoreArtLineageValidator
{
    /// <summary>Checks lineage metadata, raw inventory membership, and current raw/output bytes under captureRoot.</summary>
    public static void Validate(AppleStoreArtLineage[] compositions, AppleStoreArtInput[] rawInventory, string captureRoot)
    {
        if (compositions is null || rawInventory is null) throw new ArgumentNullException(nameof(compositions));
        if (compositions.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != compositions.Length)
            throw new InvalidOperationException("Store art lineage contains duplicate output paths.");
        foreach (var composition in compositions)
        {
            ValidateMetadata(composition);
            if (rawInventory.Any(i => string.Equals(i.Path, composition.Path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A composed output cannot replace a raw capture inventory entry.");
            VerifyFile(captureRoot, composition.Path, composition.Sha256, composition.Width, composition.Height);
            foreach (var input in composition.Inputs)
            {
                if (!rawInventory.Any(i => i.Path == input.Path && EqualHash(i.Sha256, input.Sha256) && i.Width == input.Width && i.Height == input.Height))
                    throw new InvalidOperationException($"Store art raw input '{input.Path}' is not an exact member of the capture provenance inventory.");
                VerifyFile(captureRoot, input.Path, input.Sha256, input.Width, input.Height);
            }
        }
    }

    /// <summary>Validates the self-contained metadata retained in a v3 approval entry.</summary>
    public static void ValidateMetadata(AppleStoreArtLineage composition)
    {
        if (composition is null || string.IsNullOrWhiteSpace(composition.Frame) || string.IsNullOrWhiteSpace(composition.Locale) ||
            string.IsNullOrWhiteSpace(composition.Theme) || string.IsNullOrWhiteSpace(composition.Device) || string.IsNullOrWhiteSpace(composition.Renderer) ||
            composition.Width <= 0 || composition.Height <= 0 || composition.Inputs is null || composition.Inputs.Length == 0 ||
            !IsHash(composition.Sha256) || !IsHash(composition.TemplateSha256) || !IsHash(composition.SpecSha256) ||
            !IsHash(composition.CaptionsSha256) || !IsHash(composition.RenderSha256) ||
            composition.PerceptualHash is null || !System.Text.RegularExpressions.Regex.IsMatch(composition.PerceptualHash, "^dhash-v1:[0-9a-f]{16}$"))
            throw new InvalidOperationException("Store art lineage requires frame, locale, device, theme, renderer, exact hashes, perceptual hash, and raw inputs.");
        SafeRelative(composition.Path);
        if (composition.Inputs.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != composition.Inputs.Length)
            throw new InvalidOperationException("Store art lineage contains duplicate raw input paths.");
        foreach (var input in composition.Inputs)
        {
            SafeRelative(input.Path);
            if (!IsHash(input.Sha256) || input.Width <= 0 || input.Height <= 0)
                throw new InvalidOperationException("Store art lineage contains an invalid raw input identity.");
        }
    }
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool EqualHash(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static void SafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) || path.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidOperationException($"Unsafe store art lineage path '{path}'.");
    }
    private static void VerifyFile(string root, string path, string hash, int width, int height)
    {
        SafeRelative(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(fullRoot, path));
        var current = full;
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Store art lineage traverses a link: {current}");
            if (current == fullRoot) break;
            current = Path.GetDirectoryName(current) ?? throw new InvalidOperationException("Store art path escaped capture root.");
        }
        if (!EqualHash(AppStoreConnectScreenshotSyncConfigValidator.ComputeSha256(full), hash) ||
            !AppStoreConnectScreenshotSyncConfigValidator.TryReadPngDimensions(full, out var actualWidth, out var actualHeight) ||
            width != actualWidth || height != actualHeight)
            throw new InvalidOperationException($"Store art lineage bytes or dimensions changed: {path}");
    }
}
