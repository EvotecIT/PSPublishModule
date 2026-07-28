namespace PowerForge;

/// <summary>Creates exact-byte approval manifests after screenshot review.</summary>
public sealed class AppStoreConnectScreenshotApprovalService
{
    /// <summary>Validates selected PNG files and creates their approval manifest.</summary>
    public AppStoreConnectScreenshotApprovalManifest Create(AppStoreConnectScreenshotApprovalRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Spec is null)
            throw new ArgumentException("Spec is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VersionString))
            throw new ArgumentException("VersionString is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SourceCommit) ||
            request.SourceCommit.Trim().Length != 40 ||
            !request.SourceCommit.Trim().All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SourceCommit must be an exact 40-character Git commit SHA.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.ApprovedBy))
            throw new ArgumentException("ApprovedBy is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AllowedRoot))
            throw new ArgumentException("AllowedRoot is required.", nameof(request));

        var allowedRoot = Path.GetFullPath(request.AllowedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(allowedRoot))
            throw new DirectoryNotFoundException($"Reviewed screenshot root does not exist: {allowedRoot}");
        if ((File.GetAttributes(allowedRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Reviewed screenshot root must not be a symbolic link or reparse point: {allowedRoot}");

        var spec = CloneWithoutApprovalRequirement(request.Spec);
        var validation = new AppStoreConnectScreenshotSyncConfigValidator().Validate(
            spec,
            request.BaseDirectory);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Screenshot approval requires a valid capture set: " +
                string.Join(" ", validation.Messages.Concat(validation.ScreenshotSets.SelectMany(static set => set.Messages))));
        }

        var entries = validation.ScreenshotSets.SelectMany(set => set.Files.Select(file =>
        {
            EnsureWithinAllowedRoot(file, allowedRoot);
            if (!AppStoreConnectScreenshotSyncConfigValidator.TryReadPngDimensions(file, out var width, out var height))
                throw new InvalidOperationException($"Screenshot is not a readable PNG: {file}");
            return new AppStoreConnectScreenshotApprovalEntry
            {
                ScreenshotDisplayType = set.ScreenshotDisplayType,
                File = FrameworkCompatibility.GetRelativePath(request.BaseDirectory, file).Replace('\\', '/'),
                Sha256 = AppStoreConnectScreenshotSyncConfigValidator.ComputeSha256(file),
                Width = width,
                Height = height
            };
        })).ToArray();

        return new AppStoreConnectScreenshotApprovalManifest
        {
            VersionString = request.VersionString.Trim(),
            SourceCommit = request.SourceCommit.Trim(),
            XcodeVersion = Normalize(request.XcodeVersion),
            Runtime = Normalize(request.Runtime),
            Device = Normalize(request.Device),
            Locale = request.Spec.Locale.Trim(),
            Theme = Normalize(request.Theme),
            Scenario = Normalize(request.Scenario),
            ApprovedAt = request.ApprovedAt ?? DateTimeOffset.UtcNow,
            ApprovedBy = request.ApprovedBy.Trim(),
            InitiatedBy = Normalize(request.InitiatedBy),
            ApprovalEvidence = Normalize(request.ApprovalEvidence),
            Screenshots = entries
        };
    }

    private static void EnsureWithinAllowedRoot(string file, string allowedRoot)
    {
        var candidate = Path.GetFullPath(file);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(allowedRoot + Path.DirectorySeparatorChar, comparison) &&
            !candidate.StartsWith(allowedRoot + Path.AltDirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException(
                $"Screenshot is outside the reviewed capture root '{allowedRoot}': {candidate}");
        }

        var current = candidate;
        while (!current.Equals(allowedRoot, comparison))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Screenshot path must not traverse a symbolic link or reparse point: {current}");
            }
            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException("Screenshot path could not be validated against the reviewed capture root.");
        }
    }

    private static AppStoreConnectScreenshotSyncSpec CloneWithoutApprovalRequirement(
        AppStoreConnectScreenshotSyncSpec spec)
        => new()
        {
            AppId = spec.AppId,
            VersionString = spec.VersionString,
            VersionId = spec.VersionId,
            UseReleaseVersion = spec.UseReleaseVersion,
            Platform = spec.Platform,
            Locale = spec.Locale,
            ScreenshotSets = spec.ScreenshotSets,
            Quality = new AppStoreConnectScreenshotQualitySpec
            {
                Enabled = spec.Quality?.Enabled == true,
                RejectDuplicates = spec.Quality?.RejectDuplicates ?? true,
                RequireConsistentDimensions = spec.Quality?.RequireConsistentDimensions ?? true,
                MinimumFileBytes = spec.Quality?.MinimumFileBytes ?? 4096,
                MinimumKilobytesPerMegapixel = spec.Quality?.MinimumKilobytesPerMegapixel ?? 12
            }
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
