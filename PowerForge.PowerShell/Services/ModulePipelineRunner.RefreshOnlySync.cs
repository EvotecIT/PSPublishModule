using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    internal string? SyncBuildManifestToProjectRoot(
        ModulePipelinePlan plan,
        ModuleBuildResult? buildResult = null,
        bool syncGeneratedBootstrapper = true)
    {
        var projectManifestPath = GetProjectManifestPath(plan);
        if (!File.Exists(projectManifestPath))
            return null;

        RefreshProjectManifestFromPlan(plan, projectManifestPath);
        var syncedStagedExports = buildResult is not null &&
                                  !plan.BuildSpec.RefreshManifestOnly;
        var syncedGeneratedBootstrapper = syncedStagedExports &&
                                          SyncBinaryExportsToProjectRoot(plan, buildResult!, projectManifestPath, syncGeneratedBootstrapper);

        var label = plan.GateMode == ConfigurationGateMode.Documentation
            ? "Documentation"
            : plan.BuildSpec.RefreshManifestOnly
                ? "RefreshPSD1Only"
                : "Build";
        var message = syncedGeneratedBootstrapper
            ? $"{label}: refreshed project-root manifest and bootstrapper from staged binary exports."
            : syncedStagedExports
                ? $"{label}: refreshed project-root manifest and exports from staged binary manifest."
            : $"{label}: refreshed project-root manifest from source manifest inputs.";
        _logger.Info(message);
        return message;
    }

    internal void SyncRefreshManifestToProjectRoot(
        ModulePipelinePlan plan)
    {
        if (!plan.BuildSpec.RefreshManifestOnly)
            return;

        _ = SyncBuildManifestToProjectRoot(plan);
    }

    internal void SyncPublishedManifestToProjectRoot(
        ModulePipelinePlan plan,
        IReadOnlyList<ModulePublishResult>? publishResults)
    {
        if (publishResults is null || publishResults.Count == 0)
            return;

        if (publishResults.Any(r => r is null || !r.Succeeded))
            return;

        var projectManifestPath = GetProjectManifestPath(plan);
        if (!File.Exists(projectManifestPath))
            return;

        RefreshProjectManifestFromPlan(plan, projectManifestPath);
        _logger.Info("Publish: refreshed project-root manifest from source manifest inputs.");
    }

    private static string GetProjectManifestPath(ModulePipelinePlan plan)
    {
        var projectRoot = Path.GetFullPath(plan.ProjectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(projectRoot, $"{plan.ModuleName}.psd1");
    }

    private bool SyncBinaryExportsToProjectRoot(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        string projectManifestPath,
        bool syncGeneratedBootstrapper)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));
        if (string.IsNullOrWhiteSpace(buildResult.ManifestPath) || !File.Exists(buildResult.ManifestPath))
            return false;

        var exports = ModuleManifestExportReader.ReadExports(buildResult.ManifestPath);
        _manifestMutator.TrySetManifestExports(projectManifestPath, exports.Functions, exports.Cmdlets, exports.Aliases);

        var sourcePsm1Path = Path.Combine(plan.BuildSpec.SourcePath, plan.ModuleName + ".psm1");
        var stagingPsm1Path = Path.Combine(buildResult.StagingPath, plan.ModuleName + ".psm1");
        var copiedGeneratedBootstrapper = false;
        if (syncGeneratedBootstrapper &&
            File.Exists(stagingPsm1Path) &&
            SourceCanUseGeneratedBuildBootstrapper(plan, sourcePsm1Path))
        {
            CopyGeneratedBootstrapperWithoutSignature(stagingPsm1Path, sourcePsm1Path);
            copiedGeneratedBootstrapper = true;
        }

        return copiedGeneratedBootstrapper;
    }

    private static void CopyGeneratedBootstrapperWithoutSignature(string sourcePath, string destinationPath)
    {
        var content = File.ReadAllText(sourcePath);
        var unsignedContent = RemoveAuthenticodeSignatureBlock(content);
        var normalizedContent = NormalizeLineEndingsForDestination(unsignedContent, destinationPath);

        if (File.Exists(destinationPath) &&
            string.Equals(File.ReadAllText(destinationPath), normalizedContent, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(destinationPath, normalizedContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string RemoveAuthenticodeSignatureBlock(string content)
    {
        if (string.IsNullOrEmpty(content) ||
            !content.Contains("# SIG # Begin signature block", StringComparison.Ordinal))
        {
            return content;
        }

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>(lines.Length);
        var inSignatureBlock = false;

        foreach (var line in lines)
        {
            if (!inSignatureBlock &&
                line.StartsWith("# SIG # Begin signature block", StringComparison.Ordinal))
            {
                inSignatureBlock = true;
                continue;
            }

            if (inSignatureBlock)
            {
                if (line.StartsWith("# SIG # End signature block", StringComparison.Ordinal))
                    inSignatureBlock = false;

                continue;
            }

            kept.Add(line);
        }

        return string.Join(Environment.NewLine, kept).TrimEnd() + Environment.NewLine;
    }

    private static string NormalizeLineEndingsForDestination(string content, string destinationPath)
    {
        var newline = Environment.NewLine;
        if (File.Exists(destinationPath))
        {
            var destinationContent = File.ReadAllText(destinationPath);
            var firstLineFeed = destinationContent.IndexOf('\n');
            if (firstLineFeed >= 0)
                newline = firstLineFeed > 0 && destinationContent[firstLineFeed - 1] == '\r' ? "\r\n" : "\n";
        }

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        return normalized.Replace("\n", newline);
    }

    private static bool SourceCanUseGeneratedBuildBootstrapper(
        ModulePipelinePlan plan,
        string sourcePsm1Path)
    {
        if (plan.BuildSpec.DevelopmentBinariesMode != ModuleDevelopmentBinaryMode.Off)
            return false;

        var sourceHasCustomIncludeScripts = HasCustomIncludeScriptFiles(plan.BuildSpec.SourcePath, plan.Information);
        if (sourceHasCustomIncludeScripts)
            return false;

        if (File.Exists(sourcePsm1Path))
            return IsGeneratedSourceBuildBootstrapper(sourcePsm1Path, plan.ModuleName);

        return !IsSourceSingleFileModule(plan);
    }

    private static bool IsGeneratedSourceBuildBootstrapper(string sourcePsm1Path, string moduleName)
    {
        if (!File.Exists(sourcePsm1Path))
            return false;

        var content = File.ReadAllText(sourcePsm1Path);
        return IsGeneratedSourceBuildBootstrapperContent(content, moduleName);
    }

    private static bool IsGeneratedSourceBuildBootstrapperContent(string content, string moduleName)
        => content.Contains("# Auto-generated by PowerForge. Do not edit.", StringComparison.Ordinal) &&
           content.Contains("# " + moduleName + " bootstrapper", StringComparison.Ordinal);
}
