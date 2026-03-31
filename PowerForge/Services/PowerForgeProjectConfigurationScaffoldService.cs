using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Resolves and scaffolds starter project release configuration files.
/// </summary>
public sealed class PowerForgeProjectConfigurationScaffoldService
{
    private readonly PowerForgeProjectConfigurationJsonService _jsonService = new();

    /// <summary>
    /// Resolves the output config path without writing the file.
    /// </summary>
    public string ResolveOutputPath(PowerForgeProjectConfigurationScaffoldRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var projectRoot = ResolveProjectRoot(request);
        var outputPath = ResolvePath(projectRoot, request.OutputPath);
        EnsurePathWithinRoot(projectRoot, outputPath, "Project release config output path");
        return outputPath;
    }

    /// <summary>
    /// Generates a starter project release configuration file.
    /// </summary>
    public PowerForgeProjectConfigurationScaffoldResult Generate(PowerForgeProjectConfigurationScaffoldRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var projectRoot = ResolveProjectRoot(request);
        var outputPath = ResolveOutputPath(request);
        var projectPath = ResolveProjectPath(projectRoot, request.ProjectPath);
        var inferredTargetName = Path.GetFileNameWithoutExtension(projectPath) ?? "App";
        var targetName = string.IsNullOrWhiteSpace(request.TargetName)
            ? inferredTargetName
            : request.TargetName!.Trim();
        var releaseName = string.IsNullOrWhiteSpace(request.Name)
            ? targetName
            : request.Name!.Trim();
        var explicitFramework = string.IsNullOrWhiteSpace(request.Framework) ? null : request.Framework!.Trim();
        var framework = explicitFramework ?? InferFramework(projectPath) ?? "net8.0";
        var runtimes = ResolveRuntimes(projectPath, request.Runtimes);
        var configDirectory = Path.GetDirectoryName(outputPath) ?? projectRoot;

        var project = new ConfigurationProject
        {
            Name = releaseName,
            ProjectRoot = GetRelativePathCompat(configDirectory, projectRoot).Replace('\\', '/'),
            Release = new ConfigurationProjectRelease
            {
                Configuration = string.IsNullOrWhiteSpace(request.Configuration) ? "Release" : request.Configuration.Trim(),
                ToolOutput = request.IncludePortableOutput
                    ? new[] { ConfigurationProjectReleaseOutputType.Portable }
                    : Array.Empty<ConfigurationProjectReleaseOutputType>()
            },
            Targets = new[]
            {
                new ConfigurationProjectTarget
                {
                    Name = targetName,
                    ProjectPath = GetRelativePathCompat(projectRoot, projectPath).Replace('\\', '/'),
                    Framework = framework,
                    Runtimes = runtimes,
                    Style = DotNetPublishStyle.PortableCompat,
                    OutputType = request.IncludePortableOutput
                        ? new[] { ConfigurationProjectTargetOutputType.Tool, ConfigurationProjectTargetOutputType.Portable }
                        : new[] { ConfigurationProjectTargetOutputType.Tool }
                }
            }
        };

        var savedPath = _jsonService.Save(project, outputPath, request.Force);
        return new PowerForgeProjectConfigurationScaffoldResult
        {
            ConfigPath = savedPath,
            ProjectPath = project.Targets[0].ProjectPath,
            Name = releaseName,
            TargetName = targetName,
            Framework = framework,
            Runtimes = runtimes,
            IncludesPortableOutput = request.IncludePortableOutput
        };
    }

    private static string ResolveProjectRoot(PowerForgeProjectConfigurationScaffoldRequest request)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Environment.CurrentDirectory
            : request.WorkingDirectory;
        var projectRoot = string.IsNullOrWhiteSpace(request.ProjectRoot)
            ? baseDirectory
            : request.ProjectRoot;

        var fullPath = Path.GetFullPath(Path.IsPathRooted(projectRoot)
            ? projectRoot
            : Path.Combine(baseDirectory, projectRoot));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Project root does not exist: {fullPath}");

        return fullPath;
    }

    private static string ResolvePath(string basePath, string value)
    {
        var raw = NormalizePathValue(value);
        if (raw.Length == 0)
            return Path.GetFullPath(basePath);

        return Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(basePath, raw));
    }

    private static string ResolveProjectPath(string projectRoot, string? projectPath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var explicitPath = ResolvePath(projectRoot, projectPath!);
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException($"Project file not found: {explicitPath}");
            return explicitPath;
        }

        var candidates = Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredPath(path))
            .OrderBy(path => GetRelativePathCompat(projectRoot, path).Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
            .ThenBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException($"No .csproj files found under project root: {projectRoot}");

        return candidates[0];
    }

    private static bool IsIgnoredPath(string path)
    {
        static bool IsIgnoredSegment(string segment)
            => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
               || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
               || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
               || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase);

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalized.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(IsIgnoredSegment);
    }

    private static string? InferFramework(string projectPath)
    {
        var doc = TryLoadProject(projectPath);
        if (doc is null)
            return null;

        var single = ReadFirstProperty(doc, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(single))
            return single!.Trim();

        var multi = ReadFirstProperty(doc, "TargetFrameworks");
        if (string.IsNullOrWhiteSpace(multi))
            return null;

        return multi!
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value!.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string[] ResolveRuntimes(string projectPath, string[]? runtimes)
    {
        var explicitRuntimes = NormalizeStrings(runtimes);
        if (explicitRuntimes.Length > 0)
            return explicitRuntimes;

        var doc = TryLoadProject(projectPath);
        if (doc is not null)
        {
            var single = ReadFirstProperty(doc, "RuntimeIdentifier");
            var multi = ReadFirstProperty(doc, "RuntimeIdentifiers");
            var inferredValues = !string.IsNullOrWhiteSpace(single)
                ? new[] { single! }
                : string.IsNullOrWhiteSpace(multi)
                    ? Array.Empty<string>()
                    : multi!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var inferred = NormalizeStrings(
                inferredValues);
            if (inferred.Length > 0)
                return inferred;
        }

        return new[] { "win-x64" };
    }

    private static string[] NormalizeStrings(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<string>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePathValue(string? value)
    {
        var raw = (value ?? string.Empty).Trim().Trim('"');
        if (raw.Length == 0)
            return string.Empty;

        return raw
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace(Path.DirectorySeparatorChar == '/' ? '\\' : '/', Path.DirectorySeparatorChar);
    }

    private static void EnsurePathWithinRoot(string projectRoot, string path, string label)
    {
        var relative = GetRelativePathCompat(projectRoot, path);
        if (relative.StartsWith("..", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(relative))
            throw new InvalidOperationException($"{label} must stay within project root '{projectRoot}'.");
    }

    private static XDocument? TryLoadProject(string projectPath)
    {
        try
        {
            return XDocument.Load(projectPath, LoadOptions.None);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadFirstProperty(XDocument doc, string name)
    {
        return doc
            .Descendants()
            .Where(node => node.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(node => node.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string GetRelativePathCompat(string relativeTo, string path)
    {
        if (string.IsNullOrWhiteSpace(relativeTo))
            return path;

        var fromUri = new Uri(AppendDirectorySeparator(relativeTo));
        var toUri = new Uri(path);
        return Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
    }
}
