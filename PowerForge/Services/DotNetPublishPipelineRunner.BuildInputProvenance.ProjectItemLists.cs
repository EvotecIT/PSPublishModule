using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static string[] ReadProjectReferenceItemListNames(XDocument document)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                         @"@\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
                         RegexOptions.CultureInvariant))
            {
                names.Add(match.Groups[1].Value);
            }
        }
        return names.ToArray();
    }

    private static IReadOnlyDictionary<string, string[]> ReadEvaluatedProjectItemPaths(
        ProjectEvaluationRequest request,
        IReadOnlyCollection<string> itemNames)
    {
        if (itemNames.Count == 0)
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet"
        };
        foreach (string itemName in itemNames)
            arguments.Add("-getItem:" + itemName);
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
        arguments.Add("-p:BuildProjectReferences=false");

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut)
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            int jsonStart = process.StdOut.IndexOf('{');
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            if (!document.RootElement.TryGetProperty("Items", out JsonElement items))
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            string projectDirectory = Path.GetDirectoryName(request.ProjectPath)!;
            var results = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string itemName in itemNames)
            {
                if (!items.TryGetProperty(itemName, out JsonElement values) ||
                    values.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                results[itemName] = values.EnumerateArray()
                    .Select(value => ReadItemText(value, "FullPath") ?? ReadItemText(value, "Identity"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => Path.GetFullPath(Path.IsPathRooted(value!)
                        ? value!
                        : Path.Combine(projectDirectory, value!)))
                    .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                    .ToArray();
            }
            return results;
        }
        catch
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
