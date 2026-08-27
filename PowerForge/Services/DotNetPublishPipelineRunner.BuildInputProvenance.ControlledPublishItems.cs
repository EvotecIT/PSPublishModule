using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryReadControlledEvaluatedPublishInputs(
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
        string controlledOutputRoot = Path.Combine(
            Path.GetTempPath(),
            "powerforge-publish-inputs-" + Guid.NewGuid().ToString("N"));
        string controlledSourceRoot = Path.Combine(controlledOutputRoot, "source");
        string? controlledGitRoot = null;
        try
        {
            Directory.CreateDirectory(controlledOutputRoot);
            if (!TryCreateControlledSourceCheckout(
                    request.ProjectPath,
                    controlledSourceRoot,
                    evaluatedBuildInputs,
                    executableMsBuildInputs,
                    request.ReadEffectiveGlobalProperties(),
                    BuildControlledPublishProjectContexts(request, graphBuildNodes),
                    out controlledGitRoot,
                    out string? controlledProjectPath))
            {
                return false;
            }
            if (!TryCreateControlledBuildEnvironment(
                    request.EnvironmentVariables,
                    controlledGitRoot!,
                    controlledSourceRoot,
                    Path.GetDirectoryName(request.ProjectPath)!,
                    out IReadOnlyDictionary<string, string?> controlledEnvironment))
            {
                return false;
            }
            if (!TryCreateControlledPublishInputPlaceholders(
                    controlledGitRoot!,
                    controlledSourceRoot,
                    controlledProjectPath!,
                    executableMsBuildInputs,
                    request.GlobalProperties))
            {
                return false;
            }

            string offlinePackageSource = Directory.CreateDirectory(
                Path.Combine(controlledOutputRoot, "packages-source")).FullName;
            var offlinePackageSources = new List<string>();
            int packageCatalogIndex = 0;
            foreach (VerifiedPackageInputCatalog packageCatalog in graphVerifiedPackages)
            {
                string catalogSource = Directory.CreateDirectory(Path.Combine(
                    offlinePackageSource,
                    packageCatalogIndex++.ToString(System.Globalization.CultureInfo.InvariantCulture))).FullName;
                if (!packageCatalog.TrySeedControlledPackageSource(
                        catalogSource,
                        controlledSourceRoot,
                        controlledProjectPath!,
                        out string[] catalogSources))
                {
                    return false;
                }
                offlinePackageSources.AddRange(catalogSources);
            }
            if (offlinePackageSources.Count == 0)
                offlinePackageSources.Add(offlinePackageSource);
            string[] distinctOfflinePackageSources = offlinePackageSources
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            string controlledNuGetConfig = Path.Combine(controlledOutputRoot, "NuGet.Config");
            new XDocument(
                new XElement("configuration",
                    new XElement("packageSources",
                        new XElement("clear"),
                        distinctOfflinePackageSources.Select((source, index) =>
                            new XElement("add",
                                new XAttribute("key", "verified-" + index),
                                new XAttribute("value", source)))),
                    new XElement("auditSources", new XElement("clear"))))
                .Save(controlledNuGetConfig);
            string offlinePackageSourceList = string.Join(";", distinctOfflinePackageSources);

            if (!TryBuildControlledPublishProjectGraph(
                    graphBuildNodes,
                    controlledGitRoot!,
                    controlledSourceRoot,
                    controlledEnvironment,
                    controlledNuGetConfig,
                    offlinePackageSourceList,
                    controlledOutputRoot))
            {
                return false;
            }

            var arguments = new List<string>
            {
                "msbuild",
                controlledProjectPath!,
                "-nologo",
                "-verbosity:quiet",
                "-restore",
                "-target:Build;ComputeFilesToPublish",
                "-getItem:ResolvedFileToPublish"
            };
            if (!TryAppendControlledProjectEvaluationProperties(
                    arguments,
                    request,
                    controlledGitRoot!,
                    controlledSourceRoot))
            {
                return false;
            }
            arguments.Add("-p:BuildProjectReferences=false");
            if (!TryBuildControlledPathMap(
                    controlledSourceRoot,
                    controlledGitRoot!,
                    evaluatedPathMap,
                    out string controlledPathMap))
            {
                return false;
            }
            arguments.Add("-p:PathMap=" + EscapeMsBuildPropertyValue(controlledPathMap));
            AppendControlledProofSafeguards(
                arguments,
                controlledNuGetConfig,
                offlinePackageSourceList,
                Path.Combine(controlledOutputRoot, "packages.lock.json"));

            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(controlledProjectPath!)!,
                arguments,
                controlledEnvironment,
                TimeSpan.FromMinutes(5));
            if (process.ExitCode != 0 || process.TimedOut)
            {
                return false;
            }

            int itemsMarker = process.StdOut.LastIndexOf("\"Items\"", StringComparison.Ordinal);
            int jsonStart = itemsMarker < 0
                ? -1
                : process.StdOut.LastIndexOf('{', itemsMarker);
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return false;
            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            if (!document.RootElement.TryGetProperty("Items", out JsonElement items) ||
                !items.TryGetProperty("ResolvedFileToPublish", out JsonElement resolvedFiles) ||
                resolvedFiles.ValueKind != JsonValueKind.Array)
            {
                return true;
            }

            var results = new List<EvaluatedPublishInput>();
            string controlledProjectDirectory = Path.GetDirectoryName(controlledProjectPath!)!;
            string controlledPackageRoot = controlledEnvironment.TryGetValue(
                    "NUGET_PACKAGES",
                    out string? packageRoot)
                ? Path.GetFullPath(packageRoot!)
                : string.Empty;
            foreach (JsonElement value in resolvedFiles.EnumerateArray())
            {
                EvaluatedProjectItem? item = ReadEvaluatedProjectItem(
                    value,
                    controlledProjectDirectory);
                if (item is null ||
                    !item.Metadata.TryGetValue("RelativePath", out string? relativePath) ||
                    !IsControlledPublishRelativePath(relativePath))
                {
                    return false;
                }

                if ((!string.IsNullOrEmpty(controlledPackageRoot) &&
                     IsSameOrBelowBuildInputPath(item.FullPath, controlledPackageRoot)) ||
                    IsSameOrBelowBuildInputPath(item.FullPath, offlinePackageSource) ||
                    IsTrustedExternalBuildInfrastructurePath(
                        item.FullPath,
                        trustedBuildInfrastructureRoots))
                {
                    continue;
                }
                if (!IsSameOrBelowBuildInputPath(item.FullPath, controlledSourceRoot))
                    return false;

                string relativeInputPath = FrameworkCompatibility.GetRelativePath(
                    controlledSourceRoot,
                    item.FullPath);
                string originalInputPath = Path.GetFullPath(
                    Path.Combine(controlledGitRoot!, relativeInputPath));
                if (!IsSameOrBelowBuildInputPath(originalInputPath, controlledGitRoot!))
                    return false;

                bool isSdkDefined = false;
                if (item.Metadata.TryGetValue(
                        "DefiningProjectFullPath",
                        out string? definingProject) &&
                    !string.IsNullOrWhiteSpace(definingProject))
                {
                    isSdkDefined = IsTrustedExternalBuildInfrastructurePath(
                        definingProject!,
                        trustedBuildInfrastructureRoots);
                }
                bool isControlledEquivalent = isSdkDefined &&
                    File.Exists(originalInputPath) &&
                    File.Exists(item.FullPath) &&
                    AreControlledGeneratedOutputsEquivalent(originalInputPath, item.FullPath);
                string? controlledSha256 = isControlledEquivalent
                    ? ComputeSha256Hex(File.ReadAllBytes(item.FullPath))
                    : null;
                if (isSdkDefined && !proveControlledGeneratedInputs)
                    continue;
                results.Add(new EvaluatedPublishInput(
                    originalInputPath,
                    relativePath,
                    item.Metadata,
                    isSdkDefined,
                    isControlledEquivalent,
                    controlledSha256));
            }

            publishInputs = results.ToArray();
            return true;
        }
        catch
        {
            publishInputs = Array.Empty<EvaluatedPublishInput>();
            return false;
        }
        finally
        {
            RemoveControlledSourceCheckout(controlledGitRoot, controlledSourceRoot);
            try
            {
                if (Directory.Exists(controlledOutputRoot))
                    Directory.Delete(controlledOutputRoot, recursive: true);
            }
            catch
            {
                // Temporary controlled-build cleanup is best effort.
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]>
        BuildControlledPublishProjectContexts(
            ProjectEvaluationRequest rootRequest,
            IReadOnlyCollection<ControlledPublishGraphNode> graphBuildNodes)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return graphBuildNodes
            .Select(node => node.Request)
            .Concat(new[] { rootRequest })
            .GroupBy(node => Path.GetFullPath(node.ProjectPath), comparer)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(node => node.ReadEffectiveGlobalProperties())
                    .GroupBy(
                        properties => string.Join("\n", properties.OrderBy(
                            property => property.Key,
                            StringComparer.OrdinalIgnoreCase).Select(property =>
                            property.Key + "=" + property.Value)),
                        StringComparer.Ordinal)
                    .Select(context => (IReadOnlyDictionary<string, string>)context.First())
                    .ToArray(),
                comparer);
    }

    private static bool TryBuildControlledPublishProjectGraph(
        IReadOnlyCollection<ControlledPublishGraphNode> graphBuildNodes,
        string originalGitRoot,
        string controlledSourceRoot,
        IReadOnlyDictionary<string, string?> controlledEnvironment,
        string controlledNuGetConfig,
        string offlinePackageSourceList,
        string controlledOutputRoot)
    {
        foreach (ControlledPublishGraphNode node in graphBuildNodes)
        {
            string originalProjectPath = Path.GetFullPath(node.Request.ProjectPath);
            if (!IsSameOrBelowBuildInputPath(originalProjectPath, originalGitRoot))
                return false;
            string controlledProjectPath = Path.GetFullPath(Path.Combine(
                controlledSourceRoot,
                FrameworkCompatibility.GetRelativePath(originalGitRoot, originalProjectPath)));
            if (!IsSameOrBelowBuildInputPath(controlledProjectPath, controlledSourceRoot) ||
                !File.Exists(controlledProjectPath))
            {
                return false;
            }

            var arguments = new List<string>
            {
                "msbuild",
                controlledProjectPath,
                "-nologo",
                "-maxCpuCount:1",
                "-nodeReuse:false",
                "-verbosity:quiet",
                "-restore",
                "-target:Build"
            };
            if (!TryAppendControlledProjectEvaluationProperties(
                    arguments,
                    node.Request,
                    originalGitRoot,
                    controlledSourceRoot))
            {
                return false;
            }
            arguments.Add("-p:BuildProjectReferences=false");
            if (!TryBuildControlledPathMap(
                    controlledSourceRoot,
                    originalGitRoot,
                    node.PathMap,
                    out string controlledPathMap))
            {
                return false;
            }
            arguments.Add("-p:PathMap=" + EscapeMsBuildPropertyValue(controlledPathMap));
            AppendControlledProofSafeguards(
                arguments,
                controlledNuGetConfig,
                offlinePackageSourceList,
                Path.Combine(controlledOutputRoot, "packages.lock.json"));

            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(controlledProjectPath)!,
                arguments,
                controlledEnvironment,
                TimeSpan.FromMinutes(5));
            if (process.ExitCode != 0 || process.TimedOut)
                return false;
        }
        return true;
    }

    private static bool TryAppendControlledProjectEvaluationProperties(
        ICollection<string> arguments,
        ProjectEvaluationRequest request,
        string gitRoot,
        string controlledSourceRoot)
    {
        string inputBaseDirectory = Path.GetDirectoryName(request.ProjectPath)!;
        if (request.Configuration is not null)
        {
            if (!TryRemapControlledBuildValue(
                    request.Configuration,
                    gitRoot,
                    controlledSourceRoot,
                    inputBaseDirectory,
                    out string controlledConfiguration))
            {
                return false;
            }
            arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(controlledConfiguration));
        }
        if (request.HasExplicitTargetFramework)
        {
            if (!TryRemapControlledBuildValue(
                    request.TargetFramework!,
                    gitRoot,
                    controlledSourceRoot,
                    inputBaseDirectory,
                    out string controlledTargetFramework))
            {
                return false;
            }
            arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(controlledTargetFramework));
        }
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("MSBuildToolsPath", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("MSBuildSDKsPath", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("BuildProjectReferences", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("PathMap", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!TryRemapControlledBuildValue(
                    property.Value,
                    gitRoot,
                    controlledSourceRoot,
                    inputBaseDirectory,
                    out string controlledValue))
            {
                return false;
            }
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(controlledValue));
        }

        return true;
    }

    private static bool TryCreateControlledPublishInputPlaceholders(
        string gitRoot,
        string controlledSourceRoot,
        string controlledProjectPath,
        IReadOnlyCollection<string> executableMsBuildInputs,
        IReadOnlyDictionary<string, string> evaluatedGlobalProperties)
    {
        try
        {
            var documents = new List<(XDocument Document, string DeclaringPath)>();
            foreach (string originalPath in executableMsBuildInputs)
            {
                string fullOriginalPath = Path.GetFullPath(originalPath);
                if (!IsSameOrBelowBuildInputPath(fullOriginalPath, gitRoot))
                    continue;
                string relativePath = FrameworkCompatibility.GetRelativePath(gitRoot, fullOriginalPath);
                string controlledPath = Path.GetFullPath(Path.Combine(controlledSourceRoot, relativePath));
                if (!IsSameOrBelowBuildInputPath(controlledPath, controlledSourceRoot) ||
                    !File.Exists(controlledPath))
                {
                    continue;
                }
                documents.Add((XDocument.Load(controlledPath, LoadOptions.None), controlledPath));
            }

            string controlledProjectDirectory = Path.GetDirectoryName(controlledProjectPath)!;
            foreach ((XDocument document, string declaringPath) in documents)
            {
                foreach (XElement item in document.Descendants().Where(IsTargetTimePublishFileItem))
                {
                    foreach (XAttribute include in item.Attributes().Where(attribute =>
                                 attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!TryExpandControlledTaskInputValues(
                                include.Value,
                                declaringPath,
                                controlledProjectDirectory,
                                documents,
                                evaluatedGlobalProperties,
                                out string[] expandedValues))
                        {
                            return false;
                        }
                        foreach (string value in expandedValues.SelectMany(expanded =>
                                     DecodeMsBuildEscapes(expanded).Split(';')))
                        {
                            string candidate = value.Trim().Trim('\'', '"');
                            if (candidate.Length == 0)
                                continue;
                            if (!TryResolveControlledTaskInputPath(
                                    candidate,
                                    declaringPath,
                                    controlledProjectDirectory,
                                    controlledSourceRoot,
                                    controlledSourceRoot,
                                    out string inputPath))
                            {
                                return false;
                            }
                            if (File.Exists(inputPath))
                            {
                                if (HasReparsePointBelowRoot(inputPath, controlledSourceRoot))
                                    return false;
                                continue;
                            }
                            if (Directory.Exists(inputPath))
                                return false;
                            string parentDirectory = Directory.CreateDirectory(
                                Path.GetDirectoryName(inputPath)!).FullName;
                            if (HasReparsePointBelowRoot(parentDirectory, controlledSourceRoot))
                                return false;
                            using (File.Create(inputPath))
                            {
                            }
                        }
                    }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTargetTimePublishFileItem(XElement element)
    {
        if (!element.Ancestors().Any(ancestor =>
                ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (ControlledPublishFileItemNames.Contains(element.Name.LocalName))
            return true;
        if (!element.Name.LocalName.Equals("Content", StringComparison.OrdinalIgnoreCase) &&
            !element.Name.LocalName.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return element.Attributes().Any(attribute =>
                   attribute.Name.LocalName.Equals("CopyToPublishDirectory", StringComparison.OrdinalIgnoreCase)) ||
               element.Elements().Any(metadata =>
                   metadata.Name.LocalName.Equals("CopyToPublishDirectory", StringComparison.OrdinalIgnoreCase));
    }
}
