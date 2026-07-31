using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteVisualStory(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var manifestValue = GetString(step, "manifest") ??
                            GetString(step, "manifestPath") ??
                            GetString(step, "manifest-path");
        var outputValue = GetString(step, "out") ??
                          GetString(step, "output") ??
                          GetString(step, "outputPath") ??
                          GetString(step, "output-path");
        if (string.IsNullOrWhiteSpace(manifestValue))
            throw new InvalidOperationException("visual-story requires a producer manifest path.");
        if (string.IsNullOrWhiteSpace(outputValue))
            throw new InvalidOperationException("visual-story requires an output path.");

        var manifestPath = ResolvePath(baseDir, manifestValue)
                           ?? throw new InvalidOperationException("visual-story manifest path could not be resolved.");
        var outputPath = ResolvePath(baseDir, outputValue)
                         ?? throw new InvalidOperationException("visual-story output path could not be resolved.");
        EnsureVisualStoryPathWithinBase(baseDir, manifestPath, "manifest");
        EnsureVisualStoryPathWithinBase(baseDir, outputPath, "output");

        var command = GetString(step, "command") ?? GetString(step, "cmd") ?? GetString(step, "file");
        if (!string.IsNullOrWhiteSpace(command))
        {
            if (GetBool(step, "allowFailure") == true || GetBool(step, "continueOnError") == true)
                throw new InvalidOperationException("visual-story producer failures cannot be ignored.");
            ExecuteExec(step, baseDir, new WebPipelineStepResult());
            EnsureVisualStoryPathWithinBase(baseDir, manifestPath, "manifest");
            EnsureVisualStoryPathWithinBase(baseDir, outputPath, "output");
        }

        var maximumArtifactBytes = GetLong(step, "maximumArtifactBytes") ??
                                   GetLong(step, "maximum-artifact-bytes") ??
                                   25L * 1024L * 1024L;
        var maximumTotalArtifactBytes = GetLong(step, "maximumTotalArtifactBytes") ??
                                        GetLong(step, "maximum-total-artifact-bytes") ??
                                        100L * 1024L * 1024L;
        var overwrite = GetBool(step, "overwrite") ?? true;

        var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
        {
            ManifestPath = manifestPath,
            OutputPath = outputPath,
            MaximumArtifactBytes = maximumArtifactBytes,
            MaximumTotalArtifactBytes = maximumTotalArtifactBytes,
            Overwrite = overwrite
        });

        stepResult.Success = true;
        stepResult.Message =
            $"visual-story staged {result.ArtifactCount} artifacts ({result.TotalBytes} bytes): {result.Bundle.Id}";
    }

    private static void EnsureVisualStoryPathWithinBase(string baseDir, string path, string label)
    {
        try
        {
            VisualStoryPathGuard.EnsureContainedPath(
                baseDir,
                path,
                label,
                allowRoot: string.Equals(
                    Path.GetFullPath(baseDir),
                    Path.GetFullPath(path),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"visual-story {label} path must remain within the pipeline root. {ex.Message}",
                ex);
        }
    }
}
