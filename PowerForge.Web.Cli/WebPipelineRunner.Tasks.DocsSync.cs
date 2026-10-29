using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteDocsSync(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var config = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(config)) throw new InvalidOperationException("docs-sync requires config.");
        var spec = JsonSerializer.Deserialize(File.ReadAllText(config), WebCliJson.Context.MarkdownCodeSyncSpec)
            ?? throw new InvalidDataException("Documentation synchronization configuration is empty.");
        spec.RootPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(config)!, spec.RootPath));
        var apply = GetBool(step, "apply") ?? false;
        var result = MarkdownCodeSynchronizer.Synchronize(spec, apply);
        stepResult.Success = apply || result.ChangedFiles.Length == 0;
        stepResult.Message = $"Checked {result.BlockCount} code blocks in {result.FileCount} Markdown files; {result.ChangedFiles.Length} {(apply ? "updated" : "stale")} files.";
        if (!stepResult.Success)
            throw new InvalidOperationException(stepResult.Message + " Run powerforge docs sync --config \"" + config + "\" --apply. Stale: " + string.Join(", ", result.ChangedFiles));
    }
}
