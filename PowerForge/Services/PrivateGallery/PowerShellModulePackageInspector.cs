using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Inspects PowerShell module metadata from a NuGet package without importing or executing the module.
/// </summary>
public sealed class PowerShellModulePackageInspector
{
    private const int MaxTextBytes = 1024 * 1024;
    private const int MaxEntries = 2000;
    private const long MaxPackageBytes = 250L * 1024L * 1024L;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Inspects a nupkg file and returns module metadata discovered from static files.
    /// </summary>
    /// <param name="packagePath">Path to the nupkg file.</param>
    /// <returns>Module metadata.</returns>
    public PrivateGalleryModuleMetadata Inspect(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));

        var fullPath = Path.GetFullPath(packagePath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Package file was not found: {fullPath}", fullPath);

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > MaxPackageBytes)
            throw new InvalidOperationException($"Package '{fullPath}' exceeds the private gallery inspection size limit.");

        var metadata = new PrivateGalleryModuleMetadata();
        using var archive = ZipFile.OpenRead(fullPath);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidOperationException($"Package '{fullPath}' exceeds the private gallery inspection file-count limit.");

        foreach (var entry in archive.Entries)
        {
            var normalizedPath = NormalizePackagePath(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                continue;

            if (!IsSafePackagePath(normalizedPath))
            {
                metadata.Warnings.Add($"Skipped unsafe package path '{entry.FullName}'.");
                continue;
            }

            AddDocumentAsset(metadata, normalizedPath, entry);
        }

        var manifestEntry = archive.Entries
            .Where(static entry => entry.FullName.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry.FullName.Count(ch => ch == '/' || ch == '\\'))
            .ThenBy(static entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (manifestEntry is not null)
            ApplyManifest(metadata, manifestEntry);
        else
            metadata.Warnings.Add("No PowerShell module manifest (.psd1) was found in the package.");

        foreach (var helpEntry in archive.Entries.Where(static entry => entry.FullName.EndsWith("-help.xml", StringComparison.OrdinalIgnoreCase)))
            ApplyHelpXml(metadata, helpEntry);

        metadata.Commands = metadata.Commands
            .Where(static command => !string.IsNullOrWhiteSpace(command.Name))
            .GroupBy(static command => command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        metadata.Documents = metadata.Documents
            .GroupBy(static document => document.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static document => document.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static document => document.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        metadata.RequiredModules = metadata.RequiredModules
            .GroupBy(static dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return metadata;
    }

    private static void ApplyManifest(PrivateGalleryModuleMetadata metadata, ZipArchiveEntry entry)
    {
        string content;
        try
        {
            content = ReadTextEntry(entry);
        }
        catch (Exception ex)
        {
            metadata.Warnings.Add($"Failed to read manifest '{entry.FullName}': {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var fallbackName = Path.GetFileNameWithoutExtension(entry.FullName);
        metadata.Name = fallbackName;

        if (ModuleManifestTextParser.TryGetQuotedStringValue(content, "RootModule", out var rootModule) &&
            !string.IsNullOrWhiteSpace(rootModule))
            metadata.Name = Path.GetFileNameWithoutExtension(rootModule);

        if (string.IsNullOrWhiteSpace(metadata.Name))
            metadata.Name = fallbackName;

        metadata.Version = ReadManifestString(content, "ModuleVersion");
        metadata.Description = ReadManifestString(content, "Description");
        metadata.Author = ReadManifestString(content, "Author");
        metadata.CompanyName = ReadManifestString(content, "CompanyName");
        metadata.PowerShellVersion = ReadManifestString(content, "PowerShellVersion");
        metadata.CompatiblePSEditions = ReadManifestArray(content, "CompatiblePSEditions");

        var commands = ReadManifestArray(content, "CmdletsToExport")
            .Select(name => new PrivateGalleryCommandMetadata { Name = name, Kind = "Cmdlet" })
            .Concat(ReadManifestArray(content, "FunctionsToExport").Select(name => new PrivateGalleryCommandMetadata { Name = name, Kind = "Function" }))
            .Where(static command => !string.IsNullOrWhiteSpace(command.Name) && command.Name != "*");
        metadata.Commands.AddRange(commands);

        if (ModuleManifestTextParser.TryGetRequiredModules(content, out var requiredModules) && requiredModules is not null)
        {
            foreach (var module in requiredModules)
            {
                metadata.RequiredModules.Add(new PrivateGalleryDependency
                {
                    Name = module.ModuleName,
                    VersionRange = module.RequiredVersion ?? module.ModuleVersion ?? module.MaximumVersion
                });
            }
        }

        if ((ModuleManifestTextParser.TryGetPsDataStringArrayValue(content, "Tags", out var tags) && tags is not null) ||
            TryReadSimpleStringArray(content, "Tags", out tags))
        {
            metadata.Tags = tags
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static void ApplyHelpXml(PrivateGalleryModuleMetadata metadata, ZipArchiveEntry entry)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true
            };
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            foreach (var commandElement in document.Descendants().Where(static element => element.Name.LocalName.Equals("command", StringComparison.OrdinalIgnoreCase)))
            {
                var name = commandElement
                    .Descendants()
                    .FirstOrDefault(static element => element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))
                    ?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var synopsis = commandElement
                    .Descendants()
                    .FirstOrDefault(static element => element.Name.LocalName.Equals("synopsis", StringComparison.OrdinalIgnoreCase))
                    ?.Value?.Trim();

                var existing = metadata.Commands.FirstOrDefault(command => command.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    metadata.Commands.Add(new PrivateGalleryCommandMetadata
                    {
                        Name = name!,
                        Kind = "Command",
                        Synopsis = string.IsNullOrWhiteSpace(synopsis) ? null : synopsis
                    });
                }
                else if (string.IsNullOrWhiteSpace(existing.Synopsis) && !string.IsNullOrWhiteSpace(synopsis))
                {
                    existing.Synopsis = synopsis;
                }
            }
        }
        catch (Exception ex)
        {
            metadata.Warnings.Add($"Failed to parse help XML '{entry.FullName}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? ReadManifestString(string content, string key)
        => ModuleManifestTextParser.TryGetQuotedStringValue(content, key, out var value) &&
           !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static List<string> ReadManifestArray(string content, string key)
        => ModuleManifestTextParser.TryGetStringArrayValue(content, key, out var values) && values is not null
            ? values.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

    private static bool TryReadSimpleStringArray(string content, string key, out string[] values)
    {
        values = Array.Empty<string>();
        var match = Regex.Match(
            content,
            $@"(?im)\b{Regex.Escape(key)}\s*=\s*@\((?<body>.*?)\)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            RegexTimeout);
        if (!match.Success)
            return false;

        values = Regex.Matches(
                match.Groups["body"].Value,
                @"'(?<single>(?:[^']|'')*)'|""(?<double>(?:[^""]|"""")*)""",
                RegexOptions.CultureInvariant,
                RegexTimeout)
            .Cast<Match>()
            .Select(static item => item.Groups["single"].Success ? item.Groups["single"].Value.Replace("''", "'") : item.Groups["double"].Value.Replace("\"\"", "\""))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        return values.Length > 0;
    }

    private static void AddDocumentAsset(PrivateGalleryModuleMetadata metadata, string normalizedPath, ZipArchiveEntry entry)
    {
        if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.Length == 0)
            return;

        var fileName = Path.GetFileName(normalizedPath);
        var lowerName = fileName.ToLowerInvariant();
        var lowerPath = normalizedPath.ToLowerInvariant();
        string? kind = null;

        if (lowerName == "readme.md" || lowerName == "readme.txt")
            kind = "readme";
        else if (lowerName.StartsWith("changelog", StringComparison.Ordinal) || lowerName.StartsWith("release-notes", StringComparison.Ordinal))
            kind = "changelog";
        else if (lowerName.StartsWith("license", StringComparison.Ordinal) || lowerName == "copying")
            kind = "license";
        else if (IsUnderDirectory(lowerPath, "docs") && IsTextDocument(lowerName))
            kind = "docs";
        else if (IsUnderDirectory(lowerPath, "examples") && IsExampleDocument(lowerName))
            kind = "example";
        else if (lowerName.EndsWith("-help.xml", StringComparison.Ordinal))
            kind = "help";

        if (kind is null)
            return;

        metadata.Documents.Add(new PrivateGalleryDocumentAsset
        {
            Path = normalizedPath,
            Kind = kind,
            Title = Path.GetFileNameWithoutExtension(fileName),
            Size = entry.Length
        });
    }

    private static bool IsTextDocument(string lowerName)
        => lowerName.EndsWith(".md", StringComparison.Ordinal) ||
           lowerName.EndsWith(".txt", StringComparison.Ordinal) ||
           lowerName.EndsWith(".html", StringComparison.Ordinal);

    private static bool IsUnderDirectory(string lowerPath, string directoryName)
        => lowerPath.StartsWith(directoryName + "/", StringComparison.Ordinal) ||
           lowerPath.Contains("/" + directoryName + "/", StringComparison.Ordinal);

    private static bool IsExampleDocument(string lowerName)
        => IsTextDocument(lowerName) ||
           lowerName.EndsWith(".ps1", StringComparison.Ordinal) ||
           lowerName.EndsWith(".psm1", StringComparison.Ordinal);

    private static string ReadTextEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxTextBytes)
            throw new InvalidOperationException("Text entry is too large to inspect.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string NormalizePackagePath(string path)
        => path.Replace('\\', '/').Trim('/');

    private static bool IsSafePackagePath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        if (normalizedPath.StartsWith("/", StringComparison.Ordinal) ||
            normalizedPath.Contains(":", StringComparison.Ordinal))
            return false;

        var parts = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.All(static part => part != "." && part != "..");
    }
}
