namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static string? WritePowerShellExampleMediaManifest(
        string outputPath,
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || options is null || types is null || warnings is null)
            return null;

        var entries = types
            .Where(IsPowerShellCommandType)
            .SelectMany(type => type.Examples
                .Where(static example =>
                    example is not null &&
                    example.Kind.Equals("media", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(example.Origin, ApiExampleOrigins.ImportedScript, StringComparison.OrdinalIgnoreCase) &&
                    example.Media is not null &&
                    example.Media.Type.Equals("terminal", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(example.Media.Url))
                .Select(example => BuildPowerShellExampleMediaManifestEntry(type, example.Media!, options)))
            .Where(static entry => entry is not null)
            .Cast<Dictionary<string, object?>>()
            .OrderBy(static entry => entry["commandName"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry["mediaUrl"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (entries.Length == 0)
            return null;

        var manifestPath = Path.Combine(outputPath, "powershell-example-media-manifest.json");
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["baseUrl"] = NormalizeApiRoute(options.BaseUrl ?? "/api"),
                ["entryCount"] = entries.Length,
                ["entries"] = entries
            };
            WriteJson(manifestPath, payload);
            return manifestPath;
        }
        catch (Exception ex)
        {
            warnings.Add($"API docs PowerShell coverage: failed to write example media manifest '{manifestPath}' ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static Dictionary<string, object?>? BuildPowerShellExampleMediaManifestEntry(
        ApiTypeModel type,
        ApiExampleMediaModel media,
        WebApiDocsOptions options)
    {
        if (type is null || media is null || string.IsNullOrWhiteSpace(media.Url))
            return null;

        return new Dictionary<string, object?>
        {
            ["commandName"] = type.Name,
            ["typeFullName"] = type.FullName,
            ["typeSlug"] = type.Slug,
            ["mediaType"] = media.Type,
            ["mediaUrl"] = media.Url,
            ["posterUrl"] = media.PosterUrl,
            ["mimeType"] = media.MimeType,
            ["sourcePath"] = NormalizePowerShellExampleManifestPath(media.SourcePath, options),
            ["assetPath"] = NormalizePowerShellExampleManifestPath(media.AssetPath, options),
            ["posterAssetPath"] = NormalizePowerShellExampleManifestPath(media.PosterAssetPath, options),
            ["capturedAtUtc"] = media.CapturedAtUtc?.ToString("O"),
            ["sourceUpdatedAtUtc"] = media.SourceUpdatedAtUtc?.ToString("O"),
            ["hasPoster"] = !string.IsNullOrWhiteSpace(media.PosterUrl),
            ["hasUnsupportedSidecars"] = media.HasUnsupportedSidecars,
            ["hasOversizedAssets"] = media.HasOversizedAssets,
            ["hasStaleAssets"] = media.HasStaleAssets
        };
    }

    private static string? NormalizePowerShellExampleManifestPath(string? path, WebApiDocsOptions options)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!string.IsNullOrWhiteSpace(options.PowerShellExamplesPath))
            {
                var examplesRoot = GetValidationExamplesRoot(options.PowerShellExamplesPath) ?? Path.GetFullPath(options.PowerShellExamplesPath);
                var relativeToExamples = Path.GetRelativePath(examplesRoot, fullPath);
                if (!relativeToExamples.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativeToExamples))
                    return relativeToExamples.Replace('\\', '/');
            }

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                var outputRoot = Path.GetFullPath(options.OutputPath);
                var relativeToOutput = Path.GetRelativePath(outputRoot, fullPath);
                if (!relativeToOutput.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativeToOutput))
                    return relativeToOutput.Replace('\\', '/');
            }

            return Path.GetFileName(fullPath);
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }
}
