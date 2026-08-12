using System.Text.RegularExpressions;

namespace PowerForge;

internal static class VirusTotalReleaseArtifactSelector
{
    private static readonly HashSet<VirusTotalArtifactKind> DefinedKinds =
        new(Enum.GetValues(typeof(VirusTotalArtifactKind)).Cast<VirusTotalArtifactKind>());

    public static void ValidateConfiguration(PowerForgeVirusTotalOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (!options.Enabled)
            return;

        var credentialSources = 0;
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) credentialSources++;
        if (!string.IsNullOrWhiteSpace(options.ApiKeyFilePath)) credentialSources++;
        if (!string.IsNullOrWhiteSpace(options.ApiKeyEnvName)) credentialSources++;
        if (credentialSources != 1)
        {
            throw new InvalidOperationException(
                "Enabled VirusTotal Monitor publishing requires exactly one API key source: ApiKey, ApiKeyFilePath, or ApiKeyEnvName.");
        }

        if (options.ApiKey is { } inlineKey &&
            (inlineKey.IndexOf('\r') >= 0 || inlineKey.IndexOf('\n') >= 0))
        {
            throw new InvalidOperationException("VirusTotal ApiKey must be a single-line secret.");
        }

        var kinds = options.ArtifactKinds ?? Array.Empty<VirusTotalArtifactKind>();
        if (kinds.Length == 0)
            throw new InvalidOperationException("Enabled VirusTotal Monitor publishing requires at least one ArtifactKinds value.");
        if (kinds.Any(kind => !DefinedKinds.Contains(kind)))
            throw new InvalidOperationException("VirusTotal ArtifactKinds contains an undefined value.");
        if (kinds.Distinct().Count() != kinds.Length)
            throw new InvalidOperationException("VirusTotal ArtifactKinds values must be unique.");

        if (string.IsNullOrWhiteSpace(options.DestinationPathTemplate))
            throw new InvalidOperationException("VirusTotal DestinationPathTemplate is required when publishing is enabled.");
        if (!options.DestinationPathTemplate.Contains("{RelativePath}", StringComparison.Ordinal) &&
            !options.DestinationPathTemplate.Contains("{FileName}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "VirusTotal DestinationPathTemplate must contain {RelativePath} or {FileName} so artifacts cannot overwrite each other implicitly.");
        }

        if (options.VerificationTimeoutSeconds < 0)
            throw new InvalidOperationException("VirusTotal VerificationTimeoutSeconds must not be negative.");
        if (options.PollingIntervalSeconds <= 0)
            throw new InvalidOperationException("VirusTotal PollingIntervalSeconds must be positive.");
        if (options.RequestTimeoutSeconds <= 0)
            throw new InvalidOperationException("VirusTotal RequestTimeoutSeconds must be positive.");
        if (string.IsNullOrWhiteSpace(options.ReceiptPath))
        {
            throw new InvalidOperationException(
                "VirusTotal ReceiptPath is required so partial Monitor uploads can be checkpointed and resumed safely.");
        }
    }

    public static VirusTotalMonitorArtifact[] Select(
        IEnumerable<PowerForgeReleaseAssetEntry> entries,
        PowerForgeVirusTotalOptions options,
        string project,
        string version)
    {
        if (entries is null)
            throw new ArgumentNullException(nameof(entries));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        ValidatePathSegment(project, nameof(project));
        ValidatePathSegment(version, nameof(version));
        var selectedKinds = new HashSet<VirusTotalArtifactKind>(
            options.ArtifactKinds ?? Array.Empty<VirusTotalArtifactKind>());
        var selected = new List<VirusTotalMonitorArtifact>();

        foreach (var entry in entries)
        {
            if (entry is null || !TryClassify(entry, out var kind) || !selectedKinds.Contains(kind))
                continue;

            if (!entry.IsFinalPackageOutput)
            {
                throw new InvalidOperationException(
                    $"VirusTotal artifact '{entry.Path}' is not a verified final package output. Arbitrary configured files and source archives are not eligible.");
            }

            var sourcePath = FirstNonEmpty(entry.StagedPath, entry.Path);
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new InvalidOperationException("A selected VirusTotal release artifact is missing its source path.");

            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidOperationException($"Unable to determine a file name for VirusTotal artifact '{sourcePath}'.");

            var relativePath = string.IsNullOrWhiteSpace(entry.RelativeStagePath)
                ? fileName
                : NormalizeRelativePath(entry.RelativeStagePath!);
            var artifactVersion = string.IsNullOrWhiteSpace(entry.Version) ? version : entry.Version!.Trim();
            ValidatePathSegment(artifactVersion, nameof(entry.Version));

            var destinationPath = ApplyTemplate(
                options.DestinationPathTemplate,
                project,
                artifactVersion,
                kind,
                fileName,
                relativePath,
                entry);
            var details = string.IsNullOrWhiteSpace(options.DetailsTemplate)
                ? null
                : ApplyTemplate(
                    options.DetailsTemplate!,
                    project,
                    artifactVersion,
                    kind,
                    fileName,
                    relativePath,
                    entry);

            selected.Add(new VirusTotalMonitorArtifact
            {
                SourcePath = Path.GetFullPath(sourcePath),
                Kind = kind,
                DestinationPath = ValidateDestinationPath(destinationPath),
                Details = details
            });
        }

        var duplicateDestination = selected
            .GroupBy(artifact => artifact.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateDestination is not null)
        {
            throw new InvalidOperationException(
                $"VirusTotal DestinationPathTemplate produced duplicate path '{duplicateDestination.Key}'. Include a unique artifact token.");
        }

        if (options.Enabled && options.RequireMatchingArtifacts && selected.Count == 0)
            throw new InvalidOperationException("VirusTotal publishing is enabled, but no release artifacts matched ArtifactKinds.");

        return selected.ToArray();
    }

