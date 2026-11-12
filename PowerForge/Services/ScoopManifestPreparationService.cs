using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static PowerForge.ReleaseCatalogPreparationService;

namespace PowerForge;

/// <summary>Prepares a Scoop manifest from verified release archives without building or publishing.</summary>
internal sealed class ScoopManifestPreparationService
{
    /// <summary>Checks the release manifest, checksums, selected archives and executable before exposing output.</summary>
    public string Prepare(PowerForgeReleaseSpec spec, string manifestPath, string checksumsPath, string assetRoot, string outputRoot)
    {
        var package = spec.Scoop ?? throw new InvalidOperationException("The release config must define Scoop metadata.");
        Validate(package, spec.GitHub);
        var output = Path.GetFullPath(outputRoot);
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("Scoop output already exists. Choose a new output directory.");
        var root = Path.GetFullPath(assetRoot);
        var checksums = ReadChecksums(checksumsPath);
        VerifyChecksum(manifestPath, checksums);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (manifest.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported release manifest schema version.");
        var assets = JsonSerializer.Deserialize(manifest.RootElement.GetProperty("assetEntries").GetRawText(),
            ReleaseCatalogJsonContext.Default.PowerForgeReleaseAssetEntryArray) ?? [];
        var entries = new List<(string Architecture, string Url, string Hash, string UpdateUrl)>();
        string? version = null;
        foreach (var runtime in package.Runtimes)
        {
            var matches = assets.Where(asset => asset.Category == PowerForgeReleaseAssetCategory.Tool &&
                string.Equals(asset.Target, package.Target, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(asset.Runtime, runtime, StringComparison.OrdinalIgnoreCase) &&
                FileName(asset.RelativeStagePath ?? asset.StagedPath ?? asset.Path).EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Scoop runtime '{runtime}' must select exactly one portable ZIP.");
            var asset = matches[0];
            if (string.IsNullOrEmpty(asset.Version) || !Regex.IsMatch(asset.Version, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant) ||
                (version is not null && version != asset.Version))
                throw new InvalidOperationException("Scoop archives must have one matching three-part release version.");
            version = asset.Version;
            var name = FileName(asset.RelativeStagePath ?? asset.StagedPath ?? asset.Path);
            var path = Path.Combine(root, name);
            VerifyChecksum(path, checksums);
            using (var zip = ZipFile.OpenRead(path))
            {
                if (zip.GetEntry(package.Executable.Replace('\\', '/')) is not { Length: > 0 })
                    throw new InvalidOperationException($"Scoop executable '{package.Executable}' is missing from '{name}'.");
            }
            var updateUrl = package.UrlTemplate.Replace("{Runtime}", runtime);
            var url = updateUrl.Replace("$version", version);
            ValidateHttps(url);
            if (url.Contains("{") || url.Contains("$") || Uri.UnescapeDataString(new Uri(url).AbsolutePath.Split('/').Last()) != name)
                throw new InvalidOperationException("Scoop download URL must resolve to the selected archive filename.");
            entries.Add((Architecture(runtime), url, DotNetPublishReleaseArtifactVerifier.ComputeSha256(path).ToLowerInvariant(), updateUrl));
        }
        var gitHub = spec.GitHub!;
        var tagTemplate = gitHub.TagTemplate!;
        var tagRegex = Regex.Escape(tagTemplate).Replace(@"\{Version}", @"([0-9]+\.[0-9]+\.[0-9]+)");
        // Filtering before version matching prevents a library release or prerelease in a shared repository from becoming an app update.
        var tagFilter = Regex.Escape(tagTemplate).Replace(@"\{Version}", @"[0-9]+\.[0-9]+\.[0-9]+");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("version", version);
            writer.WriteString("description", package.Description);
            writer.WriteString("homepage", package.Homepage);
            writer.WriteString("license", package.License);
            writer.WriteStartObject("architecture");
            foreach (var entry in entries)
            {
                writer.WriteStartObject(entry.Architecture);
                writer.WriteString("url", entry.Url);
                writer.WriteString("hash", entry.Hash);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteStartArray("shortcuts");
            writer.WriteStartArray();
            writer.WriteStringValue(package.Executable);
            writer.WriteStringValue(package.ShortcutName);
            writer.WriteEndArray();
            writer.WriteEndArray();
            writer.WriteStartObject("checkver");
            writer.WriteString("url", $"https://api.github.com/repos/{gitHub.Owner}/{gitHub.Repository}/releases?per_page=100");
            writer.WriteString("jsonpath", $"$[?(@.draft == false && @.prerelease == false && @.tag_name =~ /^{tagFilter}$/)].tag_name");
            writer.WriteString("regex", tagRegex);
            writer.WriteEndObject();
            writer.WriteStartObject("autoupdate");
            writer.WriteStartObject("architecture");
            foreach (var entry in entries)
            {
                writer.WriteStartObject(entry.Architecture);
                writer.WriteString("url", entry.UpdateUrl);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        Directory.CreateDirectory(output);
        var manifestOutput = Path.Combine(output, package.Name + ".json");
        File.WriteAllText(manifestOutput, Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
        return manifestOutput;
    }

    private static void Validate(PowerForgeReleaseScoopOptions package, PowerForgeReleaseGitHubOptions? gitHub)
    {
        if (!Regex.IsMatch(package.Name, "^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant) ||
            string.IsNullOrWhiteSpace(package.Description) || string.IsNullOrWhiteSpace(package.License) ||
            string.IsNullOrWhiteSpace(package.Target) || string.IsNullOrWhiteSpace(package.ShortcutName))
            throw new InvalidOperationException("Complete Scoop package identity, description and target are required.");
        ValidateHttps(package.Homepage);
        if (string.IsNullOrWhiteSpace(package.Executable) || Path.IsPathRooted(package.Executable) ||
            package.Executable.Replace('\\', '/').Split('/').Any(part => part is "" or "." or ".." || part.Contains(':')))
            throw new InvalidOperationException("Scoop executable must be a relative archive path.");
        if (package.Runtimes.Length == 0 || package.Runtimes.Distinct(StringComparer.Ordinal).Count() != package.Runtimes.Length)
            throw new InvalidOperationException("Scoop requires distinct Windows runtimes.");
        foreach (var runtime in package.Runtimes) Architecture(runtime);
        if (!package.UrlTemplate.Contains("$version") || !package.UrlTemplate.Contains("{Runtime}"))
            throw new InvalidOperationException("Scoop UrlTemplate requires $version and {Runtime} tokens.");
        if (gitHub is null || !Regex.IsMatch(gitHub.Owner ?? "", "^[A-Za-z0-9_-]+$") ||
            !Regex.IsMatch(gitHub.Repository ?? "", "^[A-Za-z0-9_.-]+$") ||
            string.IsNullOrEmpty(gitHub.TagTemplate) || gitHub.TagTemplate!.Split(["{Version}"], StringSplitOptions.None).Length != 2 ||
            !Regex.IsMatch(gitHub.TagTemplate!.Replace("{Version}", ""), "^[A-Za-z0-9_.-]*$"))
            throw new InvalidOperationException("Scoop update detection requires GitHub identity and one {Version} token in its tag template.");
    }

    private static string Architecture(string runtime) => runtime switch
    {
        "win-x64" => "64bit", "win-arm64" => "arm64", "win-x86" => "32bit",
        _ => throw new InvalidOperationException("Unsupported Scoop runtime: " + runtime)
    };

    private static void ValidateHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Scoop URLs must be absolute HTTPS URLs without credentials.");
    }

}
