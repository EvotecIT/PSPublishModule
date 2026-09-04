using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Versioning;

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
        IReadOnlyDictionary<string, string> evaluatedProperties,
        out EvaluatedPublishInput[] publishInputs,
        out string? failureReason)
    {
        publishInputs = Array.Empty<EvaluatedPublishInput>();
        failureReason = null;
        string controlledOutputRoot = Path.Combine(
            Path.GetTempPath(),
            "pfpi-" + Guid.NewGuid().ToString("N"));
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
                    BuildControlledPublishProjectContexts(
                        request,
                        evaluatedProperties,
                        graphBuildNodes),
                    out controlledGitRoot,
                    out string? controlledProjectPath,
                    out string? checkoutFailureReason))
            {
                failureReason = "controlled source checkout could not be created: " +
                    (checkoutFailureReason ?? "unknown reason");
                return false;
            }
            if (!TryCreateControlledBuildEnvironment(
                    request.EnvironmentVariables,
                    request.ControlledBuildEnvironmentVariableNames,
                    controlledGitRoot!,
                    controlledSourceRoot,
                    Path.GetDirectoryName(request.ProjectPath)!,
                    out IReadOnlyDictionary<string, string?> controlledEnvironment))
            {
                failureReason = "controlled build environment could not be created";
                return false;
            }
            if (!TryCreateControlledPublishInputPlaceholders(
                    controlledGitRoot!,
                    controlledSourceRoot,
                    controlledProjectPath!,
                    executableMsBuildInputs,
                    request.GlobalProperties))
            {
                failureReason = "controlled publish placeholders could not be created";
                return false;
            }

            string offlinePackageSource = Directory.CreateDirectory(
                Path.Combine(controlledOutputRoot, "packages-source")).FullName;
            var offlinePackageSources = new List<string>();
            foreach (VerifiedPackageInputCatalog packageCatalog in graphVerifiedPackages)
            {
                if (!packageCatalog.TrySeedControlledPackageSource(
                        offlinePackageSource,
                        controlledSourceRoot,
                        controlledProjectPath!,
                        request.TrustedBuildPackages,
                        out string[] catalogSources,
                        out string catalogFailureReason,
                        allowSdkManagedToolchainPackages: true))
                {
                    failureReason = "verified offline package source could not be created: " + catalogFailureReason;
                    return false;
                }
                offlinePackageSources.AddRange(catalogSources);
            }
            if (offlinePackageSources.Count == 0)
                offlinePackageSources.Add(offlinePackageSource);
            if (!TrySeedControlledProjectMetadataPackages(
                    graphBuildNodes.SelectMany(node => new[]
                    {
                        new KeyValuePair<string?, string?>(node.PackageId, node.PackageVersion),
                        new KeyValuePair<string?, string?>(
                            node.PackageId,
                            node.PackageValidationBaselineVersion)
                    }).Where(identity => !string.IsNullOrWhiteSpace(identity.Value)),
                    offlinePackageSource,
                    out string projectMetadataFailureReason))
            {
                failureReason = "controlled project metadata source could not be created: " +
                    projectMetadataFailureReason;
                return false;
            }
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
                    request,
                    controlledProjectPath!,
                    graphBuildNodes,
                    controlledGitRoot!,
                    controlledSourceRoot,
                    controlledEnvironment,
                    controlledNuGetConfig,
                    offlinePackageSourceList,
                    out string graphBuildFailureReason))
            {
                failureReason = "controlled project graph build failed: " + graphBuildFailureReason;
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
                failureReason = "controlled project properties could not be mapped";
                return false;
            }
            arguments.Add("-p:BuildProjectReferences=false");
            arguments.Add("-p:RestoreRecursive=false");
            if (!TryBuildControlledPathMap(
                    controlledSourceRoot,
                    controlledGitRoot!,
                    evaluatedPathMap,
                    out string controlledPathMap))
            {
                failureReason = "controlled path map could not be created";
                return false;
            }
            arguments.Add("-p:PathMap=" + EscapeMsBuildPropertyValue(controlledPathMap));
            AppendControlledProofSafeguards(
                arguments,
                controlledNuGetConfig,
                offlinePackageSourceList);
            AppendControlledPackageLockIsolation(
                arguments,
                Path.Combine(controlledOutputRoot, "unused-packages.lock.json"));

            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(controlledProjectPath!)!,
                arguments,
                controlledEnvironment,
                TimeSpan.FromMinutes(5));
            if (process.ExitCode != 0 || process.TimedOut)
            {
                failureReason = process.TimedOut
                    ? "controlled publish evaluation timed out"
                    : $"controlled publish evaluation exited with code {process.ExitCode}" +
                      ReadControlledProcessFailureDetail(process);
                return false;
            }

            int itemsMarker = process.StdOut.LastIndexOf("\"Items\"", StringComparison.Ordinal);
            int jsonStart = itemsMarker < 0
                ? -1
                : process.StdOut.LastIndexOf('{', itemsMarker);
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                failureReason = "controlled publish evaluation returned no JSON payload";
                return false;
            }
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
            VerifiedPackageInputCatalog[] packageCatalogs = graphVerifiedPackages
                .Concat(verifiedPackages is null
                    ? Array.Empty<VerifiedPackageInputCatalog>()
                    : new[] { verifiedPackages })
                .Distinct()
                .ToArray();
            foreach (JsonElement value in resolvedFiles.EnumerateArray())
            {
                EvaluatedProjectItem? item = ReadEvaluatedProjectItem(
                    value,
                    controlledProjectDirectory);
                if (item is null ||
                    !item.Metadata.TryGetValue("RelativePath", out string? relativePath) ||
                    !IsControlledPublishRelativePath(relativePath))
                {
                    failureReason = "controlled publish item has an invalid relative path";
                    return false;
                }

                if (IsSameOrBelowBuildInputPath(item.FullPath, offlinePackageSource))
                {
                    failureReason = "controlled publish item resolves inside the offline package source";
                    return false;
                }
                if (IsTrustedExternalBuildInfrastructurePath(
                        item.FullPath,
                        trustedBuildInfrastructureRoots))
                {
                    continue;
                }
                if (!TryMapControlledPublishInputPath(
                        item.FullPath,
                        controlledSourceRoot,
                        controlledGitRoot!,
                        controlledPackageRoot,
                        packageCatalogs,
                        out string originalInputPath,
                        out bool isPackageBacked))
                {
                    failureReason = "controlled publish item path could not be mapped";
                    return false;
                }
                if (!TryMapControlledPublishMetadata(
                        item.Metadata,
                        controlledSourceRoot,
                        controlledGitRoot!,
                        controlledPackageRoot,
                        controlledOutputRoot,
                        packageCatalogs,
                        out IReadOnlyDictionary<string, string> mappedMetadata))
                {
                    failureReason = "controlled publish item metadata could not be mapped";
                    return false;
                }

                bool isSdkDefined = false;
                bool isProjectDefined = false;
                if (item.Metadata.TryGetValue(
                        "DefiningProjectFullPath",
                        out string? definingProject) &&
                    !string.IsNullOrWhiteSpace(definingProject))
                {
                    isSdkDefined = IsTrustedExternalBuildInfrastructurePath(
                        definingProject!,
                        trustedBuildInfrastructureRoots);
                    isProjectDefined = !isSdkDefined &&
                        IsSameOrBelowBuildInputPath(definingProject!, controlledSourceRoot);
                }
                bool isControlledEquivalent = (isSdkDefined || isProjectDefined) &&
                    File.Exists(originalInputPath) &&
                    File.Exists(item.FullPath) &&
                    AreControlledGeneratedOutputsEquivalent(originalInputPath, item.FullPath);
                if (isPackageBacked)
                {
                    isControlledEquivalent = File.Exists(originalInputPath) &&
                        File.Exists(item.FullPath) &&
                        AreControlledGeneratedOutputsEquivalent(originalInputPath, item.FullPath);
                }
                string? controlledSha256 = isControlledEquivalent
                    ? ComputeSha256Hex(File.ReadAllBytes(item.FullPath))
                    : null;
                int? controlledUnixFileMode = isControlledEquivalent
                    ? ReadControlledUnixFileMode(item.FullPath)
                    : null;
                if (isSdkDefined && !proveControlledGeneratedInputs && !isPackageBacked)
                    continue;
                results.Add(new EvaluatedPublishInput(
                    originalInputPath,
                    relativePath,
                    mappedMetadata,
                    isSdkDefined,
                    isProjectDefined,
                    isControlledEquivalent,
                    controlledSha256,
                    controlledUnixFileMode,
                    isPackageBacked));
            }

            publishInputs = results.ToArray();
            return true;
        }
        catch (Exception exception)
        {
            publishInputs = Array.Empty<EvaluatedPublishInput>();
            string nativeCode = exception is System.ComponentModel.Win32Exception win32Exception
                ? $" (native error {win32Exception.NativeErrorCode})"
                : string.Empty;
            failureReason = $"{exception.GetType().Name}{nativeCode} while reading controlled publish inputs";
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

    internal static bool TrySeedControlledProjectMetadataPackages(
        IEnumerable<KeyValuePair<string?, string?>> identities,
        string packageSource,
        out string failureReason)
    {
        failureReason = string.Empty;
        try
        {
            Directory.CreateDirectory(packageSource);
            foreach (KeyValuePair<string?, string?> identity in identities)
            {
                string packageId = identity.Key?.Trim() ?? string.Empty;
                string packageVersion = identity.Value?.Trim() ?? string.Empty;
                if (!IsSafeControlledProjectPackageId(packageId) ||
                    !NuGetVersion.TryParse(packageVersion, out NuGetVersion? parsedVersion))
                {
                    failureReason = $"project package identity is invalid: '{packageId}' '{packageVersion}'";
                    return false;
                }

                string normalizedVersion = parsedVersion.ToNormalizedString();
                string packagePath = Path.Combine(
                    packageSource,
                    packageId.ToLowerInvariant() + "." + normalizedVersion.ToLowerInvariant() + ".nupkg");
                if (File.Exists(packagePath))
                    continue;

                string temporaryPath = packagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
                    {
                        ZipArchiveEntry nuspecEntry = archive.CreateEntry(
                            packageId + ".nuspec",
                            CompressionLevel.Optimal);
                        using Stream stream = nuspecEntry.Open();
                        using var writer = new StreamWriter(
                            stream,
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        XNamespace nuspec = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
                        new XDocument(
                            new XElement(
                                nuspec + "package",
                                new XElement(nuspec + "metadata",
                                    new XElement(nuspec + "id", packageId),
                                    new XElement(nuspec + "version", normalizedVersion),
                                    new XElement(nuspec + "authors", "PowerForge"),
                                    new XElement(
                                        nuspec + "description",
                                        "Metadata-only placeholder for a controlled ProjectReference restore."))))
                            .Save(writer, SaveOptions.DisableFormatting);
                    }
                    File.Move(temporaryPath, packagePath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            failureReason = exception.GetBaseException().Message;
            return false;
        }
    }

    private static bool IsSafeControlledProjectPackageId(string packageId)
    {
        if (packageId.Length is < 1 or > 100 ||
            !IsAsciiLetterOrDigit(packageId[0]) ||
            !IsAsciiLetterOrDigit(packageId[packageId.Length - 1]))
        {
            return false;
        }
        return packageId.All(character =>
            IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool IsAsciiLetterOrDigit(char value)
        => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]>
        BuildControlledPublishProjectContexts(
            ProjectEvaluationRequest rootRequest,
            IReadOnlyDictionary<string, string> rootEvaluatedProperties,
            IReadOnlyCollection<ControlledPublishGraphNode> graphBuildNodes)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return graphBuildNodes
            .Select(node => (
                Request: node.Request,
                EvaluatedProperties: node.EvaluatedProperties))
            .Concat(new[]
            {
                (Request: rootRequest, EvaluatedProperties: rootEvaluatedProperties)
            })
            .GroupBy(node => Path.GetFullPath(node.Request.ProjectPath), comparer)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(node => node.Request.BuildControlledEvaluationProperties(
                        node.EvaluatedProperties))
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
        ProjectEvaluationRequest rootRequest,
        string controlledRootProjectPath,
        IReadOnlyCollection<ControlledPublishGraphNode> graphBuildNodes,
        string originalGitRoot,
        string controlledSourceRoot,
        IReadOnlyDictionary<string, string?> controlledEnvironment,
        string controlledNuGetConfig,
        string offlinePackageSourceList,
        out string failureReason)
    {
        failureReason = string.Empty;
        var restoreArguments = new List<string>
        {
            "msbuild",
            controlledRootProjectPath,
            "-nologo",
            "-maxCpuCount:1",
            "-nodeReuse:false",
            "-verbosity:quiet",
            "-target:Restore"
        };
        if (!TryAppendControlledProjectEvaluationProperties(
                restoreArguments,
                rootRequest,
                originalGitRoot,
                controlledSourceRoot))
        {
            failureReason = "root restore properties could not be remapped";
            return false;
        }
        AppendControlledProofSafeguards(
            restoreArguments,
            controlledNuGetConfig,
            offlinePackageSourceList);
        AppendControlledRootGraphRestoreOverrides(
            restoreArguments,
            Path.Combine(Path.GetDirectoryName(controlledSourceRoot)!, "unused-packages.lock.json"));
        // Each graph node has already contributed a committed package lock and
        // the offline source contains only byte-verified archives from those
        // locks. Re-evaluate the root graph against that closed package universe
        // so target-specific locks can produce the complete referenced assets
        // graph without requiring unrelated target frameworks in every lock.
        var restoreProcess = RunBuildInputEvaluationProcess(
            "dotnet",
            Path.GetDirectoryName(controlledRootProjectPath)!,
            restoreArguments,
            controlledEnvironment,
            TimeSpan.FromMinutes(5));
        if (restoreProcess.ExitCode != 0 || restoreProcess.TimedOut)
        {
            failureReason = restoreProcess.TimedOut
                ? "root graph restore timed out"
                : "root graph restore failed with code " + restoreProcess.ExitCode +
                  ReadControlledProcessFailureDetail(restoreProcess);
            return false;
        }

        foreach (ControlledPublishGraphNode node in graphBuildNodes)
        {
            string originalProjectPath = Path.GetFullPath(node.Request.ProjectPath);
            if (!IsSameOrBelowBuildInputPath(originalProjectPath, originalGitRoot))
            {
                failureReason = "project path is outside the original Git root";
                return false;
            }
            string controlledProjectPath = Path.GetFullPath(Path.Combine(
                controlledSourceRoot,
                FrameworkCompatibility.GetRelativePath(originalGitRoot, originalProjectPath)));
            if (!IsSameOrBelowBuildInputPath(controlledProjectPath, controlledSourceRoot) ||
                !File.Exists(controlledProjectPath))
            {
                failureReason = $"controlled project is unavailable: '{Path.GetFileName(originalProjectPath)}'";
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
                "-target:Build"
            };
            if (!TryAppendControlledProjectEvaluationProperties(
                    arguments,
                    node.Request,
                    originalGitRoot,
                    controlledSourceRoot))
            {
                failureReason = $"project properties could not be remapped: '{Path.GetFileName(originalProjectPath)}'";
                return false;
            }
            arguments.Add("-p:BuildProjectReferences=false");
            arguments.Add("-p:RestoreRecursive=false");
            if (!TryBuildControlledPathMap(
                    controlledSourceRoot,
                    originalGitRoot,
                    node.PathMap,
                    out string controlledPathMap))
            {
                failureReason = $"path map could not be created: '{Path.GetFileName(originalProjectPath)}'";
                return false;
            }
            arguments.Add("-p:PathMap=" + EscapeMsBuildPropertyValue(controlledPathMap));
            AppendControlledProofSafeguards(
                arguments,
                controlledNuGetConfig,
                offlinePackageSourceList);

            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(controlledProjectPath)!,
                arguments,
                controlledEnvironment,
                TimeSpan.FromMinutes(5));
            if (process.ExitCode != 0 || process.TimedOut)
            {
                failureReason = process.TimedOut
                    ? $"build timed out: '{Path.GetFileName(originalProjectPath)}'"
                    : $"build failed with code {process.ExitCode}: '{Path.GetFileName(originalProjectPath)}'" +
                      ReadControlledProcessFailureDetail(process);
                return false;
            }
        }
        return true;
    }

    internal static void AppendControlledRootGraphRestoreOverrides(
        ICollection<string> arguments,
        string unusedLockFilePath)
    {
        AppendControlledPackageLockIsolation(arguments, unusedLockFilePath);
        arguments.Add("-p:BuildProjectReferences=true");
        arguments.Add("-p:RestoreRecursive=true");
        arguments.Add("-p:WarningsNotAsErrors=NU1510");
    }

    internal static void AppendControlledPackageLockIsolation(
        ICollection<string> arguments,
        string unusedLockFilePath)
    {
        arguments.Add("-p:NuGetLockFilePath=" + EscapeMsBuildPropertyValue(unusedLockFilePath));
        arguments.Add("-p:RestorePackagesWithLockFile=false");
    }

    private static string ReadControlledProcessFailureDetail(
        (int ExitCode, string StdOut, string StdErr, bool TimedOut) process)
    {
        string output = string.IsNullOrWhiteSpace(process.StdErr)
            ? process.StdOut
            : process.StdErr;
        string detail = string.Join(" | ", output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Reverse()
            .Take(6)
            .Reverse());
        return string.IsNullOrWhiteSpace(detail)
            ? string.Empty
            : ": " + RedactCommandLineSecrets(detail);
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
            if (property.Key.Equals("RuntimeIdentifiers", StringComparison.OrdinalIgnoreCase))
            {
                request.GlobalProperties.TryGetValue(
                    "RuntimeIdentifier",
                    out string? selectedRuntimeIdentifier);
                string[] controlledRuntimeIdentifiers = SelectControlledRuntimeIdentifiers(
                    controlledValue,
                    selectedRuntimeIdentifier);
                if (controlledRuntimeIdentifiers.Length > 0)
                {
                    arguments.Add("-p:RuntimeIdentifiers=" +
                        BuildMsBuildListPropertyValue(controlledRuntimeIdentifiers));
                }
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(controlledValue));
        }

        return true;
    }

    internal static string[] SelectControlledRuntimeIdentifiers(
        string runtimeIdentifiers,
        string? selectedRuntimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(selectedRuntimeIdentifier))
            return Array.Empty<string>();

        string selected = selectedRuntimeIdentifier!.Trim();
        return runtimeIdentifiers
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .DefaultIfEmpty(selected)
            .ToArray();
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
                                out string[] expandedValues,
                                consumingElement: item))
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
