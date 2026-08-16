using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool ProjectDeclaresTargetFramework(string projectPath)
    {
        try
        {
            XDocument project = XDocument.Load(projectPath, LoadOptions.None);
            return project.Descendants().Any(element =>
                (element.Name.LocalName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(element.Value));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGeneratedBuildInfrastructurePath(
        string path,
        IEnumerable<string>? generatedBuildRoots)
    {
        if (!IsBelowGeneratedBuildRoot(path, generatedBuildRoots))
            return false;

        string fileName = Path.GetFileName(path);
        return fileName.EndsWith(".nuget.g.props", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".nuget.g.targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBelowGeneratedBuildRoot(
        string path,
        IEnumerable<string>? generatedBuildRoots)
        => (generatedBuildRoots ?? Array.Empty<string>())
            .Any(root => IsSameOrBelowBuildInputPath(path, root));

    private static void AddPropertyPath(
        JsonElement properties,
        string name,
        string baseDirectory,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return;
        }

        string value = property.GetString()!;
        values.Add(Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value)));
    }

    private static bool IsTrustedExternalBuildInfrastructurePath(string path)
        => IsTrustedExternalBuildInfrastructurePath(path, Array.Empty<string>());

    private static bool IsTrustedExternalBuildInfrastructurePath(
        string path,
        IEnumerable<string>? evaluatedRoots)
    {
        string fullPath = Path.GetFullPath(path);
        var roots = new HashSet<string>(
            evaluatedRoots ?? Array.Empty<string>(),
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        AddEnvironmentDirectory(roots, "DOTNET_ROOT");
        AddEnvironmentDirectory(roots, "DOTNET_ROOT(x86)");
        AddEnvironmentDirectory(roots, "MSBuildSDKsPath");
        AddEnvironmentDirectory(roots, "MSBuildExtensionsPath");
        AddEnvironmentDirectory(roots, "NUGET_PACKAGES");

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            roots.Add(Path.GetFullPath(Path.Combine(programFiles, "dotnet")));
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            roots.Add(Path.GetFullPath(Path.Combine(programFilesX86, "dotnet")));
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.Add(Path.GetFullPath(Path.Combine(userProfile, ".dotnet")));
            roots.Add(Path.GetFullPath(Path.Combine(userProfile, ".nuget", "packages")));
        }

        return roots.Any(root => IsSameOrBelowBuildInputPath(fullPath, root));
    }

    private static string[] ReadTrustedBuildInfrastructureRoots(
        JsonElement properties,
        string projectDirectory)
    {
        string? toolsPath = ReadEvaluatedPath(properties, "MSBuildToolsPath", projectDirectory);
        if (string.IsNullOrWhiteSpace(toolsPath))
            return Array.Empty<string>();

        var versionDirectory = new DirectoryInfo(toolsPath!);
        DirectoryInfo? sdkDirectory = versionDirectory.Parent;
        if (sdkDirectory is not null &&
            sdkDirectory.Name.Equals("sdk", StringComparison.OrdinalIgnoreCase) &&
            sdkDirectory.Parent is not null)
        {
            return [sdkDirectory.Parent.FullName];
        }

        return [versionDirectory.FullName];
    }

    private static void AddEnvironmentDirectory(HashSet<string> roots, string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
            roots.Add(Path.GetFullPath(value));
    }

    private static bool IsSameOrBelowBuildInputPath(string path, string root)
    {
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(path, fullRoot, comparison) ||
               path.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private sealed class ProjectEvaluationRequest
    {
        internal ProjectEvaluationRequest(
            string projectPath,
            string? targetFramework,
            string configuration,
            IReadOnlyDictionary<string, string>? globalProperties,
            IReadOnlyDictionary<string, string?>? environmentVariables)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
            Configuration = configuration;
            GlobalProperties = globalProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EnvironmentVariables = environmentVariables ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        internal string ProjectPath { get; }
        internal string? TargetFramework { get; }
        internal string Configuration { get; }
        internal IReadOnlyDictionary<string, string> GlobalProperties { get; }
        internal IReadOnlyDictionary<string, string?> EnvironmentVariables { get; }

        internal ProjectEvaluationRequest ForProject(string projectPath, string? targetFramework)
            => new(
                Path.GetFullPath(projectPath),
                targetFramework,
                Configuration,
                GlobalProperties,
                EnvironmentVariables);

        internal string BuildVisitKey()
            => string.Join(
                "|",
                new[] { ProjectPath, TargetFramework ?? string.Empty, Configuration }
                    .Concat(GlobalProperties
                        .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => entry.Key + "=" + entry.Value))
                    .Concat(EnvironmentVariables
                        .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => entry.Key + "=" + entry.Value)));
    }

    private sealed class EvaluatedProjectInputs
    {
        internal EvaluatedProjectInputs(
            string[] buildInputs,
            string[] sourceInputs,
            EvaluatedProjectReference[] projectReferences,
            string[] targetFrameworks)
        {
            BuildInputs = buildInputs;
            SourceInputs = sourceInputs;
            ProjectReferences = projectReferences;
            TargetFrameworks = targetFrameworks;
        }

        internal string[] BuildInputs { get; }
        internal string[] SourceInputs { get; }
        internal EvaluatedProjectReference[] ProjectReferences { get; }
        internal string[] TargetFrameworks { get; }
    }

    private sealed class EvaluatedProjectReference
    {
        internal EvaluatedProjectReference(string projectPath, string? targetFramework)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
        }

        internal string ProjectPath { get; }
        internal string? TargetFramework { get; }
    }
}
