using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static Dictionary<string, IReadOnlyDictionary<string, string>> ReadProjectDocsLinkMappings(JsonElement step)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!step.TryGetProperty("linkMappings", out var projects))
            return result;
        if (projects.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("project-docs-sync linkMappings must be an object keyed by project slug.");

        foreach (var project in projects.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(project.Name) || project.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Each project-docs-sync linkMappings entry requires a project slug and an object of URL prefix mappings.");
            var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prefix in project.Value.EnumerateObject())
            {
                if (prefix.Value.ValueKind != JsonValueKind.String ||
                    !prefixes.TryAdd(prefix.Name, prefix.Value.GetString()!))
                    throw new InvalidOperationException($"Invalid or duplicate link mapping for project '{project.Name}'.");
            }
            // Validate even when a configured project is absent from this sync's selection.
            MarkdownLinkMapper.Rewrite(string.Empty, prefixes);
            if (!result.TryAdd(project.Name.Trim(), prefixes))
                throw new InvalidOperationException($"Duplicate linkMappings project '{project.Name}'.");
        }
        return result;
    }

    private static void CopyProjectDocumentationFile(string sourceFile, string targetFile, string slug,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> linkMappings)
    {
        File.Copy(sourceFile, targetFile, overwrite: true);
        if (!IsMarkdownExtension(Path.GetExtension(targetFile)) ||
            !linkMappings.TryGetValue(slug, out var mappings) || mappings.Count == 0)
            return;
        var original = File.ReadAllText(targetFile);
        var mapped = MarkdownLinkMapper.Rewrite(original, mappings);
        if (!string.Equals(original, mapped, StringComparison.Ordinal))
            File.WriteAllText(targetFile, mapped);
    }
}
