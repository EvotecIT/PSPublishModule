using System.Text;
using System.Text.RegularExpressions;

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

            var project = ReadProjectInfo(fullPath);
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

    private static ProjectInfo ReadProjectInfo(string? projectFile)
    {
        if (string.IsNullOrWhiteSpace(projectFile))
            return new ProjectInfo();

        var full = Path.GetFullPath(projectFile);
        if (!File.Exists(full))
            return new ProjectInfo();

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

        var assemblyName = NormalizeMsBuildMetadataValue(MatchValue(content, "AssemblyName"));
        var rootNamespace = NormalizeMsBuildMetadataValue(MatchValue(content, "RootNamespace"));
        var packageId = NormalizeMsBuildMetadataValue(MatchValue(content, "PackageId"));
        var packageVersion = NormalizeMsBuildMetadataValue(MatchValue(content, "PackageVersion"));
        var versionValue = NormalizeMsBuildMetadataValue(MatchValue(content, "Version"));
        var versionPrefix = NormalizeMsBuildMetadataValue(MatchValue(content, "VersionPrefix"));
        var versionSuffix = NormalizeMsBuildMetadataValue(MatchValue(content, "VersionSuffix"));
        var version = packageVersion ??
                      versionValue ??
                      CombineMsBuildVersion(versionPrefix, versionSuffix);
        var description = NormalizeMsBuildMetadataValue(MatchValue(content, "Description"));
        var packAsTool = NormalizeMsBuildMetadataValue(MatchValue(content, "PackAsTool"));
        var toolCommandName = NormalizeMsBuildMetadataValue(MatchValue(content, "ToolCommandName"));

        return new ProjectInfo
        {
            Name = assemblyName ?? rootNamespace,
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
