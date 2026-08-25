using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class EvaluatedProjectItem
    {
        internal EvaluatedProjectItem(string fullPath, IReadOnlyDictionary<string, string> metadata)
        {
            FullPath = fullPath;
            Metadata = metadata;
        }

        internal string FullPath { get; }

        internal IReadOnlyDictionary<string, string> Metadata { get; }
    }

    private static string[] ReadProjectReferenceItemListNames(
        XDocument document,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.Descendants().Any(element =>
                element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase)))
        {
            names.Add("ProjectReference");
        }
        foreach (string itemSpec in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
                 .SelectMany(element => element.Attributes())
                 .Where(attribute =>
                     attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                     attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                     attribute.Name.LocalName.Equals("Remove", StringComparison.OrdinalIgnoreCase) ||
                     attribute.Name.LocalName.Equals("Exclude", StringComparison.OrdinalIgnoreCase))
                 .Select(attribute => attribute.Value))
        {
            foreach (Match match in Regex.Matches(
                         itemSpec,
                         @"@\(\s*([A-Za-z_][A-Za-z0-9_.-]*?)(?=\s*(?:->|,|\)))",
                         RegexOptions.CultureInvariant))
            {
                names.Add(match.Groups[1].Value);
            }
        }
        if (document.Descendants().Where(element =>
                element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
            .SelectMany(element => element.Attributes().Where(attribute =>
                attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                attribute.Name.LocalName.Equals("Remove", StringComparison.OrdinalIgnoreCase)))
            .Any(attribute => IsMsBuildPropertyFunctionExpression(attribute.Value)))
        {
            names.Add("ProjectReference");
        }
        if (document.Descendants().Any(element =>
                element.Name.LocalName.Equals("Output", StringComparison.OrdinalIgnoreCase) &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName.Equals("ItemName", StringComparison.OrdinalIgnoreCase) &&
                    IsPotentialProjectReferenceTaskOutput(attribute.Value, evaluatedProperties)) &&
                element.Ancestors().Any(ancestor =>
                    ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase))))
        {
            names.Add("ProjectReference");
        }
        return names.ToArray();
    }

    private static bool TryReadEvaluatedProjectItemPaths(
        ProjectEvaluationRequest request,
        IReadOnlyCollection<string> itemNames,
        IReadOnlyCollection<string> evaluationTargets,
        bool preservePublishBuildProjectReferences,
        out IReadOnlyDictionary<string, EvaluatedProjectItem[]> evaluatedItems)
    {
        evaluatedItems = new Dictionary<string, EvaluatedProjectItem[]>(StringComparer.OrdinalIgnoreCase);
        if (itemNames.Count == 0)
            return true;

        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet"
        };
        foreach (string itemName in itemNames)
            arguments.Add("-getItem:" + itemName);
        if (evaluationTargets.Count > 0)
            arguments.Add("-target:" + string.Join(";", evaluationTargets));
        if (request.Configuration is not null)
            arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(request.Configuration));
        if (request.TargetFramework is not null)
            arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(request.TargetFramework));
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("BuildProjectReferences", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }
        AddProjectReferenceExecutionProperties(
            arguments,
            request,
            preservePublishBuildProjectReferences);

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut)
                return false;

            int itemsMarker = process.StdOut.LastIndexOf("\"Items\"", StringComparison.Ordinal);
            int jsonStart = itemsMarker < 0
                ? -1
                : process.StdOut.LastIndexOf('{', itemsMarker);
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return false;

            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            if (!document.RootElement.TryGetProperty("Items", out JsonElement items))
                return false;

            string projectDirectory = Path.GetDirectoryName(request.ProjectPath)!;
            var results = new Dictionary<string, EvaluatedProjectItem[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string itemName in itemNames)
            {
                if (!items.TryGetProperty(itemName, out JsonElement values) ||
                    values.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                results[itemName] = values.EnumerateArray()
                    .Select(value => ReadEvaluatedProjectItem(value, projectDirectory))
                    .OfType<EvaluatedProjectItem>()
                    .ToArray();
            }
            evaluatedItems = results;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddProjectReferenceExecutionProperties(
        ICollection<string> arguments,
        ProjectEvaluationRequest request,
        bool preservePublishBuildProjectReferences)
    {
        string buildProjectReferences = request.GlobalProperties.TryGetValue(
            "BuildProjectReferences",
            out string? requestedBuildProjectReferences)
            ? requestedBuildProjectReferences
            : "true";
        arguments.Add("-p:BuildProjectReferences=" + EscapeMsBuildPropertyValue(
            preservePublishBuildProjectReferences ? buildProjectReferences : "false"));
        if (preservePublishBuildProjectReferences)
            arguments.Add("-p:BuildingProject=true");
    }

    private static EvaluatedProjectItem? ReadEvaluatedProjectItem(
        JsonElement item,
        string projectDirectory)
    {
        string? path = ReadItemText(item, "FullPath") ?? ReadItemText(item, "Identity");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string fullPath = Path.GetFullPath(Path.IsPathRooted(path!)
            ? path!
            : Path.Combine(projectDirectory, path!));
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in item.EnumerateObject())
        {
            string? value = ReadItemText(item, property.Name);
            if (value is not null)
                metadata[property.Name] = value;
        }
        metadata["FullPath"] = fullPath;
        return new EvaluatedProjectItem(fullPath, metadata);
    }
}
