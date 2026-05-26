using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForge;

namespace PowerForge.Web;

/// <summary>
/// Maps private gallery package data into the generic PowerForge project catalog contract.
/// </summary>
public static class WebPrivateGalleryProjectCatalogMapper
{
    /// <summary>
    /// Writes or merges a project catalog document from a private gallery document.
    /// </summary>
    /// <param name="document">Private gallery document to project.</param>
    /// <param name="path">Project catalog path.</param>
    /// <param name="options">Private gallery generation options.</param>
    /// <returns>Number of project entries written from private gallery packages.</returns>
    public static int WriteProjectCatalog(PrivateGalleryDocument document, string path, WebPrivateGalleryOptions options)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        if (options is null) throw new ArgumentNullException(nameof(options));

        var catalog = LoadCatalog(path, options.ProjectCatalogMerge);
        var projects = EnsureProjectsArray(catalog);
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = document.Packages
            .Where(static package => !string.IsNullOrWhiteSpace(package.Name))
            .OrderBy(static package => package.Name, StringComparer.OrdinalIgnoreCase)
            .Select(package =>
            {
                var displayName = ResolveDisplayName(package);
                var slug = ResolveUniqueSlug(Slugify(displayName), usedSlugs);
                return BuildProjectEntry(document, package, options, slug, displayName);
            })
            .ToList();

        var slugs = entries
            .Select(entry => entry["slug"]?.GetValue<string>())
            .Where(static slug => !string.IsNullOrWhiteSpace(slug))
            .Select(static slug => slug!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RemoveExistingPrivateGalleryEntries(projects, document, slugs);

        foreach (var entry in entries)
            projects.Add(entry);

        catalog["generatedOn"] = DateTimeOffset.UtcNow.ToString("O");
        if (catalog["source"] is null)
            catalog["source"] = "powerforge.private-gallery";
        catalog["privateGallery"] = BuildFeedSummary(document);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, catalog.ToJsonString(WebJson.Options));

