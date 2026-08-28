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

    private sealed class EvaluatedPublishInput
    {
        internal EvaluatedPublishInput(
            string fullPath,
            string relativePath,
            IReadOnlyDictionary<string, string> metadata,
            bool isSdkDefined,
            bool isProjectDefined,
            bool isControlledEquivalent = false,
            string? controlledSha256 = null,
            int? controlledUnixFileMode = null,
            bool isPackageBacked = false)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            Metadata = metadata;
            IsSdkDefined = isSdkDefined;
            IsProjectDefined = isProjectDefined;
            IsControlledEquivalent = isControlledEquivalent;
            ControlledSha256 = controlledSha256;
            ControlledUnixFileMode = controlledUnixFileMode;
            IsPackageBacked = isPackageBacked;
        }

        internal string FullPath { get; }

        internal string RelativePath { get; }

        internal IReadOnlyDictionary<string, string> Metadata { get; }

        internal bool IsSdkDefined { get; }

        internal bool IsProjectDefined { get; }

        internal bool IsControlledEquivalent { get; }

        internal string? ControlledSha256 { get; }

        internal int? ControlledUnixFileMode { get; }

        internal bool IsPackageBacked { get; }
    }

    internal sealed class NoBuildPublishInput
    {
        internal NoBuildPublishInput(
            string evaluationKey,
            string fullPath,
            string relativePath,
            IReadOnlyDictionary<string, string> metadata,
            string sha256,
            string? customAfterMicrosoftCommonTargets = null,
            int? unixFileMode = null,
            bool isPackageBacked = false)
        {
            EvaluationKey = evaluationKey;
            FullPath = fullPath;
            RelativePath = relativePath;
            Metadata = metadata;
            Sha256 = sha256;
            CustomAfterMicrosoftCommonTargets = customAfterMicrosoftCommonTargets;
            UnixFileMode = unixFileMode;
            IsPackageBacked = isPackageBacked;
        }

        internal string EvaluationKey { get; }

        internal string FullPath { get; }

        internal string RelativePath { get; }

        internal IReadOnlyDictionary<string, string> Metadata { get; }

        internal string Sha256 { get; }

        internal string? CustomAfterMicrosoftCommonTargets { get; }

        internal int? UnixFileMode { get; }

        internal bool IsPackageBacked { get; }
    }

    private sealed class ControlledPublishGraphNode
    {
        internal ControlledPublishGraphNode(ProjectEvaluationRequest request, string? pathMap)
        {
            Request = request;
            PathMap = pathMap;
        }

        internal ProjectEvaluationRequest Request { get; }

        internal string? PathMap { get; }
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
        if (request.HasExplicitTargetFramework)
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

    private static bool TryReadEvaluatedPublishInputs(
        ProjectEvaluationRequest request,
        VerifiedPackageInputCatalog? verifiedPackages,
        IReadOnlyCollection<VerifiedPackageInputCatalog> graphVerifiedPackages,
        IReadOnlyCollection<string> trustedBuildInfrastructureRoots,
        IReadOnlyCollection<string> evaluatedBuildInputs,
        IReadOnlyCollection<string> executableMsBuildInputs,
        string? evaluatedPathMap,
        bool proveControlledGeneratedInputs,
        IReadOnlyCollection<ControlledPublishGraphNode> graphBuildNodes,
        out EvaluatedPublishInput[] publishInputs)
    {
        publishInputs = Array.Empty<EvaluatedPublishInput>();
        if (!proveControlledGeneratedInputs &&
            !ContainsPotentialPublishItemMutation(
                request.ProjectPath,
                executableMsBuildInputs,
                trustedBuildInfrastructureRoots,
                verifiedPackages))
        {
            return true;
        }

        return TryReadControlledEvaluatedPublishInputs(
            request,
            verifiedPackages,
            graphVerifiedPackages,
            trustedBuildInfrastructureRoots,
            evaluatedBuildInputs,
            executableMsBuildInputs,
            evaluatedPathMap,
            proveControlledGeneratedInputs,
            graphBuildNodes,
            out publishInputs);
    }

    private static bool TryReadFrozenProjectReferenceGraph(
        ProjectEvaluationRequest rootRequest,
        IReadOnlyDictionary<string, ProjectEvaluationRequest> requestsByEvaluation,
        IReadOnlyDictionary<string, EvaluatedProjectInputs> evaluationsByEvaluation,
        IReadOnlyDictionary<string, string?> pathMapsByEvaluation,
        out ControlledPublishGraphNode[] graphNodes,
        out string[] graphEvaluationKeys)
    {
        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        var orderedKeys = new List<string>();
        string rootKey = rootRequest.BuildVisitKey();
        if (!Visit(rootKey))
        {
            graphNodes = Array.Empty<ControlledPublishGraphNode>();
            graphEvaluationKeys = Array.Empty<string>();
            return false;
        }

        graphEvaluationKeys = orderedKeys.ToArray();
        graphNodes = orderedKeys
            .Where(key => !key.Equals(rootKey, StringComparison.Ordinal))
            .Select(key => new ControlledPublishGraphNode(
                requestsByEvaluation[key],
                pathMapsByEvaluation[key]))
            .ToArray();
        return true;

        bool Visit(string key)
        {
            if (states.TryGetValue(key, out int state))
                return state == 2;
            if (!requestsByEvaluation.TryGetValue(key, out ProjectEvaluationRequest? request) ||
                !evaluationsByEvaluation.TryGetValue(key, out EvaluatedProjectInputs? evaluation))
            {
                return false;
            }

            states[key] = 1;
            foreach (EvaluatedProjectReference reference in evaluation.ProjectReferences)
            {
                if (!File.Exists(reference.ProjectPath))
                    continue;
                string childKey = request.ForProject(reference).BuildVisitKey();
                if (states.TryGetValue(childKey, out int childState) && childState == 1)
                    return false;
                if (!Visit(childKey))
                    return false;
            }
            states[key] = 2;
            orderedKeys.Add(key);
            return true;
        }
    }

    private static bool ContainsPotentialPublishItemMutation(
        string projectPath,
        IEnumerable<string> executableMsBuildInputs,
        IEnumerable<string> trustedBuildInfrastructureRoots,
        VerifiedPackageInputCatalog? verifiedPackages)
    {
        string? gitRoot = ReadGitText(
            Path.GetDirectoryName(projectPath)!,
            "rev-parse --show-toplevel");
        string controlledRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(gitRoot)
                ? Path.GetDirectoryName(projectPath)!
                : gitRoot!);
        foreach (string path in executableMsBuildInputs.Distinct(
                     IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            if (!IsSameOrBelowBuildInputPath(path, controlledRoot) ||
                IsTrustedExternalBuildInfrastructurePath(path, trustedBuildInfrastructureRoots) ||
                verifiedPackages?.TryVerify(path, out _) is true)
            {
                continue;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(path, LoadOptions.None);
            }
            catch
            {
                return true;
            }

            foreach (XElement element in document.Descendants().Where(candidate =>
                         candidate.Ancestors().Any(ancestor =>
                             ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase))))
            {
                if (ControlledPublishFileItemNames.Contains(element.Name.LocalName))
                    return true;
                if ((element.Name.LocalName.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("None", StringComparison.OrdinalIgnoreCase)) &&
                    element.Attributes().Any(attribute =>
                        IsPublishItemSelectionAttribute(attribute.Name.LocalName) ||
                        attribute.Name.LocalName.Equals("CopyToPublishDirectory", StringComparison.OrdinalIgnoreCase)) &&
                    (element.Attributes().Any(attribute =>
                         attribute.Name.LocalName.Equals("CopyToPublishDirectory", StringComparison.OrdinalIgnoreCase)) ||
                     element.Elements().Any(metadata =>
                         metadata.Name.LocalName.Equals("CopyToPublishDirectory", StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }
                if (!element.Name.LocalName.Equals("Output", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? itemName = element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("ItemName", StringComparison.OrdinalIgnoreCase))?.Value;
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;
                string decoded = DecodeMsBuildEscapes(itemName!).Trim();
                if (ContainsUnresolvedBuildExpression(decoded) ||
                    ControlledPublishFileItemNames.Contains(decoded) ||
                    decoded.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
                    decoded.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPublishItemSelectionAttribute(string name)
        => name.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Update", StringComparison.OrdinalIgnoreCase);

    internal static bool IsControlledPublishRelativePath(string value)
    {
        string candidate = DecodeMsBuildEscapes(value).Trim().Trim('\'', '"');
        if (candidate.Length == 0 ||
            Path.IsPathRooted(candidate) ||
            ContainsUnresolvedBuildExpression(candidate) ||
            ContainsUncontrolledEnvironmentReference(candidate) ||
            ContainsUncontrolledAmbientPropertyFunction(candidate))
        {
            return false;
        }

        try
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "powerforge-publish-root"));
            string destination = Path.GetFullPath(Path.Combine(root, candidate));
            return IsSameOrBelowBuildInputPath(destination, root);
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
