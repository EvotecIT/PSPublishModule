using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebLlmsGenerator
{
    private static List<PackageInfo> ResolvePackages(IEnumerable<string>? packageFiles)
    {
        var packages = new List<PackageInfo>();
        foreach (var packageFile in packageFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(packageFile))
                continue;

            var fullPath = Path.GetFullPath(packageFile);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Configured package manifest not found: {fullPath}", fullPath);

            var project = ReadProjectInfo(fullPath, requirePackageMetadata: true);
            var id = project.PackageId ?? project.Name ?? Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            packages.Add(new PackageInfo
            {
                Id = id,
                Version = project.Version,
                InstallCommand = CreateInstallCommand(id, project.IsPowerShellModule, project.IsDotNetTool),
                IsPowerShellModule = project.IsPowerShellModule,
                IsDotNetTool = project.IsDotNetTool,
                ToolCommandName = project.ToolCommandName
            });
        }

        return packages
            .GroupBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private static string? ResolveSuiteVersion(IReadOnlyList<PackageInfo> packages)
    {
        if (packages.Count == 0)
            return null;

        var versions = packages
            .Select(static package => package.Version)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packages.Any(static package => string.IsNullOrWhiteSpace(package.Version)))
            return "unknown";

        return versions.Length == 1 ? versions[0] : "varies by package";
    }

    private static ProjectInfo ReadProjectInfo(string? projectFile, bool requirePackageMetadata = false)
    {
        if (string.IsNullOrWhiteSpace(projectFile))
            return new ProjectInfo();

        var full = Path.GetFullPath(projectFile);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Configured LLMS project file not found: {full}", full);

        var content = File.ReadAllText(full);
        if (Path.GetExtension(full).Equals(".psd1", StringComparison.OrdinalIgnoreCase))
        {
            var manifestContent = RemovePowerShellComments(content);
            var moduleVersion = NormalizeEmpty(MatchPowerShellDataValue(manifestContent, "ModuleVersion"));
            var prerelease = NormalizeEmpty(MatchPowerShellDataValue(manifestContent, "Prerelease"));
            return new ProjectInfo
            {
                Name = Path.GetFileNameWithoutExtension(full),
                PackageId = Path.GetFileNameWithoutExtension(full),
                Version = CombineMsBuildVersion(moduleVersion, prerelease),
                Description = NormalizeEmpty(MatchPowerShellDataValue(manifestContent, "Description")),
                IsPowerShellModule = true
            };
        }

        var properties = ReadMsBuildProperties(full, requirePackageMetadata);
        var assemblyName = GetMsBuildProperty(properties, "AssemblyName", requirePackageMetadata);
        var rootNamespace = GetMsBuildProperty(properties, "RootNamespace", requirePackageMetadata);
        var description = GetMsBuildProperty(properties, "Description", requirePackageMetadata);
        var projectName = Path.GetFileNameWithoutExtension(full);
        if (!requirePackageMetadata)
        {
            return new ProjectInfo
            {
                Name = assemblyName ?? rootNamespace ?? projectName,
                Description = description
            };
        }

        var packageId = GetMsBuildProperty(properties, "PackageId", throwOnUnresolved: true);
        var packageVersion = GetMsBuildProperty(properties, "PackageVersion", throwOnUnresolved: true);
        var versionValue = GetMsBuildProperty(properties, "Version", throwOnUnresolved: true);
        var versionPrefix = GetMsBuildProperty(properties, "VersionPrefix", throwOnUnresolved: true);
        var versionSuffix = GetMsBuildProperty(properties, "VersionSuffix", throwOnUnresolved: true);
        var version = packageVersion ??
                      versionValue ??
                      CombineMsBuildVersion(versionPrefix, versionSuffix);
        var packAsTool = GetMsBuildProperty(properties, "PackAsTool", throwOnUnresolved: true);
        var toolCommandName = GetMsBuildProperty(properties, "ToolCommandName", throwOnUnresolved: true);

        packageId ??= assemblyName ?? projectName;

        return new ProjectInfo
        {
            Name = assemblyName ?? rootNamespace ?? projectName,
            PackageId = packageId,
            Version = version,
            Description = description,
            ToolCommandName = toolCommandName ?? assemblyName ?? rootNamespace ?? packageId,
            IsDotNetTool = string.Equals(
                packAsTool,
                "true",
                StringComparison.OrdinalIgnoreCase)
        };
    }

    private static MsBuildPropertySet ReadMsBuildProperties(string projectFile, bool requirePackageMetadata)
    {
        var properties = new MsBuildPropertySet();
        properties.Values["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(projectFile);

        var propsPath = FindNearestBuildFile(projectFile, "Directory.Build.props");
        if (propsPath is not null)
            ReadMsBuildPropertyFile(propsPath, properties, requirePackageMetadata);

        ReadMsBuildPropertyFile(projectFile, properties, requirePackageMetadata);
        if (requirePackageMetadata)
        {
            var targetsPath = FindNearestBuildFile(projectFile, "Directory.Build.targets");
            if (targetsPath is not null)
                EnsureNoPostProjectPackageMetadata(targetsPath);
        }
        return properties;
    }

    private static string? FindNearestBuildFile(string projectFile, string fileName)
    {
        var directory = Path.GetDirectoryName(projectFile);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static void EnsureNoPostProjectPackageMetadata(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path, LoadOptions.None);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            throw new InvalidDataException($"Configured LLMS project metadata is invalid: {path}", ex);
        }

        var metadataName = document.Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .Select(element => element.Name.LocalName)
            .FirstOrDefault(PackageMetadataPropertyNames.Contains);
        if (metadataName is not null)
            throw new InvalidDataException($"Cannot prove package metadata '{metadataName}' assigned after the project in {path}.");
    }

    private static void ReadMsBuildPropertyFile(string path, MsBuildPropertySet properties, bool requirePackageMetadata)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path, LoadOptions.None);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            throw new InvalidDataException($"Configured LLMS project metadata is invalid: {path}", ex);
        }

        if (requirePackageMetadata && document.Descendants().Any(element => element.Name.LocalName == "Import"))
            throw new InvalidDataException($"Cannot prove package metadata from project with explicit MSBuild imports: {path}");

        foreach (var group in document.Descendants().Where(element => element.Name.LocalName == "PropertyGroup"))
        {
            foreach (var property in group.Elements())
            {
                var name = property.Name.LocalName;
                if (IsConditionalMsBuildScope(property))
                {
                    properties.ConditionalNames.Add(name);
                    continue;
                }

                properties.Values[name] = ExpandMsBuildPropertyAtAssignment(property.Value.Trim(), properties);
                properties.ConditionalNames.Remove(name);
            }
        }
    }

    private static bool IsConditionalMsBuildScope(XElement property)
    {
        return property.AncestorsAndSelf().Any(element =>
            !string.IsNullOrWhiteSpace(element.Attribute("Condition")?.Value) ||
            element.Name.LocalName is "Choose" or "When" or "Otherwise" or "Target");
    }

    private static string ExpandMsBuildPropertyAtAssignment(string value, MsBuildPropertySet properties)
    {
        var expanded = value;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var changed = false;
            expanded = Regex.Replace(expanded, @"\$\((?<name>[^)]+)\)", match =>
            {
                var referencedName = match.Groups["name"].Value;
                if (properties.ConditionalNames.Contains(referencedName) ||
                    !properties.Values.TryGetValue(referencedName, out var replacement))
                    return match.Value;
                changed = true;
                return replacement;
            });
            if (!changed) break;
        }

        return expanded;
    }

    private static string? GetMsBuildProperty(MsBuildPropertySet properties, string name, bool throwOnUnresolved)
    {
        if (properties.ConditionalNames.Contains(name))
        {
            if (throwOnUnresolved)
                throw new InvalidDataException($"Cannot resolve MSBuild property '{name}' for LLMS package metadata.");
            return null;
        }
        if (!properties.Values.TryGetValue(name, out var value)) return null;

        if (value.Contains("$(", StringComparison.Ordinal) ||
            value.Contains("%(", StringComparison.Ordinal))
        {
            if (throwOnUnresolved)
                throw new InvalidDataException($"Cannot resolve MSBuild property '{name}' for LLMS package metadata.");
            return null;
        }

        return NormalizeEmpty(value);
    }

    private sealed class MsBuildPropertySet
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ConditionalNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> PackageMetadataPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AssemblyName", "RootNamespace", "PackageId", "PackageVersion", "Version", "VersionPrefix",
        "VersionSuffix", "Description", "PackAsTool", "ToolCommandName"
    };

    private static string? CombineMsBuildVersion(string? versionPrefix, string? versionSuffix)
    {
        if (string.IsNullOrWhiteSpace(versionPrefix))
            return null;
        if (string.IsNullOrWhiteSpace(versionSuffix))
            return versionPrefix;

        return $"{versionPrefix}-{versionSuffix.TrimStart('-')}";
    }

    private static string MatchPowerShellDataValue(string content, string name)
    {
        var pattern = $@"(?im)(?:^|[;{{])\s*{Regex.Escape(name)}\s*=\s*['""](?<value>[^'""]+)['""]";
        var match = Regex.Match(content, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }

    private static string RemovePowerShellComments(string content)
    {
        var output = new StringBuilder(content.Length);
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];
            var next = index + 1 < content.Length ? content[index + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                    output.Append(current);
                }
                continue;
            }

            if (inBlockComment)
            {
                if (current == '#' && next == '>')
                {
                    inBlockComment = false;
                    index++;
                }
                else if (current is '\r' or '\n')
                {
                    output.Append(current);
                }
                continue;
            }

            if (inSingleQuotedString)
            {
                output.Append(current);
                if (current == '\'' && next == '\'')
                {
                    output.Append(next);
                    index++;
                }
                else if (current == '\'')
                {
                    inSingleQuotedString = false;
                }
                continue;
            }

            if (inDoubleQuotedString)
            {
                output.Append(current);
                if (current == '`' && next != '\0')
                {
                    output.Append(next);
                    index++;
                }
                else if (current == '"')
                {
                    inDoubleQuotedString = false;
                }
                continue;
            }

            if (current == '<' && next == '#')
            {
                inBlockComment = true;
                index++;
            }
            else if (current == '#')
            {
                inLineComment = true;
            }
            else
            {
                output.Append(current);
                if (current == '\'')
                    inSingleQuotedString = true;
                else if (current == '"')
                    inDoubleQuotedString = true;
            }
        }

        return output.ToString();
    }

    private static string? NormalizeEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class PackageInfo
    {
        public string Id { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string InstallCommand { get; set; } = string.Empty;
        public bool IsPowerShellModule { get; set; }
        public bool IsDotNetTool { get; set; }
        public string? ToolCommandName { get; set; }
    }

    private sealed class ProjectInfo
    {
        public string? Name { get; set; }
        public string? PackageId { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? ToolCommandName { get; set; }
        public bool IsPowerShellModule { get; set; }
        public bool IsDotNetTool { get; set; }
    }
}