        return entries.Count;
    }

    private static JsonObject LoadCatalog(string path, bool merge)
    {
        if (!merge || !File.Exists(path))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ??
                   throw new InvalidOperationException($"Cannot merge private gallery into non-object project catalog JSON: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Cannot merge private gallery into invalid project catalog JSON: {path}", ex);
        }
    }

    private static JsonArray EnsureProjectsArray(JsonObject catalog)
    {
        if (catalog["projects"] is JsonArray projects)
            return projects;

        if (catalog["projects"] is not null)
            throw new InvalidOperationException("Project catalog 'projects' value must be an array.");

        projects = new JsonArray();
        catalog["projects"] = projects;
        return projects;
    }

    private static void RemoveExistingPrivateGalleryEntries(JsonArray projects, PrivateGalleryDocument document, ISet<string> slugs)
    {
        for (var index = projects.Count - 1; index >= 0; index--)
        {
            if (projects[index] is not JsonObject existing)
                continue;

            var existingSlug = existing["slug"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(existingSlug) && slugs.Contains(existingSlug))
            {
                projects.RemoveAt(index);
                continue;
            }

            if (IsPrivateGalleryEntryForFeed(existing, document))
                projects.RemoveAt(index);
        }
    }

    private static bool IsPrivateGalleryEntryForFeed(JsonObject entry, PrivateGalleryDocument document)
    {
        if (entry["privateGallery"] is not JsonObject privateGallery)
            return false;

        return EqualsJsonString(privateGallery, "provider", document.Provider.ToString()) &&
               EqualsJsonString(privateGallery, "organization", document.Feed.Organization) &&
               EqualsJsonString(privateGallery, "project", document.Feed.Project) &&
               EqualsJsonString(privateGallery, "feed", document.Feed.Name);
    }

    private static bool EqualsJsonString(JsonObject json, string name, string? expected)
        => string.Equals(json[name]?.GetValue<string>() ?? string.Empty, expected ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static JsonObject BuildProjectEntry(PrivateGalleryDocument document, PrivateGalleryPackage package, WebPrivateGalleryOptions options, string slug, string displayName)
    {
        var module = package.Module;
        var latest = ResolveLatestVersion(package);
        var latestModule = latest?.Module ?? module;
        var version = latestModule?.Version ?? latest?.Version ?? package.LatestVersion;
        var description = FirstNonEmpty(latestModule?.Description, module?.Description, latest?.Description, package.Description, $"{displayName} private gallery module.");
        var routePrefix = NormalizeRoutePrefix(options.ProjectCatalogRoutePrefix);
        var contentMode = NormalizeContentMode(options.ProjectCatalogContentMode);
        var packageUrl = package.WebUrl;
        if (contentMode.Equals("external", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(packageUrl))
            contentMode = "hybrid";
        var commandCount = CountCommands(package);
        var documentCount = CountDocuments(package);
        var dependencyCount = CountDependencies(package);
        var downloadCount = package.Metrics?.DownloadCount;
        if (!downloadCount.HasValue)
        {
            var versionDownloads = package.Versions
                .Select(static version => version.Metrics?.DownloadCount)
                .Where(static value => value.HasValue)
                .Select(static value => value!.Value)
                .ToList();
            if (versionDownloads.Count > 0)
                downloadCount = versionDownloads.Sum();
        }

        var links = new JsonObject();
        if (!string.IsNullOrWhiteSpace(packageUrl))
        {
            links["privateGallery"] = packageUrl;
            links["downloads"] = packageUrl;
        }

        links["docs"] = $"{routePrefix}/{slug}/docs/";
        links["apiPowerShell"] = $"{routePrefix}/{slug}/api/";

        var surfaces = new JsonObject
        {
            ["docs"] = documentCount > 0,
            ["apiPowerShell"] = commandCount > 0,
            ["examples"] = HasDocumentKind(package, "example"),
            ["changelog"] = HasDocumentKind(package, "changelog"),
            ["releases"] = package.Versions.Count > 0,
            ["downloads"] = true
        };

        var metrics = new JsonObject();
        if (downloadCount.HasValue)
        {
            metrics["downloads"] = new JsonObject
            {
                ["total"] = downloadCount.Value
            };
        }

        var privateGallery = new JsonObject
        {
            ["provider"] = document.Provider.ToString(),
            ["organization"] = document.Feed.Organization,
            ["project"] = document.Feed.Project,
            ["feed"] = document.Feed.Name,
            ["repositoryName"] = document.Feed.RepositoryName,
            ["packageId"] = FirstNonEmpty(package.Id, package.Name),
            ["packageUrl"] = packageUrl,
            ["latestVersion"] = version,
            ["versionCount"] = package.Versions.Count,
            ["commandCount"] = commandCount,
            ["documentCount"] = documentCount,
            ["dependencyCount"] = dependencyCount,
            ["installCommand"] = BuildInstallCommand(document.Feed.RepositoryName, package.Name),
            ["updateCommand"] = BuildUpdateCommand(document.Feed.RepositoryName, package.Name)
        };

        var entry = new JsonObject
        {
            ["slug"] = slug,
            ["name"] = displayName,
            ["mode"] = "hub-full",
            ["contentMode"] = contentMode,
            ["hubPath"] = $"{routePrefix}/{slug}/",
            ["description"] = description,
            ["status"] = ResolveStatus(package, latest),
            ["listed"] = latest?.IsListed ?? true,
            ["version"] = version,
            ["links"] = links,
            ["surfaces"] = surfaces,
            ["metrics"] = metrics,
            ["privateGallery"] = privateGallery
        };

        if (contentMode.Equals("external", StringComparison.OrdinalIgnoreCase))
            entry["externalUrl"] = packageUrl;

        return entry;
    }

    private static string ResolveDisplayName(PrivateGalleryPackage package)
        => string.IsNullOrWhiteSpace(package.Module?.Name) ? package.Name : package.Module!.Name;

    private static string ResolveUniqueSlug(string slug, ISet<string> usedSlugs)
    {
        var baseSlug = string.IsNullOrWhiteSpace(slug) ? "module" : slug;
        var candidate = baseSlug;
        var suffix = 2;
        while (!usedSlugs.Add(candidate))
            candidate = $"{baseSlug}-{suffix++}";
        return candidate;
    }

    private static JsonObject BuildFeedSummary(PrivateGalleryDocument document)
    {
        return new JsonObject
        {
            ["provider"] = document.Provider.ToString(),
            ["organization"] = document.Feed.Organization,
            ["project"] = document.Feed.Project,
            ["feed"] = document.Feed.Name,
            ["repositoryName"] = document.Feed.RepositoryName,
            ["packageCount"] = document.Summary.PackageCount,
            ["versionCount"] = document.Summary.VersionCount,
            ["commandCount"] = document.Summary.CommandCount,
            ["documentCount"] = document.Summary.DocumentCount,
            ["totalDownloads"] = document.Summary.TotalDownloads
        };
    }

    private static PrivateGalleryPackageVersion? ResolveLatestVersion(PrivateGalleryPackage package)
    {
        return package.Versions.FirstOrDefault(static version => version.IsLatest) ??
               package.Versions.FirstOrDefault(version => !string.IsNullOrWhiteSpace(package.LatestVersion) &&
                                                          version.Version.Equals(package.LatestVersion, StringComparison.OrdinalIgnoreCase)) ??
               package.Versions.FirstOrDefault();
    }

    private static int CountCommands(PrivateGalleryPackage package)
    {
        return package.Versions
            .Select(static version => version.Module)
            .Append(package.Module)
            .Where(static module => module is not null)
            .SelectMany(static module => module!.Commands)
            .Select(static command => command.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static int CountDocuments(PrivateGalleryPackage package)
    {
        return package.Versions
            .Select(static version => version.Module)
            .Append(package.Module)
            .Where(static module => module is not null)
            .SelectMany(static module => module!.Documents)
            .Select(static doc => doc.Path)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static int CountDependencies(PrivateGalleryPackage package)
    {
        return package.Versions
            .SelectMany(static version => version.Dependencies)
            .Concat(package.Versions.Select(static version => version.Module).Append(package.Module).Where(static module => module is not null).SelectMany(static module => module!.RequiredModules))
            .Select(static dependency => dependency.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static bool HasDocumentKind(PrivateGalleryPackage package, string kind)
    {
        return package.Versions
            .Select(static version => version.Module)
            .Append(package.Module)
            .Where(static module => module is not null)
            .SelectMany(static module => module!.Documents)
            .Any(document => document.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildInstallCommand(string repositoryName, string packageName)
    {
        var repository = string.IsNullOrWhiteSpace(repositoryName) ? "<RepositoryName>" : repositoryName.Trim();
        return $"Install-PrivateModule -Repository {QuotePowerShell(repository)} -Name {QuotePowerShell(packageName)} -InstallPrerequisites";
    }

    private static string BuildUpdateCommand(string repositoryName, string packageName)
    {
        var repository = string.IsNullOrWhiteSpace(repositoryName) ? "<RepositoryName>" : repositoryName.Trim();
        return $"Update-PrivateModule -Repository {QuotePowerShell(repository)} -Name {QuotePowerShell(packageName)} -InstallPrerequisites";
    }

    private static string QuotePowerShell(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string ResolveStatus(PrivateGalleryPackage package, PrivateGalleryPackageVersion? latest)
    {
        if (latest?.IsDeleted == true)
            return "deprecated";
        if (latest?.IsListed == false)
            return "deprecated";
        return "active";
    }

    private static string NormalizeRoutePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/projects";
        var normalized = value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        return normalized.TrimEnd('/');
    }

    private static string NormalizeContentMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "hybrid";
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "hybrid" or "external" ? normalized : "hybrid";
    }

    private static string Slugify(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(static ch =>
        {
            if (ch is >= 'a' and <= 'z')
                return ch;
            if (ch is >= '0' and <= '9')
                return ch;
            return '-';
        }).ToArray();

        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }
}
