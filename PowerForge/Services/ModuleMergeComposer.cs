using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

internal sealed class ModuleMergeSources
{
    internal ModuleMergeSources(string psm1Path, string[] scriptFiles, string mergedScriptContent, bool hasLib)
    {
        Psm1Path = psm1Path;
        ScriptFiles = scriptFiles ?? System.Array.Empty<string>();
        MergedScriptContent = mergedScriptContent ?? string.Empty;
        HasLib = hasLib;
    }

    internal string Psm1Path { get; }
    internal string[] ScriptFiles { get; }
    internal string MergedScriptContent { get; }
    internal bool HasLib { get; }
    internal bool HasScripts => ScriptFiles.Length > 0;
}

internal static partial class ModuleMergeComposer
{
    internal const string MergedSourceStartMarker = "# PowerForge merged source begin";
    internal const string MergedSourceEndMarker = "# PowerForge merged source end";
    internal const string MergedSourcePreambleMarker = "# PowerForge merged source preamble ";

    internal static ModuleMergeSources BuildSources(
        string rootPath,
        string moduleName,
        InformationConfiguration? information,
        ExportSet exports,
        bool fixRelativePaths,
        IReadOnlyDictionary<string, string[]>? conditionalFunctionDependencies = null,
        IReadOnlyList<string>? scriptFiles = null,
        IReadOnlyList<string>? exportAssemblies = null)
    {
        var root = Path.GetFullPath(rootPath);
        var psm1 = Path.Combine(root, $"{moduleName}.psm1");
        var ordered = scriptFiles is null
            ? ResolveScriptFiles(root, information)
            : NormalizeScriptFiles(scriptFiles);

        var merged = ordered.Length > 0
            ? BuildMergedScriptContent(root, ordered, exports, fixRelativePaths, conditionalFunctionDependencies, moduleName)
            : string.Empty;
        var libRoot = Path.Combine(root, "Lib");
        var assemblyFileNames = ModuleBinaryFileLocator.ResolveAssemblyFileNames(moduleName, exportAssemblies);
        var hasLib = ModuleBinaryFileLocator.ContainsAnyFileName(libRoot, assemblyFileNames, SearchOption.AllDirectories);

        return new ModuleMergeSources(psm1, ordered, merged, hasLib);
    }

    internal static void SyncMergedPsm1WithGeneratedScripts(
        string manifestPath,
        string stagingPath,
        string moduleName,
        IEnumerable<string> scriptPaths,
        IReadOnlyDictionary<string, string[]>? conditionalFunctionDependencies = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(stagingPath) || string.IsNullOrWhiteSpace(moduleName))
            return;

        var psm1Path = Path.Combine(stagingPath, $"{moduleName}.psm1");
        if (!File.Exists(psm1Path))
            return;

        var generatedScripts = (scriptPaths ?? System.Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(File.ReadAllText)
            .Where(static content => !string.IsNullOrWhiteSpace(content))
            .ToArray();

        if (generatedScripts.Length == 0)
            return;

        var existing = File.ReadAllText(psm1Path);
        ExtractTrailingExportBlock(existing, out var withoutExportBlock);
        withoutExportBlock = withoutExportBlock.TrimEnd();

        var builder = new StringBuilder(withoutExportBlock);
        foreach (var script in generatedScripts)
        {
            if (builder.Length > 0)
                builder.AppendLine().AppendLine();

            builder.Append(script.TrimEnd());
        }

        var exportBlock = ModuleConditionalExportBlockBuilder.BuildExportBlock(
            ModuleManifestExportReader.ReadExports(manifestPath),
            conditionalFunctionDependencies,
            moduleName).TrimEnd();
        if (!string.IsNullOrWhiteSpace(exportBlock))
        {
            if (builder.Length > 0)
                builder.AppendLine().AppendLine();
            builder.Append(exportBlock);
        }

        WriteMergedPsm1(psm1Path, builder.ToString());
    }

    internal static string PrependFunctions(string[] functions, string content)
    {
        var block = (functions ?? System.Array.Empty<string>())
            .Where(static function => !string.IsNullOrWhiteSpace(function))
            .ToArray();
        if (block.Length == 0)
            return content;

        var prefix = string.Join(System.Environment.NewLine, block);
        var preamble = ExtractMergedScriptPreamble(content, out var body);
        var updatedBody = string.IsNullOrWhiteSpace(body)
            ? prefix
            : prefix + System.Environment.NewLine + System.Environment.NewLine + body;
        return string.IsNullOrWhiteSpace(preamble)
            ? updatedBody
            : preamble + System.Environment.NewLine + System.Environment.NewLine + updatedBody;
    }

