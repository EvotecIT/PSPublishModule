using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static readonly Regex GeneratedApiTypeSlugRegex = new(
        "^[a-z0-9][a-z0-9-]*$",
        RegexOptions.CultureInvariant);

    private static HashSet<string> ReadExistingApiTypeSlugs(string outputPath)
    {
        var indexPath = Path.Combine(outputPath, "index.json");
        if (!File.Exists(indexPath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(indexPath));
            if (!document.RootElement.TryGetProperty("types", out var types) ||
                types.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return types
                .EnumerateArray()
                .Where(static type => type.ValueKind == JsonValueKind.Object)
                .Select(static type =>
                    type.TryGetProperty("slug", out var slug) && slug.ValueKind == JsonValueKind.String
                        ? slug.GetString()
                        : null)
                .Where(static slug => IsSafeGeneratedApiTypeSlug(slug))
                .Select(static slug => slug!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void RemoveStaleApiTypeArtifacts(
        string outputPath,
        IReadOnlySet<string> previousTypeSlugs,
        IReadOnlyList<ApiTypeModel> currentTypes)
    {
        if (previousTypeSlugs.Count == 0)
            return;

        var currentTypeSlugs = currentTypes
            .Select(static type => type.Slug)
            .Where(static slug => IsSafeGeneratedApiTypeSlug(slug))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var typesPath = Path.Combine(outputPath, "types");

        foreach (var staleSlug in previousTypeSlugs.Where(slug => !currentTypeSlugs.Contains(slug)))
        {
            DeleteGeneratedApiFile(Path.Combine(typesPath, staleSlug + ".json"));
            DeleteGeneratedApiFile(Path.Combine(typesPath, staleSlug + ".html"));
            DeleteGeneratedApiFile(Path.Combine(outputPath, staleSlug + ".html"));

            var routePath = Path.Combine(outputPath, staleSlug);
            if (Directory.Exists(routePath))
                Directory.Delete(routePath, recursive: true);
        }
    }

    private static bool IsSafeGeneratedApiTypeSlug(string? slug) =>
        !string.IsNullOrWhiteSpace(slug) &&
        GeneratedApiTypeSlugRegex.IsMatch(slug);

    private static void DeleteGeneratedApiFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
