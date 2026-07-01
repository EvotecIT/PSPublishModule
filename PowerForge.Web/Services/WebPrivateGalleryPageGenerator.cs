using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using PowerForge;

namespace PowerForge.Web;

/// <summary>
/// Generates static Markdown pages for private PowerShell gallery modules.
/// </summary>
public static class WebPrivateGalleryPageGenerator
{
    /// <summary>Generates module index pages and optional module documentation pages.</summary>
    public static WebPrivateGalleryPageResult Generate(WebPrivateGalleryPageOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.PrivateGalleryPath))
            throw new ArgumentException("PrivateGalleryPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("OutputDirectory is required.", nameof(options));

        var warnings = new List<string>();
        var baseDir = ResolveBaseDirectory(options.BaseDirectory);
        var galleryPath = ResolvePath(options.PrivateGalleryPath, baseDir);
        var outputDirectory = ResolvePath(options.OutputDirectory, baseDir);
        var portalDocsPath = string.IsNullOrWhiteSpace(options.PortalDocsPath)
            ? null
            : ResolvePath(options.PortalDocsPath!, baseDir);

        if (!File.Exists(galleryPath))
            throw new FileNotFoundException("Private gallery feed JSON was not found.", galleryPath);

        var gallery = ReadJson<PrivateGalleryDocument>(galleryPath)
            ?? throw new InvalidOperationException($"Private gallery feed JSON '{galleryPath}' could not be read.");
        var portalDocs = TryReadPortalDocs(portalDocsPath, warnings);

        ResetDirectory(outputDirectory);
        var modulePages = 0;
        var documentPages = 0;
        var profileName = FirstNonEmpty(options.ProfileName, gallery.Feed.RepositoryName, "CompanyPowerShellGallery")!;
        var repositoryName = FirstNonEmpty(options.RepositoryName, gallery.Feed.RepositoryName, profileName)!;
        var packages = gallery.Packages
            .Where(static package => !string.IsNullOrWhiteSpace(package.Name))
            .OrderBy(static package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var packageSlugs = BuildPackageSlugs(packages);
        var usedDocPageSlugs = new HashSet<string>(packageSlugs.Values, StringComparer.OrdinalIgnoreCase)
        {
            "index"
        };
        WriteGalleryIndexPage(outputDirectory, gallery, packages, packageSlugs, profileName, repositoryName, options);

        foreach (var package in packages)
        {
            var moduleSlug = packageSlugs[package];
            var moduleDocs = FindPortalDocsForPackage(portalDocs, package)
                .OrderBy(static doc => doc.Order)
                .ThenBy(static doc => doc.NavigationGroup, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static doc => doc.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var packageDocs = ResolveModule(package)?.Documents ?? new List<PrivateGalleryDocumentAsset>();
            var docLinks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (options.GenerateDocumentPages)
            {
                foreach (var doc in moduleDocs.Where(static doc => !string.IsNullOrWhiteSpace(doc.Content)))
                {
                    var pageSlug = MakeUniqueSlug(
                        $"{moduleSlug}-{Slugify(FirstNonEmpty(doc.Title, Path.GetFileNameWithoutExtension(doc.Path), doc.Id, "document")!)}",
                        $"{doc.Id}|{doc.SourceId}|{doc.Path}|{doc.Title}",
                        usedDocPageSlugs);
                    var relativePath = WriteDocumentPage(outputDirectory, package, doc, moduleSlug, pageSlug, options);
                    docLinks[doc.Id] = relativePath;
                    documentPages++;
                }
            }

            WriteModulePage(outputDirectory, package, moduleSlug, moduleDocs, packageDocs, docLinks, profileName, repositoryName, options);
            modulePages++;
        }

        return new WebPrivateGalleryPageResult
        {
            OutputDirectory = outputDirectory,
            ModulePageCount = modulePages,
            DocumentPageCount = documentPages,
            Warnings = warnings.ToArray()
        };
    }

    private static void WriteGalleryIndexPage(
        string outputDirectory,
        PrivateGalleryDocument gallery,
        IReadOnlyList<PrivateGalleryPackage> packages,
        IReadOnlyDictionary<PrivateGalleryPackage, string> packageSlugs,
        string profileName,
        string repositoryName,
        WebPrivateGalleryPageOptions options)
    {
        var sb = new StringBuilder();
        AppendFrontMatter(sb, "Modules", gallery.Title, ResolvePageLayout(options.IndexLayout, options.Layout), new Dictionary<string, string?>
        {
            ["profile"] = profileName,
            ["pageKind"] = "module-catalog",
            ["feed"] = gallery.Feed.Name,
            ["moduleCount"] = packages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["repositoryName"] = repositoryName,
            ["organization"] = gallery.Feed.Organization,
            ["project"] = gallery.Feed.Project
        });
        sb.AppendLine("# Modules");
        sb.AppendLine();
        sb.AppendLine($"Private gallery modules generated from `{gallery.Feed.Name}`.");
        sb.AppendLine();
        sb.AppendLine("## Initialize once");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine($"Initialize-ManagedModuleRepository -ProfileName {QuotePowerShell(profileName)} -Organization {QuotePowerShell(gallery.Feed.Organization ?? "<organization>")} -Project {QuotePowerShell(gallery.Feed.Project ?? "<project>")} -Feed {QuotePowerShell(gallery.Feed.Name)} -InstallPrerequisites");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Available modules");
        sb.AppendLine();
        sb.AppendLine("| Module | Version | Commands | Docs |");
        sb.AppendLine("| --- | --- | ---: | ---: |");
        foreach (var package in packages)
        {
            var module = ResolveModule(package);
            var version = FirstNonEmpty(package.LatestVersion, module?.Version, "latest")!;
            var commandCount = module?.Commands.Count ?? 0;
            var documentCount = module?.Documents.Count ?? 0;
            sb.AppendLine($"| [{EscapeMarkdownCell(package.Name)}]({packageSlugs[package]}/) | {EscapeMarkdownCell(version)} | {commandCount} | {documentCount} |");
        }
        sb.AppendLine();

        File.WriteAllText(Path.Combine(outputDirectory, "index.md"), sb.ToString(), Encoding.UTF8);
    }

    private static void WriteModulePage(
        string outputDirectory,
        PrivateGalleryPackage package,
        string slug,
        IReadOnlyList<WebPortalDocEntry> portalDocs,
        IReadOnlyList<PrivateGalleryDocumentAsset> packageDocs,
        IReadOnlyDictionary<string, string> docLinks,
        string profileName,
        string repositoryName,
        WebPrivateGalleryPageOptions options)
    {
        var module = ResolveModule(package);
        var moduleDir = Path.Combine(outputDirectory, slug);
        Directory.CreateDirectory(moduleDir);
        var path = Path.Combine(moduleDir, "index.md");
        var version = FirstNonEmpty(package.LatestVersion, module?.Version, "latest")!;
        var description = FirstNonEmpty(package.Description, module?.Description, $"Private gallery package {package.Name}.")!;
        var commands = module?.Commands ?? new List<PrivateGalleryCommandMetadata>();
        var dependencies = module?.RequiredModules ?? new List<PrivateGalleryDependency>();
        if (dependencies.Count == 0)
            dependencies = package.Versions.SelectMany(static version => version.Dependencies).ToList();

        var install = $"Install-ManagedModule -ProfileName {QuotePowerShell(profileName)} -Name {QuotePowerShell(package.Name)}";
        var update = $"Update-ManagedModule -ProfileName {QuotePowerShell(profileName)} -Name {QuotePowerShell(package.Name)}";
        var nativeInstall = $"Install-PSResource -Name {QuotePowerShell(package.Name)} -Repository {QuotePowerShell(repositoryName)} -TrustRepository";

        var sb = new StringBuilder();
        AppendFrontMatter(sb, package.Name, description, ResolvePageLayout(options.ModuleLayout, options.Layout), new Dictionary<string, string?>
        {
            ["pageKind"] = "module-detail",
            ["module"] = package.Name,
            ["version"] = version,
            ["commandCount"] = commands.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["packageDocCount"] = packageDocs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["portalDocCount"] = portalDocs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["dependencyCount"] = dependencies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

        sb.AppendLine($"# {package.Name}");
        sb.AppendLine();
        sb.AppendLine(description);
        sb.AppendLine();
        sb.AppendLine("## Install");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine(install);
        sb.AppendLine(update);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Native PSResourceGet command:");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine(nativeInstall);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Authentication is handled by the registered repository and Azure Artifacts credential provider. If the cached session is missing or expired, the provider should prompt and cache the result.");
        sb.AppendLine();
        AppendModuleFacts(sb, package, module, version);
        AppendCommands(sb, commands);
        AppendDocuments(sb, "Portal and repository docs", portalDocs, docLinks);
        AppendPackageDocs(sb, packageDocs);
        AppendDependencies(sb, dependencies);
        AppendVersions(sb, package.Versions);
        AppendWarnings(sb, package.Warnings.Concat(module?.Warnings ?? Enumerable.Empty<string>()));

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string WriteDocumentPage(
        string outputDirectory,
        PrivateGalleryPackage package,
        WebPortalDocEntry doc,
        string moduleSlug,
        string pageSlug,
        WebPrivateGalleryPageOptions options)
    {
        var docDir = Path.Combine(outputDirectory, pageSlug);
        Directory.CreateDirectory(docDir);
        var path = Path.Combine(docDir, "index.md");

        var sb = new StringBuilder();
        AppendFrontMatter(sb, doc.Title, doc.Summary, ResolvePageLayout(options.DocumentLayout, options.Layout), new Dictionary<string, string?>
        {
            ["pageKind"] = "module-document",
            ["module"] = package.Name,
            ["source"] = doc.SourceKind,
            ["kind"] = doc.Kind,
            ["sourceId"] = doc.SourceId,
            ["navigationGroup"] = doc.NavigationGroup
        });
        sb.AppendLine($"# {doc.Title}");
        sb.AppendLine();
        sb.AppendLine($"Module: [{package.Name}](../{moduleSlug}/)");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(doc.Url))
            sb.AppendLine($"Source: [{doc.Path}]({doc.Url})");
        else
            sb.AppendLine($"Source: `{doc.Path}`");
        sb.AppendLine();
        sb.AppendLine(NormalizeMarkdownBody(doc.Content!));

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return $"../{pageSlug}/";
    }

    private static void AppendModuleFacts(StringBuilder sb, PrivateGalleryPackage package, PrivateGalleryModuleMetadata? module, string version)
    {
        sb.AppendLine("## Overview");
        sb.AppendLine();
        sb.AppendLine("| Item | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Latest version | {EscapeMarkdownCell(version)} |");
        sb.AppendLine($"| Author | {EscapeMarkdownCell(FirstNonEmpty(module?.Author, package.Versions.FirstOrDefault()?.Author, "n/a")!)} |");
        sb.AppendLine($"| PowerShell | {EscapeMarkdownCell(FirstNonEmpty(module?.PowerShellVersion, "not specified")!)} |");
        sb.AppendLine($"| Editions | {EscapeMarkdownCell(module?.CompatiblePSEditions.Count > 0 ? string.Join(", ", module.CompatiblePSEditions) : "not specified")} |");
        sb.AppendLine($"| Commands | {module?.Commands.Count ?? 0} |");
        sb.AppendLine($"| Package docs | {module?.Documents.Count ?? 0} |");
        if (!string.IsNullOrWhiteSpace(package.WebUrl))
            sb.AppendLine($"| Feed link | [Azure Artifacts]({package.WebUrl}) |");
        sb.AppendLine();
    }

    private static void AppendCommands(StringBuilder sb, IReadOnlyList<PrivateGalleryCommandMetadata> commands)
    {
        sb.AppendLine("## Commands");
        sb.AppendLine();
        if (commands.Count == 0)
        {
            sb.AppendLine("No commands were discovered from package metadata yet.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Command | Type | Synopsis |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var command in commands.OrderBy(static command => command.Name, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"| `{EscapeMarkdownCell(command.Name)}` | {EscapeMarkdownCell(command.Kind ?? "Command")} | {EscapeMarkdownCell(command.Synopsis ?? string.Empty)} |");
        sb.AppendLine();
    }

    private static void AppendDocuments(
        StringBuilder sb,
        string title,
        IReadOnlyList<WebPortalDocEntry> docs,
        IReadOnlyDictionary<string, string> docLinks)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (docs.Count == 0)
        {
            sb.AppendLine("No portal or repository documents are attached to this module yet.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Document | Kind | Source | Notes |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var doc in docs)
        {
            var titleText = EscapeMarkdownCell(doc.Title);
            var link = docLinks.TryGetValue(doc.Id, out var relative) ? $"[{titleText}]({relative})" :
                !string.IsNullOrWhiteSpace(doc.Url) ? $"[{titleText}]({doc.Url})" : titleText;
            var notes = FirstNonEmpty(doc.Summary, doc.NavigationGroup, doc.Path, string.Empty)!;
            sb.AppendLine($"| {link} | {EscapeMarkdownCell(doc.Kind)} | {EscapeMarkdownCell(doc.SourceKind)} | {EscapeMarkdownCell(notes)} |");
        }
        sb.AppendLine();
    }

    private static void AppendPackageDocs(StringBuilder sb, IReadOnlyList<PrivateGalleryDocumentAsset> docs)
    {
        sb.AppendLine("## Package assets");
        sb.AppendLine();
        if (docs.Count == 0)
        {
            sb.AppendLine("No document assets were discovered inside the package metadata.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Asset | Kind | Size |");
        sb.AppendLine("| --- | --- | ---: |");
        foreach (var doc in docs.OrderBy(static doc => doc.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(static doc => doc.Path, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"| `{EscapeMarkdownCell(doc.Path)}` | {EscapeMarkdownCell(doc.Kind)} | {doc.Size} |");
        sb.AppendLine();
    }

    private static void AppendDependencies(StringBuilder sb, IReadOnlyList<PrivateGalleryDependency> dependencies)
    {
        sb.AppendLine("## Dependencies");
        sb.AppendLine();
        if (dependencies.Count == 0)
        {
            sb.AppendLine("No required modules were discovered.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Module | Version |");
        sb.AppendLine("| --- | --- |");
        foreach (var dependency in dependencies.GroupBy(static dependency => dependency.Name, StringComparer.OrdinalIgnoreCase).Select(static group => group.First()).OrderBy(static dependency => dependency.Name, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"| {EscapeMarkdownCell(dependency.Name)} | {EscapeMarkdownCell(dependency.VersionRange ?? "not specified")} |");
        sb.AppendLine();
    }

    private static void AppendVersions(StringBuilder sb, IReadOnlyList<PrivateGalleryPackageVersion> versions)
    {
        sb.AppendLine("## Versions");
        sb.AppendLine();
        if (versions.Count == 0)
        {
            sb.AppendLine("No version metadata was discovered.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Version | Listed | Published |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var version in versions.OrderByDescending(static version => version.PublishedAtUtc ?? DateTimeOffset.MinValue))
        {
            var listed = version.IsListed is null ? "n/a" : (version.IsListed.Value ? "yes" : "no");
            var published = version.PublishedAtUtc?.ToString("yyyy-MM-dd") ?? "n/a";
            sb.AppendLine($"| {EscapeMarkdownCell(version.Version)} | {listed} | {published} |");
        }
        sb.AppendLine();
    }

    private static void AppendWarnings(StringBuilder sb, IEnumerable<string> warnings)
    {
        var items = warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0)
            return;

        sb.AppendLine("## Indexing warnings");
        sb.AppendLine();
        foreach (var warning in items)
            sb.AppendLine($"- {warning}");
        sb.AppendLine();
    }

    private static IReadOnlyList<WebPortalDocEntry> FindPortalDocsForPackage(WebPortalDocsDocument? docs, PrivateGalleryPackage package)
    {
        if (docs?.Documents is null || docs.Documents.Count == 0)
            return Array.Empty<WebPortalDocEntry>();

        return docs.Documents
            .Where(doc =>
                Matches(doc.Module, package.Name) ||
                Matches(doc.Package, package.Name) ||
                Matches(doc.Module, package.Module?.Name))
            .ToList();
    }

    private static PrivateGalleryModuleMetadata? ResolveModule(PrivateGalleryPackage package)
        => package.Module ?? package.Versions.FirstOrDefault(static version => version.Module is not null)?.Module;

    private static Dictionary<PrivateGalleryPackage, string> BuildPackageSlugs(IReadOnlyList<PrivateGalleryPackage> packages)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "index"
        };
        var slugs = new Dictionary<PrivateGalleryPackage, string>();
        foreach (var package in packages)
            slugs[package] = MakeUniqueSlug(Slugify(package.Name), package.Name, used);
        return slugs;
    }

    private static string MakeUniqueSlug(string slug, string uniquenessKey, HashSet<string> used)
    {
        var normalized = string.IsNullOrWhiteSpace(slug) ? "item" : slug;
        if (used.Add(normalized))
            return normalized;

        var hash = ShortHash(uniquenessKey);
        var withHash = $"{normalized}-{hash}";
        var suffix = 2;
        while (!used.Add(withHash))
        {
            withHash = $"{normalized}-{hash}-{suffix}";
            suffix++;
        }
        return withHash;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).Substring(0, 8).ToLowerInvariant();
    }

    private static WebPortalDocsDocument? TryReadPortalDocs(string? path, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return ReadJson<WebPortalDocsDocument>(path);
        }
        catch (Exception ex)
        {
            warnings.Add($"Portal docs JSON could not be read: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static T? ReadJson<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, WebJson.Options);
    }

    private static void AppendFrontMatter(StringBuilder sb, string? title, string? description, string? layout, IReadOnlyDictionary<string, string?> extra)
    {
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{EscapeYaml(title ?? "Module")}\"");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"description: \"{EscapeYaml(description)}\"");
        if (!string.IsNullOrWhiteSpace(layout))
            sb.AppendLine($"layout: {layout}");
        foreach (var item in extra)
        {
            if (!string.IsNullOrWhiteSpace(item.Value))
                sb.AppendLine($"meta.{item.Key}: \"{EscapeYaml(item.Value)}\"");
        }
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static string NormalizeMarkdownBody(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return normalized + Environment.NewLine;

        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        return end < 0
            ? normalized + Environment.NewLine
            : normalized[(end + 5)..].TrimStart() + Environment.NewLine;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? ResolvePageLayout(params string?[] layouts)
        => FirstNonEmpty(layouts);

    private static bool Matches(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string ResolveBaseDirectory(string? baseDirectory)
        => string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory);

    private static string ResolvePath(string path, string baseDirectory)
        => Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
    }

    private static string QuotePowerShell(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string EscapeYaml(string? value)
        => (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string EscapeMarkdownCell(string? value)
        => (value ?? string.Empty)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "item";

        var sb = new StringBuilder(value.Length);
        var previousDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            var allowed = (ch is >= 'a' and <= 'z') || (ch is >= '0' and <= '9');
            if (allowed)
            {
                sb.Append(ch);
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                sb.Append('-');
                previousDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }
}
