using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal DotNetPublishMsiPrepareResult PrepareMsiPackage(
        DotNetPublishPlan plan,
        IReadOnlyList<DotNetPublishArtefactResult> artefacts,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (artefacts is null) throw new ArgumentNullException(nameof(artefacts));
        if (step is null) throw new ArgumentNullException(nameof(step));

        var installerId = (step.InstallerId ?? string.Empty).Trim();
        var target = (step.TargetName ?? string.Empty).Trim();
        var framework = (step.Framework ?? string.Empty).Trim();
        var runtime = (step.Runtime ?? string.Empty).Trim();
        var style = step.Style;

        if (string.IsNullOrWhiteSpace(installerId))
            throw new InvalidOperationException($"Step '{step.Key}' is missing InstallerId.");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(runtime))
            throw new InvalidOperationException($"Step '{step.Key}' is missing target/framework/runtime metadata.");
        if (!style.HasValue)
            throw new InvalidOperationException($"Step '{step.Key}' is missing style metadata.");
        if (string.IsNullOrWhiteSpace(step.StagingPath))
            throw new InvalidOperationException($"Step '{step.Key}' is missing staging path.");
        if (string.IsNullOrWhiteSpace(step.ManifestPath))
            throw new InvalidOperationException($"Step '{step.Key}' is missing manifest path.");

        var sourceBundleId = ResolveInstallerSourceBundleId(plan, installerId, step.BundleId);
        var sourceArtefact = ResolveInstallerSourceArtefact(
            artefacts,
            target,
            framework,
            runtime,
            style.Value,
            sourceBundleId);

        if (sourceArtefact is null)
        {
            var sourceKindText = string.IsNullOrWhiteSpace(sourceBundleId)
                ? "publish artefact"
                : $"bundle artefact '{sourceBundleId}'";
            throw new InvalidOperationException(
                $"MSI prepare step '{step.Key}' could not find matching source artefact for " +
                $"target='{target}', framework='{framework}', runtime='{runtime}', style='{style.Value}', source={sourceKindText}.");
        }

        var sourceOutputDir = Path.GetFullPath(sourceArtefact.OutputDir);
        if (!Directory.Exists(sourceOutputDir))
            throw new DirectoryNotFoundException($"Publish output directory not found: {sourceOutputDir}");

        var stagingPath = Path.GetFullPath(step.StagingPath!);
        var manifestPath = Path.GetFullPath(step.ManifestPath!);
        var harvestPath = string.IsNullOrWhiteSpace(step.HarvestPath)
            ? null
            : Path.GetFullPath(step.HarvestPath!);

        if (!plan.AllowOutputOutsideProjectRoot)
        {
            EnsurePathWithinRoot(plan.ProjectRoot, stagingPath, $"Installer '{installerId}' staging path");
            EnsurePathWithinRoot(plan.ProjectRoot, manifestPath, $"Installer '{installerId}' manifest path");
            if (!string.IsNullOrWhiteSpace(harvestPath))
                EnsurePathWithinRoot(plan.ProjectRoot, harvestPath!, $"Installer '{installerId}' harvest path");
        }

        if (PathsEqual(sourceOutputDir, stagingPath))
        {
            throw new InvalidOperationException(
                $"Installer '{installerId}' staging path resolves to the source output directory. " +
                "Use a different Installers[].StagingPath.");
        }

        if (Directory.Exists(stagingPath))
        {
            try { Directory.Delete(stagingPath, recursive: true); }
            catch { /* best effort */ }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
        DirectoryCopy(sourceOutputDir, stagingPath);

        string? resolvedHarvestPath = null;
        string? resolvedHarvestDirectoryRefId = null;
        string? resolvedHarvestComponentGroupId = null;
        if (!string.IsNullOrWhiteSpace(harvestPath))
        {
            resolvedHarvestPath = harvestPath!;
            resolvedHarvestDirectoryRefId = SanitizeWixIdentifier(
                string.IsNullOrWhiteSpace(step.HarvestDirectoryRefId) ? "INSTALLFOLDER" : step.HarvestDirectoryRefId!,
                fallback: "INSTALLFOLDER");
            resolvedHarvestComponentGroupId = SanitizeWixIdentifier(
                string.IsNullOrWhiteSpace(step.HarvestComponentGroupId) ? $"Harvest_{installerId}" : step.HarvestComponentGroupId!,
                fallback: $"Harvest_{installerId}");

            Directory.CreateDirectory(Path.GetDirectoryName(resolvedHarvestPath)!);
            File.WriteAllText(
                resolvedHarvestPath,
                BuildWixHarvestFragment(stagingPath, resolvedHarvestDirectoryRefId, resolvedHarvestComponentGroupId),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            _logger.Info($"MSI prepare harvest -> {resolvedHarvestPath}");
        }

        var summary = SummarizeDirectory(stagingPath, runtime);
        var result = new DotNetPublishMsiPrepareResult
        {
            InstallerId = installerId,
            Target = target,
            Framework = framework,
            Runtime = runtime,
            Style = style.Value,
            SourceCategory = sourceArtefact.Category,
            BundleId = sourceArtefact.BundleId,
            SourceOutputDir = sourceOutputDir,
            StagingDir = stagingPath,
            ManifestPath = manifestPath,
            HarvestPath = resolvedHarvestPath,
            HarvestDirectoryRefId = resolvedHarvestDirectoryRefId,
            HarvestComponentGroupId = resolvedHarvestComponentGroupId,
            Files = summary.Files,
            TotalBytes = summary.TotalBytes
        };

        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _logger.Info(
            $"MSI prepare completed for '{installerId}' from {target} ({framework}, {runtime}, {style.Value}) -> {stagingPath}");
        _logger.Info($"MSI prepare manifest -> {manifestPath}");

        return result;
    }

    private static string? ResolveInstallerSourceBundleId(
        DotNetPublishPlan plan,
        string installerId,
        string? stepBundleId)
    {
        if (!string.IsNullOrWhiteSpace(stepBundleId))
            return stepBundleId!.Trim();

        var installer = (plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
            .FirstOrDefault(entry => string.Equals(entry.Id, installerId, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(installer?.PrepareFromBundleId)
            ? null
            : installer!.PrepareFromBundleId!.Trim();
    }

    private static DotNetPublishArtefactResult? ResolveInstallerSourceArtefact(
        IReadOnlyList<DotNetPublishArtefactResult> artefacts,
        string target,
        string framework,
        string runtime,
        DotNetPublishStyle style,
        string? bundleId)
    {
        return (artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
            .LastOrDefault(a =>
                string.Equals(a.Target, target, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Framework, framework, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Runtime, runtime, StringComparison.OrdinalIgnoreCase)
                && a.Style == style
                && string.Equals(a.BundleId, bundleId, StringComparison.OrdinalIgnoreCase)
                && (bundleId is null
                    ? a.Category == DotNetPublishArtefactCategory.Publish
                    : a.Category == DotNetPublishArtefactCategory.Bundle));
    }

    internal static string BuildWixHarvestFragment(
        string stagingPath,
        string directoryRefId,
        string componentGroupId)
    {
        var files = Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var root = new HarvestDirectoryNode(string.Empty, id: null);
        foreach (var file in files)
        {
            var relativePath = GetRelativePathCompat(stagingPath, file);
            var segments = relativePath
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            var current = root;
            if (segments.Length > 1)
            {
                var pathSoFar = string.Empty;
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    var segment = segments[i];
                    pathSoFar = pathSoFar.Length == 0 ? segment : Path.Combine(pathSoFar, segment);
                    if (!current.Children.TryGetValue(segment, out var child))
                    {
                        child = new HarvestDirectoryNode(segment, BuildHarvestId("Dir", pathSoFar));
                        current.Children.Add(segment, child);
                    }

                    current = child;
                }
            }

            current.Files.Add(new HarvestFileNode(file, relativePath));
        }

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">");
        sb.AppendLine("  <Fragment>");
        sb.AppendLine($"    <DirectoryRef Id=\"{XmlEscape(directoryRefId)}\">");

        var componentIds = new List<string>(files.Length);

        static void AppendFiles(
            StringBuilder builder,
            List<string> collectedComponentIds,
            IEnumerable<HarvestFileNode> directoryFiles,
            int indent)
        {
            foreach (var file in directoryFiles.OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                var componentId = BuildHarvestId("Cmp", file.RelativePath);
                var fileId = BuildHarvestId("Fil", file.RelativePath);
                collectedComponentIds.Add(componentId);

                builder.AppendLine($"{new string(' ', indent)}<Component Id=\"{componentId}\" Guid=\"*\">");
                builder.AppendLine($"{new string(' ', indent + 2)}<File Id=\"{fileId}\" Source=\"{XmlEscape(file.SourcePath)}\" KeyPath=\"yes\" />");
                builder.AppendLine($"{new string(' ', indent)}</Component>");
            }
        }

        static void AppendDirectories(
            StringBuilder builder,
            List<string> collectedComponentIds,
            HarvestDirectoryNode directory,
            int indent)
        {
            foreach (var child in directory.Children.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"{new string(' ', indent)}<Directory Id=\"{XmlEscape(child.Id!)}\" Name=\"{XmlEscape(child.Name)}\">");
                AppendFiles(builder, collectedComponentIds, child.Files, indent + 2);
                AppendDirectories(builder, collectedComponentIds, child, indent + 2);
                builder.AppendLine($"{new string(' ', indent)}</Directory>");
            }
        }

        AppendFiles(sb, componentIds, root.Files, indent: 6);
        AppendDirectories(sb, componentIds, root, indent: 6);

        sb.AppendLine("    </DirectoryRef>");
        sb.AppendLine("  </Fragment>");
        sb.AppendLine("  <Fragment>");
        sb.AppendLine($"    <ComponentGroup Id=\"{XmlEscape(componentGroupId)}\">");
        foreach (var componentId in componentIds)
            sb.AppendLine($"      <ComponentRef Id=\"{componentId}\" />");
        sb.AppendLine("    </ComponentGroup>");
        sb.AppendLine("  </Fragment>");
        sb.AppendLine("</Wix>");
        return sb.ToString();
    }

    private static string BuildHarvestId(string prefix, string value)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var hash = sha256.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
            builder.Append(item.ToString("X2"));
        var suffix = builder.ToString().Substring(0, 16);
        return $"{prefix}_{suffix}";
    }

    private static string XmlEscape(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;");
    }

    private static string GetRelativePathCompat(string relativeTo, string path)
    {
#if NET472
        var baseUri = new Uri(AppendDirectorySeparator(relativeTo), UriKind.Absolute);
        var targetUri = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        var relativeUri = baseUri.MakeRelativeUri(targetUri);
        return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

    private static string AppendDirectorySeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            fullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath + Path.DirectorySeparatorChar;
    }

    private sealed class HarvestDirectoryNode
    {
        public HarvestDirectoryNode(string name, string? id)
        {
            Name = name;
            Id = id;
        }

        public string Name { get; }
        public string? Id { get; }
        public Dictionary<string, HarvestDirectoryNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<HarvestFileNode> Files { get; } = new();
    }

    private sealed class HarvestFileNode
    {
        public HarvestFileNode(string sourcePath, string relativePath)
        {
            SourcePath = sourcePath;
            RelativePath = relativePath;
        }

        public string SourcePath { get; }
        public string RelativePath { get; }
    }
}