    internal static string ExtractMergedScriptPreamble(string? content, out string body)
    {
        body = content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var normalized = body.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var preamble = new List<string>();
        var index = 0;
        for (; index < lines.Length; index++)
        {
            var line = lines[index];
            var directiveStart = 0;
            while (directiveStart < line.Length && char.IsWhiteSpace(line[directiveStart]))
                directiveStart++;

            if (StartsWithDirective(line, directiveStart, "#requires"))
            {
                preamble.Add(line);
                continue;
            }

            if (StartsWithDirective(line, directiveStart, "using"))
            {
                var directiveEnd = FindUsingDirectiveEnd(lines, index, directiveStart);
                for (; index <= directiveEnd; index++)
                    preamble.Add(lines[index]);
                index--;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line) && preamble.Count > 0)
                continue;

            break;
        }

        if (preamble.Count == 0)
            return string.Empty;

        body = string.Join(System.Environment.NewLine, lines.Skip(index)).TrimStart('\r', '\n');
        return string.Join(System.Environment.NewLine, preamble);
    }

    internal static void WriteMergedPsm1(string path, string content)
    {
        GeneratedTextNormalizer.WriteUtf8Bom(path, content);
    }

    internal static bool IsAutoGeneratedPsm1(string content)
        => !string.IsNullOrWhiteSpace(content) &&
           content.IndexOf("Auto-generated by PowerForge", System.StringComparison.OrdinalIgnoreCase) >= 0;

    internal static string[] ResolveScriptFiles(string rootPath, InformationConfiguration? information)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return Array.Empty<string>();

        var root = Path.GetFullPath(rootPath);
        var files = new List<string>();
        foreach (var dir in ResolveMergeDirectories(information))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full))
                continue;

            try
            {
                files.AddRange(
                    Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                        .Where(static file => string.Equals(Path.GetExtension(file), ".ps1", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                // best effort only
            }
        }

        return NormalizeScriptFiles(files);
    }

    private static string[] NormalizeScriptFiles(IEnumerable<string> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return files
            .Where(static file => !string.IsNullOrWhiteSpace(file))
            .Select(Path.GetFullPath)
            .Where(seen.Add)
            .ToArray();
    }

    internal static string[] ResolveMergeDirectories(InformationConfiguration? information)
    {
        IEnumerable<string> configured = information?.IncludePS1 is { Length: > 0 }
            ? information.IncludePS1
            : new[] { "Enums", "Classes", "Private", "Public" };

        if (information?.IncludeToArray is { Length: > 0 })
        {
            foreach (var entry in information.IncludeToArray)
            {
                if (entry is null || !string.Equals(entry.Key, "IncludePS1", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.Values is { Length: > 0 })
                    configured = entry.Values;
            }
        }

        var normalized = configured
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.All(IsStandardMergeDirectory)
            ? normalized.OrderBy(GetStandardMergeDirectoryPriority).ToArray()
            : normalized;
    }

    private static bool IsStandardMergeDirectory(string directory)
        => GetStandardMergeDirectoryPriority(directory) < int.MaxValue;

    private static int GetStandardMergeDirectoryPriority(string directory)
    {
        if (string.Equals(directory, "Enums", System.StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(directory, "Classes", System.StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(directory, "Private", System.StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(directory, "Public", System.StringComparison.OrdinalIgnoreCase))
            return 3;
        return int.MaxValue;
    }

    private static string BuildMergedScriptContent(
        string rootPath,
        IReadOnlyList<string> files,
        ExportSet exports,
        bool fixRelativePaths,
        IReadOnlyDictionary<string, string[]>? conditionalFunctionDependencies,
        string moduleName)
    {
        var requires = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usingLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceBlocks = new List<string>();
        var sourceBlockHasPreamble = new List<bool>();

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                continue;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch
            {
                continue;
            }

            var block = new List<string>();
            var sourceUsingLines = new List<string>();
            var directiveLineReplacements = ExtractPreambleDirectives(
                lines,
                requires,
                usingLines,
                sourceUsingLines,
                file,
                rootPath);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                if (directiveLineReplacements.TryGetValue(lineIndex, out var replacement))
                {
                    if (replacement.Length == 0)
                        continue;

                    line = replacement;
                }

                block.Add(fixRelativePaths ? NormalizeMergedRelativePathReferences(line) : line);
            }

            if (block.Count == 0)
                continue;

            var sourceBlock = string.Join(System.Environment.NewLine, block);
            if (sourceUsingLines.Count > 0)
            {
                var sourcePreamble = string.Join(System.Environment.NewLine, sourceUsingLines);
                var encodedPreamble = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(sourcePreamble));
                sourceBlock = MergedSourcePreambleMarker + encodedPreamble +
                              System.Environment.NewLine + sourceBlock;
            }
            sourceBlocks.Add(sourceBlock);
            sourceBlockHasPreamble.Add(sourceUsingLines.Count > 0);
        }

        var body = new StringBuilder(8192);
        var boundaryToken = ComputeMergedSourceBoundaryToken(sourceBlocks);
        var sourceStartMarker = MergedSourceStartMarker + " " + boundaryToken;
        var sourceEndMarker = MergedSourceEndMarker + " " + boundaryToken;
        var sourcePreambleMarker = MergedSourcePreambleMarker + boundaryToken + " ";
        for (var sourceIndex = 0; sourceIndex < sourceBlocks.Count; sourceIndex++)
        {
            var block = sourceBlocks[sourceIndex];
            if (sourceBlockHasPreamble[sourceIndex])
                block = sourcePreambleMarker + block.Substring(MergedSourcePreambleMarker.Length);
            body.AppendLine(sourceStartMarker);
            body.AppendLine(block);
            body.AppendLine(sourceEndMarker);
            body.AppendLine();
        }

        var header = new StringBuilder(1024);
        foreach (var require in requires.OrderBy(static line => line, StringComparer.OrdinalIgnoreCase))
            header.AppendLine(require);
        foreach (var usingLine in usingLines.OrderBy(static line => line, StringComparer.OrdinalIgnoreCase))
            header.AppendLine(usingLine);

        if (header.Length > 0)
            header.AppendLine();

        header.Append(body.ToString().TrimEnd());

        var merged = header.ToString().TrimEnd();
        var exportBlock = ModuleConditionalExportBlockBuilder.BuildExportBlock(
            exports,
            conditionalFunctionDependencies,
            moduleName);
        if (!string.IsNullOrWhiteSpace(exportBlock))
        {
            if (!string.IsNullOrWhiteSpace(merged))
                merged += System.Environment.NewLine + System.Environment.NewLine;
            merged += exportBlock.TrimEnd();
        }

        return merged;
    }

    private static Dictionary<int, string> ExtractPreambleDirectives(
        IReadOnlyList<string> lines,
        ISet<string> requires,
        ISet<string> usingLines,
        ICollection<string> sourceUsingLines,
        string sourcePath,
        string moduleRoot)
    {
        // PowerShell using statements are valid only in the script preamble. Restricting extraction to that region
        // prevents embedded languages in here-strings and block comments from being mistaken for module directives.
        var lineReplacements = new Dictionary<int, string>();
        var blockCommentDepth = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            var kind = ClassifyPreambleLine(lines[index], ref blockCommentDepth, out var directiveStart);
            if (kind == PreambleLineKind.Trivia)
                continue;

            if (kind == PreambleLineKind.Requires)
            {
                requires.Add(lines[index].Substring(directiveStart));
                lineReplacements[index] = lines[index].Substring(0, directiveStart).TrimEnd();
                continue;
            }

            if (kind == PreambleLineKind.Using)
            {
                var directiveEnd = FindUsingDirectiveEnd(lines, index, directiveStart);
                var directive = new StringBuilder(lines[index].Substring(directiveStart));
                for (var directiveLine = index + 1; directiveLine <= directiveEnd; directiveLine++)
                    directive.AppendLine().Append(lines[directiveLine]);

                var rebasedDirective = RebaseUsingDirective(
                    directive.ToString(),
                    sourcePath,
                    moduleRoot);
                usingLines.Add(rebasedDirective);
                sourceUsingLines.Add(rebasedDirective);
                for (var directiveLine = index; directiveLine <= directiveEnd; directiveLine++)
                {
                    lineReplacements[directiveLine] = directiveLine == index
                        ? lines[index].Substring(0, directiveStart).TrimEnd()
                        : string.Empty;
                }

                index = directiveEnd;
                continue;
            }

            break;
        }

        return lineReplacements;
    }

    internal static bool TryResolveMergedSourceMarkers(
        string? content,
        out string startMarker,
        out string endMarker)
    {
        startMarker = MergedSourceStartMarker;
        endMarker = MergedSourceEndMarker;
        if (content is null || string.IsNullOrWhiteSpace(content))
            return false;

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var prefix = MergedSourceStartMarker + " ";
        foreach (var line in normalized.Split('\n'))
        {
            if (!line.StartsWith(prefix, System.StringComparison.Ordinal))
                continue;

            var token = line.Substring(prefix.Length).Trim();
            if (token.Length == 0)
                continue;

            var candidateEnd = MergedSourceEndMarker + " " + token;
            if (normalized.IndexOf(candidateEnd, System.StringComparison.Ordinal) < 0)
                continue;

            startMarker = line;
            endMarker = candidateEnd;
            return true;
        }

        return false;
    }

    private static string ComputeMergedSourceBoundaryToken(IReadOnlyList<string> sourceBlocks)
    {
        var content = string.Join("\u001f", sourceBlocks ?? Array.Empty<string>());
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static PreambleLineKind ClassifyPreambleLine(
        string line,
        ref int blockCommentDepth,
        out int directiveStart)
    {
        var index = 0;
        directiveStart = -1;

        while (index < line.Length)
        {
            if (blockCommentDepth > 0)
            {
                var commentStart = line.IndexOf("<#", index, System.StringComparison.Ordinal);
                var commentEnd = line.IndexOf("#>", index, System.StringComparison.Ordinal);
                if (commentStart >= 0 && (commentEnd < 0 || commentStart < commentEnd))
                {
                    blockCommentDepth++;
                    index = commentStart + 2;
                    continue;
                }

                if (commentEnd < 0)
                    return PreambleLineKind.Trivia;

                blockCommentDepth--;
                index = commentEnd + 2;
                continue;
            }

            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;

            if (index >= line.Length)
                return PreambleLineKind.Trivia;

            if (line.IndexOf("<#", index, System.StringComparison.Ordinal) == index)
            {
                blockCommentDepth++;
                index += 2;
                continue;
            }

            if (line[index] == '#')
            {
                if (!StartsWithDirective(line, index, "#requires"))
                    return PreambleLineKind.Trivia;

                directiveStart = index;
                return PreambleLineKind.Requires;
            }

            if (StartsWithDirective(line, index, "using"))
            {
                directiveStart = index;
                return PreambleLineKind.Using;
            }

            return PreambleLineKind.Code;
        }

        return PreambleLineKind.Trivia;
    }

    private static bool StartsWithDirective(string line, int index, string directive)
    {
        if (line.IndexOf(directive, index, System.StringComparison.OrdinalIgnoreCase) != index)
            return false;

        var boundary = index + directive.Length;
        return boundary == line.Length || char.IsWhiteSpace(line[boundary]);
    }

    private enum PreambleLineKind
    {
        Trivia,
        Requires,
        Using,
        Code
    }

    private static string NormalizeMergedRelativePathReferences(string line)
    {
        if (string.IsNullOrEmpty(line))
            return line ?? string.Empty;

        var updated = line;
        // Legacy merged modules historically normalized both literal and evaluated parent-root references back to the
        // merged module root so inline script assets still resolve after the original folder hierarchy disappears.
        updated = updated.Replace("$PSScriptRoot\\..\\..\\", "$PSScriptRoot\\");
        updated = updated.Replace("$PSScriptRoot\\..\\", "$PSScriptRoot\\");
        updated = updated.Replace("$PSScriptRoot/../../", "$PSScriptRoot/");
        updated = updated.Replace("$PSScriptRoot/../", "$PSScriptRoot/");
        updated = updated.Replace("`$PSScriptRoot, '..',", "$PSScriptRoot,");
        updated = updated.Replace("`$PSScriptRoot,'..',", "$PSScriptRoot,");
        updated = updated.Replace("$PSScriptRoot, '..',", "$PSScriptRoot,");
        updated = updated.Replace("$PSScriptRoot,'..',", "$PSScriptRoot,");
        updated = updated.Replace("$PSScriptRoot, \"..\",", "$PSScriptRoot,");
        updated = updated.Replace("$PSScriptRoot,\"..\",", "$PSScriptRoot,");
        return updated;
    }

    internal static string ExtractTrailingExportBlock(string content, out string body)
    {
        var source = content ?? string.Empty;
        body = source;
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var normalized = source.Replace("\r\n", "\n");
        // The generated export block is expected to be the tail of the merged PSM1, so syncing generated scripts
        // replaces that trailing block wholesale before appending a fresh one from the manifest.
        var exportStart = normalized.LastIndexOf("\n$FunctionsToExport = ", System.StringComparison.Ordinal);
        if (exportStart < 0 && normalized.StartsWith("$FunctionsToExport = ", System.StringComparison.Ordinal))
            exportStart = 0;

        if (exportStart < 0)
            return string.Empty;

        const string exportLine = "Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport";
        var exportLineIndex = normalized.IndexOf(exportLine, exportStart, System.StringComparison.Ordinal);
        if (exportLineIndex < 0)
            return string.Empty;

        body = normalized.Substring(0, exportStart).TrimEnd('\n');
        return normalized.Substring(exportStart).TrimStart('\n').TrimEnd('\n');
    }
}
