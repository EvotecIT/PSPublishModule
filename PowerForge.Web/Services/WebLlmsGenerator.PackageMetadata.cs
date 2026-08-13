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

    private static ProjectInfo ReadProjectInfo(
        string? projectFile,
        bool requirePackageMetadata = false,
        bool requirePackageId = true,
        bool requireVersion = true)
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

        var properties = ReadMsBuildProperties(full);
        var assemblyName = GetMsBuildProperty(properties, "AssemblyName", throwOnUnresolved: false);
        var rootNamespace = GetMsBuildProperty(properties, "RootNamespace", throwOnUnresolved: false);
        var description = GetMsBuildProperty(properties, "Description", throwOnUnresolved: false);
        var projectName = Path.GetFileNameWithoutExtension(full);
        if (!requirePackageMetadata)
        {
            return new ProjectInfo
            {
                Name = assemblyName ?? rootNamespace ?? projectName,
                Description = description
            };
        }

        var packageId = GetMsBuildProperty(properties, "PackageId", throwOnUnresolved: requirePackageId);
        if (packageId is null)
            assemblyName = GetMsBuildProperty(properties, "AssemblyName", throwOnUnresolved: requirePackageId);

        var version = GetMsBuildProperty(properties, "PackageVersion", throwOnUnresolved: requireVersion);
        if (version is null)
            version = GetMsBuildProperty(properties, "Version", throwOnUnresolved: requireVersion);
        if (version is null)
        {
            var versionPrefix = GetMsBuildProperty(properties, "VersionPrefix", throwOnUnresolved: requireVersion);
            var versionSuffix = versionPrefix is null
                ? null
                : GetMsBuildProperty(properties, "VersionSuffix", throwOnUnresolved: requireVersion);
            version = CombineMsBuildVersion(versionPrefix, versionSuffix);
        }
        var packAsTool = GetMsBuildProperty(properties, "PackAsTool", throwOnUnresolved: true);
        var isDotNetTool = string.Equals(packAsTool, "true", StringComparison.OrdinalIgnoreCase);
        var toolCommandName = isDotNetTool
            ? GetMsBuildProperty(properties, "ToolCommandName", throwOnUnresolved: true)
            : null;
        if (isDotNetTool && toolCommandName is null)
        {
            assemblyName = GetMsBuildProperty(properties, "AssemblyName", throwOnUnresolved: true);
            toolCommandName = assemblyName ?? projectName;
        }

        packageId ??= assemblyName ?? projectName;

        return new ProjectInfo
        {
            Name = assemblyName ?? rootNamespace ?? projectName,
            PackageId = packageId,
            Version = version,
            Description = description,
            ToolCommandName = toolCommandName,
            IsDotNetTool = isDotNetTool
        };
    }

    internal static IReadOnlyList<string> DiscoverMsBuildMetadataInputs(string projectFile)
    {
        if (string.IsNullOrWhiteSpace(projectFile) || !File.Exists(projectFile))
            return Array.Empty<string>();
        if (Path.GetExtension(projectFile).Equals(".psd1", StringComparison.OrdinalIgnoreCase))
            return new[] { Path.GetFullPath(projectFile) };

        return ReadMsBuildProperties(Path.GetFullPath(projectFile)).InputPaths
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MsBuildPropertySet ReadMsBuildProperties(string projectFile)
    {
        var properties = new MsBuildPropertySet();
        properties.Values["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(projectFile);

        var propsPath = FindNearestBuildFile(projectFile, "Directory.Build.props");
        if (propsPath is not null)
            ReadMsBuildPropertyFile(propsPath, properties);

        ReadMsBuildPropertyFile(projectFile, properties);
        var targetsPath = ResolveDirectoryBuildTargetsPath(projectFile, properties);
        if (targetsPath is not null)
            ReadMsBuildPropertyFile(targetsPath, properties);
        return properties;
    }

    private static string? ResolveDirectoryBuildTargetsPath(string projectFile, MsBuildPropertySet properties)
    {
        if (IsMsBuildPropertyUnresolved(properties, "ImportDirectoryBuildTargets") ||
            IsMsBuildPropertyUnresolved(properties, "DirectoryBuildTargetsPath"))
        {
            MarkAllPackageMetadataConditional(properties);
            return null;
        }

        var importTargets = GetMsBuildProperty(properties, "ImportDirectoryBuildTargets", throwOnUnresolved: false);
        if (string.Equals(importTargets, "false", StringComparison.OrdinalIgnoreCase))
            return null;

        var configuredPath = GetMsBuildProperty(properties, "DirectoryBuildTargetsPath", throwOnUnresolved: false);
        if (configuredPath is null)
            return FindNearestBuildFile(projectFile, "Directory.Build.targets");
        if (!Path.IsPathRooted(configuredPath))
        {
            MarkAllPackageMetadataConditional(properties);
            return null;
        }

        var normalizedPath = configuredPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(normalizedPath, Path.GetDirectoryName(projectFile) ?? string.Empty);
        if (File.Exists(fullPath))
            return fullPath;

        properties.InputPaths.Add(fullPath);
        return null;
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

    private static void ReadMsBuildPropertyFile(string path, MsBuildPropertySet properties)
    {
        var fullPath = Path.GetFullPath(path);
        if (!properties.InputPaths.Add(fullPath))
            return;

        XDocument document;
        try
        {
            document = XDocument.Load(fullPath, LoadOptions.None);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            throw new InvalidDataException($"Configured LLMS project metadata is invalid: {fullPath}", ex);
        }

        var previousThisFileDirectory = properties.Values.GetValueOrDefault("MSBuildThisFileDirectory");
        properties.Values["MSBuildThisFileDirectory"] = (Path.GetDirectoryName(fullPath) ?? string.Empty) + Path.DirectorySeparatorChar;
        foreach (var element in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var elementName = element.Name.LocalName;
            if (elementName == "PropertyGroup")
            {
                EvaluatePropertyGroup(element, properties);
                continue;
            }
            if (elementName == "Import")
                EvaluateImport(element, fullPath, properties);
            else if (elementName == "ImportGroup")
                foreach (var import in element.Elements().Where(static item => item.Name.LocalName == "Import"))
                    EvaluateImport(import, fullPath, properties, inheritedCondition: element.Attribute("Condition")?.Value);
            else if (elementName is "Choose" or "Target")
                MarkConditionalProperties(element, properties);
        }

        if (previousThisFileDirectory is null)
            properties.Values.Remove("MSBuildThisFileDirectory");
        else
            properties.Values["MSBuildThisFileDirectory"] = previousThisFileDirectory;
    }

    private static void EvaluatePropertyGroup(XElement group, MsBuildPropertySet properties)
    {
        var groupConditional = !string.IsNullOrWhiteSpace(group.Attribute("Condition")?.Value);
        foreach (var property in group.Elements())
        {
            var name = property.Name.LocalName;
            if (groupConditional || !string.IsNullOrWhiteSpace(property.Attribute("Condition")?.Value))
            {
                properties.ConditionalNames.Add(name);
                continue;
            }

            properties.Values[name] = ExpandMsBuildPropertyAtAssignment(property.Value.Trim(), properties);
            properties.ConditionalNames.Remove(name);
        }
    }

    private static void EvaluateImport(XElement import, string importingFile, MsBuildPropertySet properties, string? inheritedCondition = null)
    {
        var projectExpression = import.Attribute("Project")?.Value;
        var isConditional = !string.IsNullOrWhiteSpace(inheritedCondition) ||
                            !string.IsNullOrWhiteSpace(import.Attribute("Condition")?.Value);
        var expanded = ExpandMsBuildPropertyAtAssignment(projectExpression ?? string.Empty, properties);
        if (string.IsNullOrWhiteSpace(expanded) || expanded.Contains("$(", StringComparison.Ordinal) ||
            expanded.Contains('*') || expanded.Contains(';'))
        {
            MarkAllPackageMetadataConditional(properties);
            return;
        }

        var normalizedImportPath = expanded
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var importedPath = Path.GetFullPath(normalizedImportPath, Path.GetDirectoryName(importingFile) ?? string.Empty);
        if (!File.Exists(importedPath))
        {
            properties.InputPaths.Add(importedPath);
            if (ConditionProvesMissingImportIsSkipped(inheritedCondition, importingFile, properties) ||
                ConditionProvesMissingImportIsSkipped(import.Attribute("Condition")?.Value, importingFile, properties))
                return;

            MarkAllPackageMetadataConditional(properties);
            return;
        }

        if (isConditional)
        {
            var conditionalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectImportedPropertyNames(importedPath, conditionalNames, properties.InputPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var name in conditionalNames)
                properties.ConditionalNames.Add(name);
            return;
        }

        ReadMsBuildPropertyFile(importedPath, properties);
    }

    private static void CollectImportedPropertyNames(
        string path,
        HashSet<string> names,
        HashSet<string> inputPaths,
        HashSet<string> visited)
    {
        var fullPath = Path.GetFullPath(path);
        if (!visited.Add(fullPath)) return;
        inputPaths.Add(fullPath);

        XDocument document;
        try
        {
            document = XDocument.Load(fullPath, LoadOptions.None);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            throw new InvalidDataException($"Configured LLMS project metadata is invalid: {fullPath}", ex);
        }

        foreach (var property in document.Descendants()
                     .Where(static element => element.Parent?.Name.LocalName == "PropertyGroup"))
            names.Add(property.Name.LocalName);

        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var conditionProperties = new MsBuildPropertySet();
        conditionProperties.Values["MSBuildThisFileDirectory"] = directory + Path.DirectorySeparatorChar;
        foreach (var import in document.Descendants().Where(static element => element.Name.LocalName == "Import"))
        {
            var expression = import.Attribute("Project")?.Value ?? string.Empty;
            expression = expression.Replace("$(MSBuildThisFileDirectory)", directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(expression) || expression.Contains("$(", StringComparison.Ordinal) ||
                expression.Contains('*') || expression.Contains(';'))
            {
                foreach (var metadataName in PackageMetadataPropertyNames)
                    names.Add(metadataName);
                continue;
            }

            var normalizedPath = expression
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var importedPath = Path.GetFullPath(normalizedPath, directory);
            inputPaths.Add(importedPath);
            if (!File.Exists(importedPath))
            {
                if (ConditionProvesMissingImportIsSkipped(import.Attribute("Condition")?.Value, fullPath, conditionProperties))
                    continue;

                foreach (var metadataName in PackageMetadataPropertyNames)
                    names.Add(metadataName);
                continue;
            }

            CollectImportedPropertyNames(importedPath, names, inputPaths, visited);
        }
    }

    private static bool ConditionProvesMissingImportIsSkipped(
        string? condition,
        string importingFile,
        MsBuildPropertySet properties)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return false;

        var expandedCondition = ExpandMsBuildPropertyAtAssignment(condition, properties);
        if (Regex.IsMatch(expandedCondition, @"\bor\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        foreach (Match match in Regex.Matches(
                     expandedCondition,
                     @"(?<![!\w])Exists\s*\(\s*(?<quote>['""])(?<path>.*?)\k<quote>\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var candidate = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains("$(", StringComparison.Ordinal))
                continue;

            var normalizedPath = candidate
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(normalizedPath, Path.GetDirectoryName(importingFile) ?? string.Empty);
            if (!File.Exists(fullPath))
                return true;
        }

        return false;
    }

    private static void MarkConditionalProperties(XElement? root, MsBuildPropertySet properties)
    {
        if (root is null) return;
        foreach (var property in root.Descendants()
                     .Where(static element => element.Parent?.Name.LocalName == "PropertyGroup"))
            properties.ConditionalNames.Add(property.Name.LocalName);
    }

    private static void MarkAllPackageMetadataConditional(MsBuildPropertySet properties)
    {
        foreach (var name in PackageMetadataPropertyNames)
            properties.ConditionalNames.Add(name);
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

    private static bool IsMsBuildPropertyUnresolved(MsBuildPropertySet properties, string name)
    {
        if (properties.ConditionalNames.Contains(name))
            return true;
        return properties.Values.TryGetValue(name, out var value) &&
               (value.Contains("$(", StringComparison.Ordinal) || value.Contains("%(", StringComparison.Ordinal));
    }

    private sealed class MsBuildPropertySet
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ConditionalNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> InputPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> PackageMetadataPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AssemblyName", "RootNamespace", "PackageId", "PackageVersion", "Version", "VersionPrefix",
        "VersionSuffix", "Description", "PackAsTool", "ToolCommandName", "ImportDirectoryBuildTargets",
        "DirectoryBuildTargetsPath"
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
