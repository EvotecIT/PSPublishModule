using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteSearchIndexExport(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var strict = GetBool(step, "strict") ?? true;
        var maxItems = GetInt(step, "maxItems") ?? GetInt(step, "max-items") ?? 5000;

        var sourcePath = ResolvePath(baseDir,
            GetString(step, "source") ??
            GetString(step, "input") ??
            GetString(step, "path") ??
            "./_site/search/index.json");
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("search-index-export requires source/input/path.");
        sourcePath = Path.GetFullPath(sourcePath);

        var outputPath = ResolvePath(baseDir,
            GetString(step, "out") ??
            GetString(step, "output") ??
            GetString(step, "outputPath") ??
            GetString(step, "output-path") ??
            "./_site/search-index.json");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("search-index-export requires out/output path.");
        outputPath = Path.GetFullPath(outputPath);

        var summaryPath = ResolvePath(baseDir,
            GetString(step, "summaryPath") ??
            GetString(step, "summary-path") ??
            "./Build/generate-search-index-last-run.json");

        if (!File.Exists(sourcePath))
        {
            if (strict)
                throw new InvalidOperationException($"search-index-export source file not found: {sourcePath}");

            if (!string.IsNullOrWhiteSpace(summaryPath))
                WriteSearchIndexExportSummary(summaryPath, sourcePath, outputPath, sourceCount: 0, exportedCount: 0, truncated: false, status: "skipped");

            stepResult.Success = true;
            stepResult.Message = $"search-index-export skipped: source not found '{sourcePath}'.";
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"search-index-export source payload must be an array: {sourcePath}");

        var sourceCount = document.RootElement.GetArrayLength();
        var takeCount = maxItems > 0 ? Math.Min(sourceCount, maxItems) : sourceCount;
        var exportedEntries = new List<JsonElement>(takeCount);
        var index = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (index >= takeCount)
                break;
            exportedEntries.Add(item.Clone());
            index++;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var serializeOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(exportedEntries, serializeOptions));

        var truncated = sourceCount > takeCount;
        if (!string.IsNullOrWhiteSpace(summaryPath))
            WriteSearchIndexExportSummary(summaryPath, sourcePath, outputPath, sourceCount, takeCount, truncated, status: "updated");

        stepResult.Success = true;
        stepResult.Message = $"search-index-export ok: exported={takeCount}/{sourceCount}";
    }

    private static void WriteSearchIndexExportSummary(
        string summaryPath,
        string sourcePath,
        string outputPath,
        int sourceCount,
        int exportedCount,
        bool truncated,
        string status)
    {
        var summaryDirectory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(summaryDirectory))
            Directory.CreateDirectory(summaryDirectory);

        var summary = new
        {
            generatedOn = DateTimeOffset.UtcNow.ToString("O"),
            status,
            sourcePath = Path.GetFullPath(sourcePath),
            outputPath = Path.GetFullPath(outputPath),
            sourceItems = sourceCount,
            items = exportedCount,
            truncated
        };
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }
}
