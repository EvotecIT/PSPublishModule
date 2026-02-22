using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private DotNetPublishMsiPrepareResult PrepareMsiPackage(
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

        var sourceArtefact = artefacts
            .LastOrDefault(a =>
                string.Equals(a.Target, target, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Framework, framework, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Runtime, runtime, StringComparison.OrdinalIgnoreCase)
                && a.Style == style.Value);

        if (sourceArtefact is null)
        {
            throw new InvalidOperationException(
                $"MSI prepare step '{step.Key}' could not find matching publish artefact for " +
                $"target='{target}', framework='{framework}', runtime='{runtime}', style='{style.Value}'.");
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

    private static string BuildWixHarvestFragment(
        string stagingPath,
        string directoryRefId,
        string componentGroupId)
    {
        var files = Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">");
        sb.AppendLine("  <Fragment>");
        sb.AppendLine($"    <DirectoryRef Id=\"{XmlEscape(directoryRefId)}\">");

        var componentIds = new List<string>(files.Length);
        for (var i = 0; i < files.Length; i++)
        {
            var componentId = $"Cmp_{i + 1:D5}";
            var fileId = $"Fil_{i + 1:D5}";
            componentIds.Add(componentId);

            sb.AppendLine($"      <Component Id=\"{componentId}\" Guid=\"*\">");
            sb.AppendLine($"        <File Id=\"{fileId}\" Source=\"{XmlEscape(files[i])}\" KeyPath=\"yes\" />");
            sb.AppendLine("      </Component>");
        }

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

    private static string XmlEscape(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;");
    }
}