    private static bool TryClassify(PowerForgeReleaseAssetEntry entry, out VirusTotalArtifactKind kind)
    {
        var sourcePath = FirstNonEmpty(entry.StagedPath, entry.Path) ?? string.Empty;
        var fileName = Path.GetFileName(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        if (fileName.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".snupkg", StringComparison.OrdinalIgnoreCase))
        {
            kind = default;
            return false;
        }

        if (entry.Category == PowerForgeReleaseAssetCategory.Module &&
            extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            kind = VirusTotalArtifactKind.PowerShellModule;
            return true;
        }

        if (entry.Category == PowerForgeReleaseAssetCategory.Package &&
            extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            kind = VirusTotalArtifactKind.NuGetPackage;
            return true;
        }

        if (IsPackedBinaryCategory(entry.Category) && extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            kind = VirusTotalArtifactKind.ZipArchive;
            return true;
        }

        if (entry.Category == PowerForgeReleaseAssetCategory.Installer &&
            extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            kind = VirusTotalArtifactKind.MsiPackage;
            return true;
        }

        if ((entry.Category == PowerForgeReleaseAssetCategory.Installer ||
             entry.Category == PowerForgeReleaseAssetCategory.Store) &&
            IsMsixExtension(extension))
        {
            kind = VirusTotalArtifactKind.MsixPackage;
            return true;
        }

        if ((entry.Category == PowerForgeReleaseAssetCategory.Portable ||
             entry.Category == PowerForgeReleaseAssetCategory.Tool ||
             entry.Category == PowerForgeReleaseAssetCategory.Installer) &&
            extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            kind = VirusTotalArtifactKind.Executable;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsPackedBinaryCategory(PowerForgeReleaseAssetCategory category)
        => category is PowerForgeReleaseAssetCategory.Package
            or PowerForgeReleaseAssetCategory.Portable
            or PowerForgeReleaseAssetCategory.Installer
            or PowerForgeReleaseAssetCategory.Store
            or PowerForgeReleaseAssetCategory.Tool;

    private static bool IsMsixExtension(string extension)
        => extension.Equals(".msix", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".msixupload", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".appx", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".appxbundle", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".appxupload", StringComparison.OrdinalIgnoreCase);

    private static string ApplyTemplate(
        string template,
        string project,
        string version,
        VirusTotalArtifactKind kind,
        string fileName,
        string relativePath,
        PowerForgeReleaseAssetEntry entry)
    {
        var output = template
            .Replace("{Project}", project)
            .Replace("{Version}", version)
            .Replace("{Kind}", kind.ToString())
            .Replace("{FileName}", fileName)
            .Replace("{RelativePath}", relativePath)
            .Replace("{Target}", entry.Target ?? string.Empty)
            .Replace("{Runtime}", entry.Runtime ?? string.Empty)
            .Replace("{Framework}", entry.Framework ?? string.Empty);

        if (Regex.IsMatch(output, "\\{[A-Za-z][A-Za-z0-9]*\\}"))
            throw new InvalidOperationException($"VirusTotal template contains an unsupported token: '{template}'.");
        return output.Replace('\\', '/');
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Split('/').Any(segment => segment is "." or ".." || string.IsNullOrWhiteSpace(segment)))
        {
            throw new InvalidOperationException($"VirusTotal artifact relative path is invalid: '{value}'.");
        }
        return normalized;
    }

    private static string ValidateDestinationPath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.EndsWith("/", StringComparison.Ordinal) ||
            normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.Split('/').Skip(1).Any(segment => segment is "." or ".." || string.IsNullOrWhiteSpace(segment)))
        {
            throw new InvalidOperationException($"VirusTotal destination path is invalid: '{value}'.");
        }
        return normalized;
    }

    private static void ValidatePathSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.IndexOf('/') >= 0 ||
            value.IndexOf('\\') >= 0 ||
            value is "." or "..")
        {
            throw new ArgumentException("VirusTotal path token values must be non-empty single path segments.", parameterName);
        }
    }

    private static string? FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first : second;
}
