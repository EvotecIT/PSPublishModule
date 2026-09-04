using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    /// <summary>
    /// Resolves runnable artifacts for one target and stages dimension-qualified aliases when
    /// direct matrix outputs would otherwise collide by GitHub asset file name.
    /// </summary>
    internal static bool TryBuildDotNetGitHubRunnableAssets(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        DotNetPublishResult result,
        out List<string> runnableAssets,
        out string checksumDirectory,
        out string safeTarget,
        out string? error)
    {
        runnableAssets = new List<string>();
        checksumDirectory = string.Empty;
        safeTarget = DotNetPublishReleaseAssetNaming.ToSafeComponent(target.Name);
        error = null;
        var targetRunnableAssets = new List<(DotNetPublishArtefactResult Artefact, string Path, bool Direct)>();
        DotNetPublishArtefactResult[] targetArtefacts = (result.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
            .Where(entry =>
                string.Equals(entry.Target, target.Name, StringComparison.OrdinalIgnoreCase) &&
                entry.Category != DotNetPublishArtefactCategory.Installer)
            .ToArray();
        foreach (DotNetPublishArtefactResult artefact in targetArtefacts)
        {
            if (!TryResolveDotNetGitHubArtefactPath(plan, target, artefact, out string? path, out bool direct, out error) ||
                string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                error ??= $"A runnable release artifact is missing for DotNet publish target '{target.Name}'.";
                return false;
            }
            if (direct && !IsStandaloneDotNetGitHubArtefact(artefact, path!, out string? directError))
            {
                error = directError;
                return false;
            }
            targetRunnableAssets.Add((artefact, Path.GetFullPath(path!), direct));
        }

        runnableAssets.AddRange(targetRunnableAssets.Select(entry => entry.Path));
        if (!TryAddRequiredDotNetReleaseOutputs(
                runnableAssets,
            (result.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
            .Where(entry =>
                string.Equals(entry.Target, target.Name, StringComparison.OrdinalIgnoreCase) &&
                entry.Category == DotNetPublishArtefactCategory.Installer)
            .Select(entry => entry.OutputFiles ?? Array.Empty<string>()),
                target.Name,
                "native installer",
                out error))
            return false;
        if (!TryAddRequiredDotNetReleaseOutputs(
                runnableAssets,
            (result.MsiBuilds ?? Array.Empty<DotNetPublishMsiBuildResult>())
            .Where(entry => string.Equals(entry.Target, target.Name, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.OutputFiles ?? Array.Empty<string>()),
                target.Name,
                "MSI",
                out error))
            return false;
        if (!TryAddRequiredDotNetReleaseOutputs(
                runnableAssets,
            (result.StorePackages ?? Array.Empty<DotNetPublishStorePackageResult>())
            .Where(entry => string.Equals(entry.Target, target.Name, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (entry.OutputFiles ?? Array.Empty<string>())
                .Concat(entry.UploadFiles ?? Array.Empty<string>())
                .Concat(entry.SymbolFiles ?? Array.Empty<string>())),
                target.Name,
                "Store package",
                out error))
            return false;
        if (runnableAssets.Count == 0)
        {
            error = $"No runnable release artifact was produced for DotNet publish target '{target.Name}'.";
            return false;
        }

        checksumDirectory = !string.IsNullOrWhiteSpace(result.ChecksumsPath)
            ? Path.GetDirectoryName(Path.GetFullPath(result.ChecksumsPath!))!
            : Path.GetDirectoryName(Path.GetFullPath(runnableAssets[0]))!;
        runnableAssets = StageCollidingDotNetGitHubAssets(
            targetRunnableAssets,
            runnableAssets,
            checksumDirectory,
            safeTarget,
            out List<string> directEvidenceAssets);
        runnableAssets.AddRange(directEvidenceAssets);
        return true;
    }

    private static bool TryAddRequiredDotNetReleaseOutputs(
        ICollection<string> runnableAssets,
        IEnumerable<IEnumerable<string>> declaredOutputGroups,
        string targetName,
        string outputKind,
        out string? error)
    {
        error = null;
        foreach (IEnumerable<string> declaredOutputGroup in declaredOutputGroups)
        {
            string[] declaredPaths = declaredOutputGroup.ToArray();
            if (declaredPaths.Length == 0)
            {
                error = $"A declared {outputKind} result for DotNet publish target '{targetName}' contains no output files.";
                return false;
            }
            foreach (string? declaredPath in declaredPaths)
            {
                if (string.IsNullOrWhiteSpace(declaredPath))
                {
                    error = $"A declared {outputKind} output for DotNet publish target '{targetName}' has an empty path.";
                    return false;
                }

                string fullPath = Path.GetFullPath(declaredPath);
                if (!File.Exists(fullPath))
                {
                    error = $"A declared {outputKind} output is missing for DotNet publish target '{targetName}': {fullPath}";
                    return false;
                }
                runnableAssets.Add(fullPath);
            }
        }
        return true;
    }

    /// <summary>
    /// Selects an archive or direct executable from the producing artifact's own packaging policy.
    /// </summary>
    internal static bool TryResolveDotNetGitHubArtefactPath(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        DotNetPublishArtefactResult artefact,
        out string? path,
        out bool direct,
        out string? error)
    {
        path = null;
        direct = false;
        error = null;
        bool zip;
        switch (artefact.Category)
        {
            case DotNetPublishArtefactCategory.Publish:
                zip = target.Publish.Zip;
                break;
            case DotNetPublishArtefactCategory.Bundle:
                DotNetPublishBundlePlan? bundle = (plan.Bundles ?? Array.Empty<DotNetPublishBundlePlan>())
                    .FirstOrDefault(entry =>
                        string.Equals(entry.Id, artefact.BundleId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.PrepareFromTarget, target.Name, StringComparison.OrdinalIgnoreCase));
                if (bundle is null)
                {
                    error = $"Bundle '{artefact.BundleId}' is not present in the DotNet publish plan for target '{target.Name}'.";
                    return false;
                }
                zip = bundle.Zip;
                break;
            default:
                error = $"Artifact category '{artefact.Category}' is not a runnable DotNet publish artifact for target '{target.Name}'.";
                return false;
        }

        direct = !zip;
        path = zip ? artefact.ZipPath : artefact.ExePath;
        return true;
    }

    private static bool IsStandaloneDotNetGitHubArtefact(
        DotNetPublishArtefactResult artefact,
        string executablePath,
        out string? error)
    {
        error = null;
        string identity = artefact.Category == DotNetPublishArtefactCategory.Bundle
            ? $"bundle '{artefact.BundleId}'"
            : $"publish target '{artefact.Target}'";
        if (artefact.Style == DotNetPublishStyle.FrameworkDependent)
        {
            error = $"DotNet {identity} uses FrameworkDependent output and cannot be published as one direct asset. " +
                    "Enable ZIP packaging so the executable, runtime configuration, and dependencies stay together.";
            return false;
        }

        string? outputDirectory = Directory.Exists(artefact.OutputDir)
            ? artefact.OutputDir
            : Directory.Exists(artefact.PublishDir)
                ? artefact.PublishDir
                : null;
        if (outputDirectory is null)
            return true;

        string primaryPath = Path.GetFullPath(executablePath);
        StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string[] requiredCompanions = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !string.Equals(path, primaryPath, pathComparison))
            .Where(path => !IsOptionalDirectAssetDiagnostic(primaryPath, path))
            .ToArray();
        if (requiredCompanions.Length == 0)
            return true;

        error = $"DotNet {identity} contains {requiredCompanions.Length} additional runtime payload " +
                $"{(requiredCompanions.Length == 1 ? "file" : "files")} and cannot be published as one direct asset. " +
                "Enable ZIP packaging so the complete output stays together.";
        return false;
    }

    private static bool IsOptionalDirectAssetDiagnostic(string primaryPath, string path)
    {
        if (string.Equals(
                path,
                primaryPath + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                path,
                primaryPath + PowerForgePortablePayloadInventory.DirectSignatureSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string extension = Path.GetExtension(path);
        if (!extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
            Path.GetFileNameWithoutExtension(path),
            Path.GetFileNameWithoutExtension(primaryPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> StageCollidingDotNetGitHubAssets(
        IReadOnlyList<(DotNetPublishArtefactResult Artefact, string Path, bool Direct)> targetAssets,
        IReadOnlyList<string> runnableAssets,
        string checksumDirectory,
        string safeTarget,
        out List<string> directEvidenceAssets)
    {
        directEvidenceAssets = new List<string>();
        var duplicateNames = new HashSet<string>(
            runnableAssets
                .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key!),
            StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stagedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string stagingDirectory = Path.Combine(checksumDirectory, safeTarget + ".release-assets");
        foreach (var entry in targetAssets.Where(entry =>
                     entry.Direct && duplicateNames.Contains(Path.GetFileName(entry.Path))))
        {
            string stagedName = DotNetPublishReleaseAssetNaming.CreateDirectMatrixAssetName(
                entry.Artefact.Target,
                entry.Artefact.Framework,
                entry.Artefact.Runtime,
                entry.Artefact.Style.ToString(),
                entry.Artefact.Category,
                entry.Artefact.BundleId,
                entry.Path);
            if (!stagedNames.Add(stagedName))
            {
                throw new InvalidOperationException(
                    $"DotNet publish artifacts for target '{safeTarget}' do not have unique release matrix identities.");
            }
            Directory.CreateDirectory(stagingDirectory);
            string stagedPath = Path.Combine(stagingDirectory, stagedName);
            File.Copy(entry.Path, stagedPath, overwrite: true);
            replacements[entry.Path] = stagedPath;
        }

        foreach (var entry in targetAssets.Where(entry => entry.Direct && entry.Artefact.SignedFiles > 0))
        {
            string finalExecutablePath = replacements.TryGetValue(entry.Path, out string? stagedPath)
                ? stagedPath
                : entry.Path;
            foreach (string suffix in new[]
                     {
                         PowerForgePortablePayloadInventory.DirectInventorySuffix,
                         PowerForgePortablePayloadInventory.DirectSignatureSuffix
                     })
            {
                string sourcePath = entry.Path + suffix;
                if (!File.Exists(sourcePath))
                {
                    throw new InvalidOperationException(
                        $"Signed direct release artifact '{Path.GetFileName(entry.Path)}' is missing publisher-signed matrix evidence '{Path.GetFileName(sourcePath)}'.");
                }
                string finalPath = finalExecutablePath + suffix;
                if (!string.Equals(sourcePath, finalPath, StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourcePath, finalPath, overwrite: true);
                directEvidenceAssets.Add(finalPath);
            }
        }

        return runnableAssets
            .Select(path => replacements.TryGetValue(path, out string? stagedPath) ? stagedPath : path)
            .ToList();
    }

    internal static string[] GetDotNetGitHubConfigurationAssets(
        DotNetPublishPlan plan,
        string stagingDirectory)
    {
        DotNetPublishPipelineRunner.ValidateGeneratedConfigurationInputs(plan);
        string[] generatedInputs = ResolveExistingConfigurationInputs(plan.GeneratedConfigurationInputPaths);
        if (generatedInputs.Length > 0)
            return generatedInputs;

        string[] inputs = ResolveExistingConfigurationInputs(plan.ConfigurationInputPaths);
        if (inputs.Length < 2)
            return inputs;

        foreach (string releaseConfigurationPath in inputs)
        {
            JsonObject? releaseConfiguration;
            try
            {
                releaseConfiguration = JsonNode.Parse(
                    File.ReadAllText(releaseConfigurationPath),
                    nodeOptions: null,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    }) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }

            if (releaseConfiguration is null ||
                !TryGetJsonObject(releaseConfiguration, "Tools", out JsonObject? tools) ||
                tools is null ||
                !TryGetJsonString(tools, "DotNetPublishConfigPath", out string? configuredPath) ||
                string.IsNullOrWhiteSpace(configuredPath))
            {
                continue;
            }

            string releaseDirectory = Path.GetDirectoryName(releaseConfigurationPath) ?? Directory.GetCurrentDirectory();
            string referencedPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(releaseDirectory, configuredPath));
            if (!inputs.Contains(referencedPath, StringComparer.OrdinalIgnoreCase))
                continue;

            byte[] referencedBytes = File.ReadAllBytes(referencedPath);
            string referencedName = $".release.dotnetpublish.{ComputeSha256(referencedBytes)}.json";
            Directory.CreateDirectory(stagingDirectory);
            string stagedReferencedPath = Path.Combine(stagingDirectory, referencedName);
            File.WriteAllBytes(stagedReferencedPath, referencedBytes);
            SetJsonProperty(tools, "DotNetPublishConfigPath", referencedName);

            string stagedReleasePath = Path.Combine(stagingDirectory, Path.GetFileName(releaseConfigurationPath));
            File.WriteAllText(
                stagedReleasePath,
                releaseConfiguration.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return new[] { stagedReleasePath, stagedReferencedPath };
        }

        return inputs;
    }

    private static string[] ResolveExistingConfigurationInputs(IEnumerable<string>? paths) =>
        (paths ?? Array.Empty<string>())
        .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool TryGetJsonObject(JsonObject parent, string propertyName, out JsonObject? value)
    {
        KeyValuePair<string, JsonNode?> property = parent.FirstOrDefault(entry =>
            string.Equals(entry.Key, propertyName, StringComparison.OrdinalIgnoreCase));
        value = property.Value as JsonObject;
        return value is not null;
    }

    private static bool TryGetJsonString(JsonObject parent, string propertyName, out string? value)
    {
        KeyValuePair<string, JsonNode?> property = parent.FirstOrDefault(entry =>
            string.Equals(entry.Key, propertyName, StringComparison.OrdinalIgnoreCase));
        value = property.Value is JsonValue jsonValue && jsonValue.TryGetValue(out string? text)
            ? text
            : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void SetJsonProperty(JsonObject parent, string propertyName, string value)
    {
        string key = parent
            .Select(entry => entry.Key)
            .First(entry => string.Equals(entry, propertyName, StringComparison.OrdinalIgnoreCase));
        parent[key] = value;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
