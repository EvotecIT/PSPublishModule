using System.Diagnostics;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly string[] EvaluatedBuildItemNames =
    [
        "Compile",
        "Content",
        "EmbeddedResource",
        "AdditionalFiles",
        "Analyzer",
        "ApplicationDefinition",
        "Page",
        "Resource",
        "SplashScreen",
        "RazorComponent",
        "TypeScriptCompile",
        "None",
        "ProjectReference"
    ];

    private static bool TryEvaluateDotNetBuildInputs(
        IEnumerable<string>? projectPaths,
        out string[] projectDirectories,
        out HashSet<string> buildInputs)
    {
        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var pending = new Queue<ProjectEvaluationRequest>((projectPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new ProjectEvaluationRequest(Path.GetFullPath(path), null)));
        var visited = new HashSet<string>(comparison);
        var directories = new HashSet<string>(comparison);
        buildInputs = new HashSet<string>(comparison);

        while (pending.Count > 0)
        {
            ProjectEvaluationRequest request = pending.Dequeue();
            string visitKey = request.ProjectPath + "|" + (request.TargetFramework ?? string.Empty);
            if (!visited.Add(visitKey) || !File.Exists(request.ProjectPath))
                continue;

            string projectDirectory = Path.GetDirectoryName(request.ProjectPath)!;
            directories.Add(projectDirectory);
            buildInputs.Add(request.ProjectPath);
            if (!TryReadEvaluatedProjectInputs(request, out EvaluatedProjectInputs? evaluation) || evaluation is null)
            {
                projectDirectories = directories.ToArray();
                return false;
            }

            foreach (string input in evaluation.BuildInputs)
                buildInputs.Add(input);
            foreach (string projectReference in evaluation.ProjectReferences)
                pending.Enqueue(new ProjectEvaluationRequest(projectReference, null));
            if (request.TargetFramework is null)
            {
                foreach (string targetFramework in evaluation.TargetFrameworks)
                    pending.Enqueue(new ProjectEvaluationRequest(request.ProjectPath, targetFramework));
            }
        }

        projectDirectories = directories.ToArray();
        return true;
    }

    private static bool TryReadEvaluatedProjectInputs(
        ProjectEvaluationRequest request,
        out EvaluatedProjectInputs? evaluation)
    {
        evaluation = null;
        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-getProperty:TargetFramework",
            "-getProperty:TargetFrameworks",
            "-getProperty:MSBuildAllProjects",
            "-p:Configuration=Release"
        };
        foreach (string itemName in EvaluatedBuildItemNames)
            arguments.Add("-getItem:" + itemName);
        if (!string.IsNullOrWhiteSpace(request.TargetFramework))
            arguments.Add("-p:TargetFramework=" + request.TargetFramework);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(request.ProjectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
#if NET472
        startInfo.Arguments = BuildWindowsArgumentString(arguments);
#else
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
#endif

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
                return false;
            string standardOutput = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return false;

            int jsonStart = standardOutput.IndexOf('{');
            int jsonEnd = standardOutput.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return false;
            using JsonDocument document = JsonDocument.Parse(
                standardOutput.Substring(jsonStart, jsonEnd - jsonStart + 1));
            JsonElement root = document.RootElement;
            var inputs = new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var references = new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var targetFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("Properties", out JsonElement properties))
            {
                AddSemicolonSeparatedPaths(
                    properties,
                    "MSBuildAllProjects",
                    Path.GetDirectoryName(request.ProjectPath)!,
                    inputs);
                AddSemicolonSeparatedValues(properties, "TargetFrameworks", targetFrameworks);
                if (targetFrameworks.Count == 0)
                    AddSemicolonSeparatedValues(properties, "TargetFramework", targetFrameworks);
            }
            if (root.TryGetProperty("Items", out JsonElement items))
            {
                foreach (string itemName in EvaluatedBuildItemNames)
                {
                    if (!items.TryGetProperty(itemName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (JsonElement item in values.EnumerateArray())
                    {
                        if (!item.TryGetProperty("FullPath", out JsonElement fullPathElement) ||
                            fullPathElement.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(fullPathElement.GetString()))
                        {
                            continue;
                        }
                        string fullPath = Path.GetFullPath(fullPathElement.GetString()!);
                        if (itemName.Equals("ProjectReference", StringComparison.Ordinal))
                        {
                            references.Add(fullPath);
                            inputs.Add(fullPath);
                        }
                        else if (!itemName.Equals("None", StringComparison.Ordinal) || IsOutputRelevantNoneItem(item))
                        {
                            inputs.Add(fullPath);
                        }
                    }
                }
            }

            evaluation = new EvaluatedProjectInputs(
                inputs.ToArray(),
                references.ToArray(),
                targetFrameworks.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOutputRelevantNoneItem(JsonElement item)
        => HasRelevantMetadata(item, "CopyToOutputDirectory")
           || HasRelevantMetadata(item, "CopyToPublishDirectory")
           || (item.TryGetProperty("Pack", out JsonElement pack) &&
               pack.ValueKind == JsonValueKind.String &&
               bool.TryParse(pack.GetString(), out bool packs) && packs);

    private static bool HasRelevantMetadata(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
           && !value.GetString()!.Equals("Never", StringComparison.OrdinalIgnoreCase);

    private static void AddSemicolonSeparatedPaths(
        JsonElement properties,
        string name,
        string baseDirectory,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return;
        foreach (string value in (property.GetString() ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.GetFullPath(
                Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
            if (File.Exists(fullPath))
                values.Add(fullPath);
        }
    }

    private static void AddSemicolonSeparatedValues(
        JsonElement properties,
        string name,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return;
        foreach (string value in (property.GetString() ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            values.Add(value.Trim());
        }
    }

    private sealed class ProjectEvaluationRequest
    {
        internal ProjectEvaluationRequest(string projectPath, string? targetFramework)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
        }

        internal string ProjectPath { get; }
        internal string? TargetFramework { get; }
    }

    private sealed class EvaluatedProjectInputs
    {
        internal EvaluatedProjectInputs(
            string[] buildInputs,
            string[] projectReferences,
            string[] targetFrameworks)
        {
            BuildInputs = buildInputs;
            ProjectReferences = projectReferences;
            TargetFrameworks = targetFrameworks;
        }

        internal string[] BuildInputs { get; }
        internal string[] ProjectReferences { get; }
        internal string[] TargetFrameworks { get; }
    }
}
