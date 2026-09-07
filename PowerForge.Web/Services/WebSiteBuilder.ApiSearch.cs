using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static void AppendApiSearchEntries(SiteSpec spec, string outputRoot, List<SearchIndexEntry> entries)
    {
        var root = Path.GetFullPath(outputRoot);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var seen = entries.Select(entry => entry.Url).ToHashSet(StringComparer.Ordinal);
        foreach (var apiRoot in spec.Search?.ApiRoots ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(apiRoot) || Path.IsPathRooted(apiRoot))
                throw new ArgumentException("Search.ApiRoots must contain output-relative directories.");
            var directory = Path.GetFullPath(Path.Combine(root, apiRoot));
            if (!directory.StartsWith(rootPrefix, comparison))
                throw new ArgumentException("Search.ApiRoots must stay within the site output directory.");
            if (!Directory.Exists(directory)) continue;

            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var catalogPath in Directory.EnumerateFiles(directory, "search.json", options).Order(StringComparer.Ordinal))
            {
                using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
                if (catalog.RootElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"API search catalog must be an array: {catalogPath}");
                var catalogDirectory = Path.GetDirectoryName(catalogPath)!;
                var catalogRoute = "/" + Path.GetRelativePath(root, catalogDirectory).Replace('\\', '/').Trim('/') + "/";
                foreach (var item in catalog.RootElement.EnumerateArray())
                {
                    var slug = ApiSearchString(item, "slug");
                    if (!Regex.IsMatch(slug, "^[A-Za-z0-9_-]+$")) continue;
                    // Generated catalogs link JSON payloads; visible search must link the HTML reference.
                    var route = File.Exists(Path.Combine(catalogDirectory, slug, "index.html"))
                        ? catalogRoute + slug + "/"
                        : File.Exists(Path.Combine(catalogDirectory, slug + ".html")) ? catalogRoute + slug + ".html" : null;
                    if (route is null || !seen.Add(route)) continue;
                    var title = ApiSearchString(item, "title");
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    var summary = WebApiDocsGenerator.StripCrefTokens(ApiSearchString(item, "summary"));
                    var kind = ApiSearchString(item, "kind");
                    var aliasNames = item.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array
                        ? aliases.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray()
                        : Array.Empty<string>();
                    entries.Add(new SearchIndexEntry
                    {
                        Title = title,
                        Aliases = aliasNames,
                        Url = route,
                        Description = summary.Length > 400 ? summary[..400] : summary,
                        Collection = kind.Equals("Cmdlet", StringComparison.OrdinalIgnoreCase) ? "powershell" : "api",
                        Kind = kind.ToLowerInvariant(),
                        Weight = 1,
                        Language = NormalizeLanguageToken(spec.Localization?.DefaultLanguage ?? "en"),
                        SearchText = string.Join(' ', title, string.Join(' ', aliasNames), summary, ApiSearchString(item, "namespace")),
                        Tags = new[] { ApiSearchString(item, "namespace") }.Where(value => value.Length > 0).ToArray()
                    });
                }
            }
        }
    }

    private static string ApiSearchString(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
