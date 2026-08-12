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
        var assemblyName = GetMsBuildProperty(properties, "AssemblyName");
        var rootNamespace = GetMsBuildProperty(properties, "RootNamespace");
        var packageId = GetMsBuildProperty(properties, "PackageId");
        var packageVersion = GetMsBuildProperty(properties, "PackageVersion");
        var versionValue = GetMsBuildProperty(properties, "Version");
        var versionPrefix = GetMsBuildProperty(properties, "VersionPrefix");
        var versionSuffix = GetMsBuildProperty(properties, "VersionSuffix");
        var version = packageVersion ??
                      versionValue ??
                      CombineMsBuildVersion(versionPrefix, versionSuffix);
        var description = GetMsBuildProperty(properties, "Description");
        var packAsTool = GetMsBuildProperty(properties, "PackAsTool");
        var toolCommandName = GetMsBuildProperty(properties, "ToolCommandName");

        var projectName = Path.GetFileNameWithoutExtension(full);
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

    private static Dictionary<string, string> ReadMsBuildProperties(string projectFile, bool requirePackageMetadata)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(projectFile)
        };

        var directory = Path.GetDirectoryName(projectFile);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var propsPath = Path.Combine(directory, "Directory.Build.props");
            if (File.Exists(propsPath))
            {
                ReadMsBuildPropertyFile(propsPath, properties, requirePackageMetadata);
                break;
            }
            directory = Directory.GetParent(directory)?.FullName;
        }

        ReadMsBuildPropertyFile(projectFile, properties, requirePackageMetadata);
        return properties;
    }

    private static void ReadMsBuildPropertyFile(string path, Dictionary<string, string> properties, bool requirePackageMetadata)
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
            var groupCondition = group.Attribute("Condition")?.Value;
            foreach (var property in group.Elements())
            {
                var name = property.Name.LocalName;
                if (!string.IsNullOrWhiteSpace(groupCondition) || property.Attribute("Condition") is not null)
                {
                    if (requirePackageMetadata && CriticalPackageProperties.Contains(name))
                        throw new InvalidDataException($"Cannot prove conditional package metadata '{name}' in {path}.");
                    continue;
                }

                properties[name] = property.Value.Trim();
            }
        }
    }

    private static string? GetMsBuildProperty(IReadOnlyDictionary<string, string> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value)) return null;

        var expanded = value;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var changed = false;
            expanded = Regex.Replace(expanded, @"\$\((?<name>[^)]+)\)", match =>
            {
                if (!properties.TryGetValue(match.Groups["name"].Value, out var replacement))
                    return match.Value;
                changed = true;
                return replacement;
            });
            if (!changed) break;
        }

        if (expanded.Contains("$(", StringComparison.Ordinal) || expanded.Contains("%(", StringComparison.Ordinal))
            throw new InvalidDataException($"Cannot resolve MSBuild property '{name}' for LLMS package metadata.");
        return NormalizeEmpty(expanded);
    }

    private static readonly HashSet<string> CriticalPackageProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "AssemblyName", "PackageId", "PackAsTool", "ToolCommandName"
    };

    private static string? CombineMsBuildVersion(string? versionPrefix, string? versionSuffix)
    {
        if (string.IsNullOrWhiteSpace(versionPrefix))
            return null;
        if (string.IsNullOrWhiteSpace(versionSuffix))
            return versionPrefix;

        return $"{versionPrefix}-{versionSuffix.TrimStart('-')}";
    }

    private static string? NormalizeMsBuildMetadataValue(string value)
    {
        var normalized = NormalizeEmpty(value);
        if (normalized is null)
            return null;

        return normalized.Contains("$(", StringComparison.Ordinal) ||
               normalized.Contains("%(", StringComparison.Ordinal)
            ? null
            : normalized;
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

    private static string MatchValue(string content, string name)
    {
        var pattern = $@"<{name}>([^<]+)</{name}>";
        var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
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
