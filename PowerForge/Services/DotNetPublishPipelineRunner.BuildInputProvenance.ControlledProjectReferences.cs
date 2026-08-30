using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryReadControlledResolvedProjectReferences(
        ProjectEvaluationRequest request,
        VerifiedPackageInputCatalog? verifiedPackages,
        IReadOnlyCollection<string> evaluatedBuildInputs,
        IReadOnlyCollection<string> executableMsBuildInputs,
        IReadOnlyCollection<EvaluatedProjectReference> evaluatedProjectReferences,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> projectReferenceDeclarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        bool allowAmbiguousEvaluatedAssignments,
        string? originalIntermediateRoot,
        string? customAfterMicrosoftCommonTargets,
        out EvaluatedProjectReference[] references,
        out string resolvedItemsJson)
    {
        references = Array.Empty<EvaluatedProjectReference>();
        resolvedItemsJson = string.Empty;
        string controlledOutputRoot = Path.Combine(
            Path.GetTempPath(),
            "powerforge-project-references-" + Guid.NewGuid().ToString("N"));
        string controlledSourceRoot = Path.Combine(controlledOutputRoot, "source");
        string? originalGitRoot = null;
        try
        {
            Directory.CreateDirectory(controlledOutputRoot);
            string? contextGitRoot = ReadGitText(
                Path.GetDirectoryName(request.ProjectPath)!,
                "rev-parse --show-toplevel");
            if (string.IsNullOrWhiteSpace(contextGitRoot))
                return false;
            string? trackedInputList = ReadGitRawText(contextGitRoot!, "ls-files -z");
            if (trackedInputList is null)
                return false;
            var trackedInputPaths = new HashSet<string>(
                trackedInputList.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Replace('\\', '/').TrimStart('/')),
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            string[] controlledConditionPropertyNames = ReadControlledBuildPropertyNames(
                executableMsBuildInputs
                    .Where(path =>
                    {
                        string? relativePath = ToGitRelativeExclusion(
                            contextGitRoot!,
                            contextGitRoot!,
                            path);
                        return relativePath is not null &&
                               trackedInputPaths.Contains(relativePath.Replace('\\', '/'));
                    })
                    .Append(request.ProjectPath));
            var controlledConditionPropertyNameSet = new HashSet<string>(
                controlledConditionPropertyNames,
                StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, string> BuildProjectContext(
                ProjectEvaluationRequest contextRequest,
                IReadOnlyDictionary<string, string>? knownEvaluatedProperties = null)
            {
                var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                IReadOnlyDictionary<string, string> evaluatedContext =
                    contextRequest.BuildControlledEvaluationProperties(
                        knownEvaluatedProperties ?? ReadEvaluatedProjectProperties(
                            contextRequest,
                            controlledConditionPropertyNames));
                foreach (KeyValuePair<string, string> property in evaluatedContext)
                {
                    if (controlledConditionPropertyNameSet.Contains(property.Key))
                        properties[property.Key] = property.Value;
                }
                if (!properties.ContainsKey("TargetFramework") &&
                    contextRequest.HasExplicitTargetFramework)
                {
                    properties["TargetFramework"] = contextRequest.TargetFramework!;
                }
                return properties;
            }

            Dictionary<string, IReadOnlyDictionary<string, string>[]> projectContexts =
                evaluatedProjectReferences
                    .Where(reference => File.Exists(reference.ProjectPath))
                    .GroupBy(
                        reference => Path.GetFullPath(reference.ProjectPath),
                        IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .Select(reference =>
                            {
                                ProjectEvaluationRequest contextRequest = request.ForProject(reference);
                                return BuildProjectContext(contextRequest);
                            })
                            .GroupBy(
                                properties => string.Join("\n", properties.OrderBy(
                                    property => property.Key,
                                    StringComparer.OrdinalIgnoreCase).Select(property =>
                                        property.Key + "=" + property.Value)),
                                StringComparer.Ordinal)
                            .Select(context => (IReadOnlyDictionary<string, string>)context.First())
                            .ToArray(),
                        IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            string rootProjectPath = Path.GetFullPath(request.ProjectPath);
            IReadOnlyDictionary<string, string> rootContext = BuildProjectContext(
                request,
                evaluatedConditionProperties);
            projectContexts[rootProjectPath] = projectContexts.TryGetValue(
                    rootProjectPath,
                    out IReadOnlyDictionary<string, string>[]? existingRootContexts)
                ? existingRootContexts
                    .Append(rootContext)
                    .GroupBy(
                        properties => string.Join("\n", properties.OrderBy(
                            property => property.Key,
                            StringComparer.OrdinalIgnoreCase).Select(property =>
                                property.Key + "=" + property.Value)),
                        StringComparer.Ordinal)
                    .Select(context => (IReadOnlyDictionary<string, string>)context.First())
                    .ToArray()
                : [rootContext];
            if (!TryCreateControlledSourceCheckout(
                    request.ProjectPath,
                    controlledSourceRoot,
                    evaluatedBuildInputs,
                    executableMsBuildInputs,
                    request.ReadEffectiveGlobalProperties(),
                    projectContexts,
                    out originalGitRoot,
                    out string? controlledProjectPath))
                return false;
            if (!TryCreateControlledBuildEnvironment(
                    request.EnvironmentVariables,
                    request.ControlledBuildEnvironmentVariableNames,
                    originalGitRoot!,
                    controlledSourceRoot,
                    Path.GetDirectoryName(request.ProjectPath)!,
                    out IReadOnlyDictionary<string, string?> controlledEnvironment))
            {
                return false;
            }

            string offlinePackageSource = Directory.CreateDirectory(
                Path.Combine(controlledOutputRoot, "packages-source")).FullName;
            string[] offlinePackageSources = { offlinePackageSource };
            if (verifiedPackages is not null &&
                !verifiedPackages.TrySeedControlledPackageSource(
                    offlinePackageSource,
                    controlledSourceRoot,
                    controlledProjectPath!,
                    out offlinePackageSources,
                    allowSdkManagedToolchainPackages: true))
            {
                return false;
            }
            string controlledNuGetConfig = Path.Combine(controlledOutputRoot, "NuGet.Config");
            new XDocument(
                new XElement("configuration",
                    new XElement("packageSources",
                        new XElement("clear"),
                        offlinePackageSources.Select((source, index) =>
                            new XElement("add",
                                new XAttribute("key", "verified-" + index),
                                new XAttribute("value", source)))),
                    new XElement("auditSources", new XElement("clear"))))
                .Save(controlledNuGetConfig);
            string offlinePackageSourceList = string.Join(";", offlinePackageSources);
            string controlledReferenceTargets = Path.Combine(
                controlledOutputRoot,
                "PowerForge.ControlledProjectReferences.targets");
            string? controlledCustomAfterTargets = null;
            if (!string.IsNullOrWhiteSpace(customAfterMicrosoftCommonTargets))
            {
                string originalCustomAfterTargets = Path.GetFullPath(
                    customAfterMicrosoftCommonTargets!);
                StringComparer pathComparer = IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal;
                if (executableMsBuildInputs.Contains(originalCustomAfterTargets, pathComparer))
                {
                    if (!IsSameOrBelowBuildInputPath(originalCustomAfterTargets, originalGitRoot!))
                        return false;

                    string relativeCustomAfterTargets = FrameworkCompatibility.GetRelativePath(
                        originalGitRoot!,
                        originalCustomAfterTargets);
                    controlledCustomAfterTargets = Path.GetFullPath(Path.Combine(
                        controlledSourceRoot,
                        relativeCustomAfterTargets));
                    if (!IsSameOrBelowBuildInputPath(
                            controlledCustomAfterTargets,
                            controlledSourceRoot) ||
                        !File.Exists(controlledCustomAfterTargets))
                    {
                        return false;
                    }
                }
            }

            var controlledTargets = new XElement("Project");
            if (controlledCustomAfterTargets is not null)
            {
                controlledTargets.Add(new XElement(
                    "Import",
                    new XAttribute("Project", controlledCustomAfterTargets)));
            }
            controlledTargets.Add(
                new XElement("Target",
                    new XAttribute("Name", "PowerForgeEnterControlledProjectReferenceResolution"),
                    new XAttribute("BeforeTargets", "ResolveProjectReferences"),
                    new XElement("PropertyGroup",
                        new XElement("_PowerForgeRequestedBuildProjectReferences",
                            "$(BuildProjectReferences)"),
                        new XElement("BuildProjectReferences", "false"))),
                new XElement("Target",
                    new XAttribute("Name", "PowerForgeLeaveControlledProjectReferenceResolution"),
                    new XAttribute("AfterTargets", "ResolveProjectReferences"),
                    new XElement("PropertyGroup",
                        new XElement("BuildProjectReferences",
                            "$(_PowerForgeRequestedBuildProjectReferences)"))),
                new XElement("Target",
                    new XAttribute("Name", "PowerForgeCaptureControlledProjectReferences"),
                    new XAttribute("BeforeTargets", "ResolveReferences"),
                    new XAttribute("DependsOnTargets", "$(ResolveReferencesDependsOn)"),
                    new XElement("ItemGroup",
                        new XElement(
                            "_PowerForgeControlledProjectReference",
                            new XAttribute("Include", "@(ProjectReference)")))));
            new XDocument(
                controlledTargets)
                .Save(controlledReferenceTargets);
            string controlledIntermediateRoot = Directory.CreateDirectory(
                Path.Combine(controlledOutputRoot, "intermediate")).FullName;

            var arguments = new List<string>
            {
                "msbuild",
                controlledProjectPath!,
                "-nologo",
                "-verbosity:quiet",
                "-restore",
                "-target:ResolveReferences",
                "-getItem:_MSBuildProjectReferenceExistent",
                "-getItem:_PowerForgeControlledProjectReference",
                "-getItem:ProjectReference"
            };
            foreach (string itemName in EvaluatedBuildItemNames)
            {
                if (!itemName.Equals("ProjectReference", StringComparison.Ordinal) &&
                    !itemName.Equals("_MSBuildProjectReferenceExistent", StringComparison.Ordinal))
                {
                    arguments.Add("-getItem:" + itemName);
                }
            }
            if (!TryAppendControlledProjectEvaluationProperties(
                    arguments,
                    request,
                    originalGitRoot!,
                    controlledSourceRoot))
            {
                return false;
            }
            AddProjectReferenceExecutionProperties(
                arguments,
                request,
                preservePublishBuildProjectReferences: true);
            AppendControlledProofSafeguards(
                arguments,
                controlledNuGetConfig,
                offlinePackageSourceList,
                Path.Combine(controlledOutputRoot, "packages.lock.json"));
            arguments.Add("-p:RestoreRecursive=false");
            arguments.Add("-p:BaseIntermediateOutputPath=" +
                EscapeMsBuildPropertyValue(controlledIntermediateRoot + Path.DirectorySeparatorChar));
            arguments.Add("-p:MSBuildProjectExtensionsPath=" +
                EscapeMsBuildPropertyValue(controlledIntermediateRoot + Path.DirectorySeparatorChar));
            arguments.Add("-p:IntermediateOutputPath=" +
                EscapeMsBuildPropertyValue(controlledIntermediateRoot + Path.DirectorySeparatorChar));
            arguments.Add("-p:CustomAfterMicrosoftCommonTargets=" +
                EscapeMsBuildPropertyValue(controlledReferenceTargets));

            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(controlledProjectPath!)!,
                arguments,
                controlledEnvironment,
                TimeSpan.FromMinutes(5));
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
            if (!controlledEnvironment.TryGetValue("NUGET_PACKAGES", out string? controlledPackageRoot) ||
                string.IsNullOrWhiteSpace(controlledPackageRoot))
            {
                return false;
            }
            if (!TryMapControlledResolutionItems(
                    items,
                    controlledOutputRoot,
                    controlledSourceRoot,
                    originalGitRoot!,
                    controlledIntermediateRoot,
                    originalIntermediateRoot,
                    controlledPackageRoot!,
                    verifiedPackages,
                    out resolvedItemsJson))
                return false;
            using JsonDocument mappedDocument = JsonDocument.Parse(resolvedItemsJson);
            if (!TryReadControlledProjectReferences(
                    mappedDocument.RootElement,
                    request.ProjectPath,
                    executableMsBuildInputs,
                    projectReferenceDeclarations,
                    evaluatedConditionProperties,
                    taskWidePropertyRemovals,
                    allowAmbiguousEvaluatedAssignments,
                    out EvaluatedProjectReference[] controlledReferences))
                return false;
            references = controlledReferences
                .GroupBy(BuildEvaluatedProjectReferenceKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            return true;
        }
        catch
        {
            references = Array.Empty<EvaluatedProjectReference>();
            resolvedItemsJson = string.Empty;
            return false;
        }
        finally
        {
            RemoveControlledSourceCheckout(originalGitRoot, controlledSourceRoot);
            try
            {
                if (Directory.Exists(controlledOutputRoot))
                    Directory.Delete(controlledOutputRoot, recursive: true);
            }
            catch
            {
                // Temporary controlled-reference cleanup is best effort.
            }
        }
    }

    private static bool TryMapControlledResolutionItems(
        JsonElement items,
        string controlledOutputRoot,
        string controlledSourceRoot,
        string originalGitRoot,
        string controlledIntermediateRoot,
        string? originalIntermediateRoot,
        string controlledPackageRoot,
        VerifiedPackageInputCatalog? verifiedPackages,
        out string mappedItemsJson)
    {
        mappedItemsJson = string.Empty;
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (JsonProperty itemList in items.EnumerateObject())
                {
                    writer.WritePropertyName(itemList.Name);
                    if (itemList.Value.ValueKind != JsonValueKind.Array)
                        return false;
                    writer.WriteStartArray();
                    foreach (JsonElement item in itemList.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                            return false;
                        var mappedStrings = new Dictionary<string, string>(StringComparer.Ordinal);
                        bool proofOnlyItem = false;
                        foreach (JsonProperty metadata in item.EnumerateObject())
                        {
                            if (metadata.Value.ValueKind != JsonValueKind.String)
                                continue;
                            string value = metadata.Value.GetString() ?? string.Empty;
                            if (!TryMapControlledResolutionValue(
                                    value,
                                    controlledSourceRoot,
                                    originalGitRoot,
                                    controlledIntermediateRoot,
                                    originalIntermediateRoot,
                                    controlledPackageRoot,
                                    verifiedPackages,
                                    out string mappedValue))
                            {
                                return false;
                            }
                            mappedStrings[metadata.Name] = mappedValue;
                            if ((metadata.Name.Equals("Identity", StringComparison.OrdinalIgnoreCase) ||
                                 metadata.Name.Equals("FullPath", StringComparison.OrdinalIgnoreCase)) &&
                                ContainsControlledResolutionPath(mappedValue, controlledOutputRoot))
                            {
                                proofOnlyItem = true;
                            }
                        }
                        if (proofOnlyItem)
                            continue;

                        writer.WriteStartObject();
                        foreach (JsonProperty metadata in item.EnumerateObject())
                        {
                            writer.WritePropertyName(metadata.Name);
                            if (metadata.Value.ValueKind != JsonValueKind.String)
                            {
                                metadata.Value.WriteTo(writer);
                                continue;
                            }
                            writer.WriteStringValue(mappedStrings[metadata.Name]);
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            mappedItemsJson = Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
        catch
        {
            mappedItemsJson = string.Empty;
            return false;
        }
    }

    internal static bool TryMapControlledResolutionItemsForTest(
        JsonElement items,
        string controlledOutputRoot,
        string controlledSourceRoot,
        string originalGitRoot,
        string controlledIntermediateRoot,
        string? originalIntermediateRoot,
        string controlledPackageRoot,
        out string mappedItemsJson)
        => TryMapControlledResolutionItems(
            items,
            controlledOutputRoot,
            controlledSourceRoot,
            originalGitRoot,
            controlledIntermediateRoot,
            originalIntermediateRoot,
            controlledPackageRoot,
            verifiedPackages: null,
            out mappedItemsJson);

    private static bool ContainsControlledResolutionPath(string value, string controlledOutputRoot)
    {
        try
        {
            return Path.IsPathRooted(value) &&
                   IsSameOrBelowBuildInputPath(value, controlledOutputRoot);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryMapControlledResolutionValue(
        string value,
        string controlledSourceRoot,
        string originalGitRoot,
        string controlledIntermediateRoot,
        string? originalIntermediateRoot,
        string controlledPackageRoot,
        VerifiedPackageInputCatalog? verifiedPackages,
        out string mappedValue)
    {
        mappedValue = originalIntermediateRoot is null
            ? value
            : ReplaceControlledPathRoot(
                value,
                controlledIntermediateRoot,
                originalIntermediateRoot);
        if (!string.Equals(mappedValue, value, StringComparison.Ordinal))
            return true;
        mappedValue = ReplaceControlledPathRoot(value, controlledSourceRoot, originalGitRoot);
        if (!string.Equals(mappedValue, value, StringComparison.Ordinal))
            return true;
        if (!Path.IsPathRooted(value) ||
            !IsSameOrBelowBuildInputPath(value, controlledPackageRoot))
        {
            return true;
        }
        return verifiedPackages is not null &&
               verifiedPackages.TryMapControlledPackageInput(
                   value,
                   controlledPackageRoot,
                   out mappedValue);
    }

    private static string ReplaceControlledPathRoot(
        string value,
        string controlledRoot,
        string originalRoot)
    {
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        int index = value.IndexOf(controlledRoot, comparison);
        if (index < 0)
            return value;
        var mapped = new StringBuilder(value.Length - controlledRoot.Length + originalRoot.Length);
        int offset = 0;
        while (index >= 0)
        {
            mapped.Append(value, offset, index - offset);
            mapped.Append(originalRoot);
            offset = index + controlledRoot.Length;
            index = value.IndexOf(controlledRoot, offset, comparison);
        }
        mapped.Append(value, offset, value.Length - offset);
        return mapped.ToString();
    }

    private static bool TryReadControlledProjectReferences(
        JsonElement items,
        string projectPath,
        IReadOnlyCollection<string> propertyDefinitionPaths,
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> projectReferenceDeclarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        bool allowAmbiguousEvaluatedAssignments,
        out EvaluatedProjectReference[] references)
    {
        references = Array.Empty<EvaluatedProjectReference>();
        JsonElement projectReferences = default;
        bool hasProjectReferences =
            items.TryGetProperty(
                "_PowerForgeControlledProjectReference",
                out projectReferences) ||
            items.TryGetProperty("ProjectReference", out projectReferences);
        if (hasProjectReferences && projectReferences.ValueKind != JsonValueKind.Array)
            return false;
        var nonOutputProjectPaths = new HashSet<string>(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (hasProjectReferences)
        {
            foreach (JsonElement item in projectReferences.EnumerateArray())
            {
                if (!string.Equals(
                        ReadItemText(item, "ReferenceOutputAssembly"),
                        "false",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(ReadItemText(item, "OutputItemType")))
                {
                    continue;
                }
                string? fullPath = ReadItemText(item, "FullPath");
                if (!string.IsNullOrWhiteSpace(fullPath))
                    nonOutputProjectPaths.Add(Path.GetFullPath(fullPath!));
            }
        }
        if (!TryReadResolvedProjectReferences(
                items,
                projectPath,
                Array.Empty<string>(),
                Array.Empty<PreprocessedProjectReferenceDeclaration>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                taskWidePropertyRemovals,
                allowAmbiguousEvaluatedAssignments: true,
                out EvaluatedProjectReference[] resolvedReferences))
        {
            return false;
        }

        var capturedReferences = new List<EvaluatedProjectReference>();
        if (hasProjectReferences)
        {
            foreach (JsonElement item in projectReferences.EnumerateArray())
            {
                if (string.Equals(
                        ReadItemText(item, "ReferenceOutputAssembly"),
                        "false",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(ReadItemText(item, "OutputItemType")))
                {
                    // ResolveProjectReferences intentionally drops non-output references. Their
                    // explicitly declared alternative outputs are handled by the controlled item
                    // vector; retaining the raw project here would incorrectly recurse into a
                    // project that cannot contribute to the published output.
                    continue;
                }
                if (!TryReadEvaluatedProjectReferences(
                        item,
                        projectPath,
                        Array.Empty<string>(),
                        Array.Empty<PreprocessedProjectReferenceDeclaration>(),
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        taskWidePropertyRemovals,
                        preferEffectiveLiteralAssignments: false,
                        allowAmbiguousEvaluatedAssignments: true,
                        out EvaluatedProjectReference[] itemReferences))
                {
                    return false;
                }
                foreach (EvaluatedProjectReference reference in itemReferences)
                    capturedReferences.Add(reference);
            }
        }

        EvaluatedProjectReference[] participatingResolvedReferences = resolvedReferences
            .Where(reference => !nonOutputProjectPaths.Contains(Path.GetFullPath(reference.ProjectPath)))
            .ToArray();
        var controlledReferences = MergeResolvedProjectReferenceContexts(
                capturedReferences,
                participatingResolvedReferences,
                Array.Empty<EvaluatedProjectReference>(),
                new HashSet<string>(StringComparer.Ordinal))
            .ToDictionary(BuildEvaluatedProjectReferenceKey, StringComparer.Ordinal);
        var resolvedProjectPaths = new HashSet<string>(
            participatingResolvedReferences.Select(reference => Path.GetFullPath(reference.ProjectPath)),
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (EvaluatedProjectReference captured in capturedReferences)
        {
            // A target-time addition may intentionally enter ProjectReference after the SDK's
            // resolved vector was materialized. Preserve it as an independent branch; matched
            // entries above already carry the authoritative target framework metadata.
            if (!resolvedProjectPaths.Contains(Path.GetFullPath(captured.ProjectPath)))
                controlledReferences[BuildEvaluatedProjectReferenceKey(captured)] = captured;
        }

        references = controlledReferences.Values.ToArray();
        return true;
    }

}
