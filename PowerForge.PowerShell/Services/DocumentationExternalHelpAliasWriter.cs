using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Creates the assembly-named external-help aliases that PowerShell expects when a binary is imported directly.
/// </summary>
internal static class DocumentationExternalHelpAliasWriter
{
    private const string LegacyGeneratedAliasMarker = "<!-- PowerForgeGeneratedExternalHelpAlias -->";
    private const string GeneratedAliasMarkerPrefix = "<!-- PowerForgeGeneratedExternalHelpAlias:";

    public static void PruneGeneratedAliases(
        string rootDirectory,
        string moduleName,
        string? primaryFileName = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) ||
            string.IsNullOrWhiteSpace(moduleName) ||
            !Directory.Exists(rootDirectory)) return;

        var marker = GetGeneratedAliasMarker(moduleName);
        var legacyPrimaryContent = GetLegacyPrimaryContent(rootDirectory, primaryFileName);
        var nestedModuleRoots = GetNestedModuleRoots(rootDirectory);

        foreach (var aliasPath in Directory.EnumerateFiles(
                     rootDirectory,
                     "*",
                     SearchOption.AllDirectories)
                 .Where(path => Path.GetFileName(path)
                     .EndsWith(".dll-Help.xml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var prefix = ReadPrefix(aliasPath);
                var insideNestedModule = nestedModuleRoots.Any(root => IsPathWithinRoot(root, aliasPath));
                var ownedByCurrentModule =
                    !insideNestedModule &&
                    prefix.Contains(marker, StringComparison.Ordinal);
                var legacyOwnedByCurrentModule =
                    legacyPrimaryContent.Count > 0 &&
                    prefix.Contains(LegacyGeneratedAliasMarker, StringComparison.Ordinal) &&
                    !insideNestedModule &&
                    legacyPrimaryContent.Contains(NormalizeLegacyContent(File.ReadAllText(aliasPath)));
                if (ownedByCurrentModule || legacyOwnedByCurrentModule)
                    File.Delete(aliasPath);
            }
            catch
            {
                // Best effort only. A locked or independently authored file must not block documentation generation.
            }
        }
    }

    public static IReadOnlyList<string> WriteAliases(
        DocumentationExtractionPayload payload,
        string externalHelpFilePath,
        string moduleName)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("Module name is required.", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(externalHelpFilePath) || !File.Exists(externalHelpFilePath))
            return Array.Empty<string>();

        var output = new List<string> { Path.GetFullPath(externalHelpFilePath) };
        var directory = Path.GetDirectoryName(Path.GetFullPath(externalHelpFilePath))!;
        var culture = new DirectoryInfo(directory).Name;
        var stagingRoot = Directory.GetParent(directory)?.FullName ?? directory;
        var pathComparison = FrameworkCompatibility.GetPathStringComparison(stagingRoot);
        var pathComparer = pathComparison == StringComparison.Ordinal
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var assemblyPaths = (payload.Commands ?? new List<DocumentationCommandHelp>())
            .Where(command => command is not null && !string.IsNullOrWhiteSpace(command.AssemblyPath))
            .Select(command => command.AssemblyPath!.Trim().Trim('"'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(stagingRoot, path)))
            .Distinct(pathComparer)
            .OrderBy(path => path, pathComparer);

        var marker = GetGeneratedAliasMarker(moduleName);
        var content = File.ReadAllText(externalHelpFilePath);
        var declarationEnd = content.IndexOf("?>", StringComparison.Ordinal);
        content = declarationEnd >= 0
            ? content.Insert(declarationEnd + 2, Environment.NewLine + marker)
            : marker + Environment.NewLine + content;

        foreach (var assemblyPath in assemblyPaths)
        {
            if (!IsPathWithinRoot(stagingRoot, assemblyPath))
                continue;

            var aliasName = Path.GetFileName(assemblyPath) + "-Help.xml";
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? stagingRoot;
            var aliasDirectory = Path.Combine(assemblyDirectory, culture);
            var aliasPath = Path.Combine(aliasDirectory, aliasName);
            if (string.Equals(aliasPath, externalHelpFilePath, pathComparison))
                continue;

            Directory.CreateDirectory(aliasDirectory);
            if (!CanWriteGeneratedAlias(aliasPath, moduleName))
                continue;
            File.WriteAllText(aliasPath, content, new System.Text.UTF8Encoding(false));
            output.Add(aliasPath);
        }

        return output;
    }

    internal static string GetGeneratedAliasMarker(string moduleName)
        => GeneratedAliasMarkerPrefix +
           Convert.ToBase64String(Encoding.UTF8.GetBytes((moduleName ?? string.Empty).ToUpperInvariant())) +
           " -->";

    internal static string GetLegacyGeneratedAliasMarker()
        => LegacyGeneratedAliasMarker;

    internal static bool IsGeneratedAlias(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        var prefix = ReadPrefix(path);
        return prefix.Contains(GeneratedAliasMarkerPrefix, StringComparison.Ordinal) ||
               prefix.Contains(LegacyGeneratedAliasMarker, StringComparison.Ordinal);
    }

    internal static bool CanWriteGeneratedAlias(string path, string moduleName)
    {
        if (!File.Exists(path)) return true;
        var prefix = ReadPrefix(path);
        return prefix.Contains(GetGeneratedAliasMarker(moduleName), StringComparison.Ordinal) ||
               prefix.Contains(LegacyGeneratedAliasMarker, StringComparison.Ordinal);
    }

    private static string ReadPrefix(string path)
    {
        using var reader = new StreamReader(path);
        var prefix = new char[512];
        var read = reader.Read(prefix, 0, prefix.Length);
        return new string(prefix, 0, read);
    }

    private static HashSet<string> GetLegacyPrimaryContent(string rootDirectory, string? primaryFileName)
    {
        var content = new HashSet<string>(StringComparer.Ordinal);
        var fullRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathComparison = FrameworkCompatibility.GetPathStringComparison(fullRoot);
        var preferredFileName = Path.GetFileName(primaryFileName ?? string.Empty);

        foreach (var path in Directory.EnumerateFiles(
                     rootDirectory,
                     "*",
                     SearchOption.AllDirectories)
                 .Where(path =>
                 {
                     var fileName = Path.GetFileName(path);
                     return fileName.EndsWith("-Help.xml", StringComparison.OrdinalIgnoreCase) &&
                            (!fileName.EndsWith(".dll-Help.xml", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(fileName, preferredFileName, StringComparison.OrdinalIgnoreCase));
                 })
                 .OrderByDescending(path => string.Equals(
                     Path.GetFileName(path), preferredFileName, StringComparison.OrdinalIgnoreCase)))
        {
            var parent = Path.GetDirectoryName(path);
            var candidateRoot = string.IsNullOrWhiteSpace(parent)
                ? null
                : Directory.GetParent(parent)?.FullName;
            if (!string.Equals(fullRoot,
                    candidateRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    pathComparison))
                continue;

            try
            {
                var text = File.ReadAllText(path);
                if (text.Contains(GeneratedAliasMarkerPrefix, StringComparison.Ordinal) ||
                    text.Contains(LegacyGeneratedAliasMarker, StringComparison.Ordinal))
                    continue;
                content.Add(NormalizeLegacyContent(text));
            }
            catch { /* best effort */ }
        }

        return content;
    }

    private static string[] GetNestedModuleRoots(string rootDirectory)
    {
        var pathComparison = FrameworkCompatibility.GetPathStringComparison(rootDirectory);
        var pathComparer = pathComparison == StringComparison.Ordinal
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        return Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            .Where(IsModuleManifest)
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                           !string.Equals(
                               Path.GetFullPath(path!),
                               Path.GetFullPath(rootDirectory),
                               pathComparison))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(pathComparer)
            .ToArray();
    }

    private static bool IsModuleManifest(string path)
    {
        try
        {
            var ast = Parser.ParseFile(path, out _, out var errors);
            if (errors is { Length: > 0 }) return false;
            var manifest = ast.Find(
                node => node is HashtableAst table && !HasHashtableAncestor(table),
                searchNestedScriptBlocks: false) as HashtableAst;
            if (manifest is null) return false;
            var keys = manifest.KeyValuePairs
                .Select(pair => GetManifestKey(pair.Item1))
                .Where(key => !string.IsNullOrEmpty(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return keys.Contains("ModuleVersion") && keys.Overlaps(ModuleManifestContentKeys);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasHashtableAncestor(Ast ast)
    {
        for (var parent = ast.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is HashtableAst) return true;
        }
        return false;
    }

    private static string? GetManifestKey(ExpressionAst key)
        => key switch
        {
            StringConstantExpressionAst text => text.Value,
            ConstantExpressionAst constant when constant.Value is string value => value,
            _ => null
        };

    private static readonly string[] ModuleManifestContentKeys =
    {
        "RootModule", "ModuleToProcess", "NestedModules", "RequiredModules",
        "FunctionsToExport", "CmdletsToExport", "AliasesToExport", "VariablesToExport",
        "FormatsToProcess", "TypesToProcess", "ScriptsToProcess"
    };

    private static string NormalizeLegacyContent(string content)
    {
        var normalized = (content ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
        normalized = normalized
            .Replace("\n" + LegacyGeneratedAliasMarker, string.Empty)
            .Replace(LegacyGeneratedAliasMarker + "\n", string.Empty)
            .Replace(LegacyGeneratedAliasMarker, string.Empty);
        return normalized.Trim();
    }

    private static bool IsPathWithinRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var pathComparison = FrameworkCompatibility.GetPathStringComparison(fullRoot);
        return string.Equals(fullRoot, fullPath, pathComparison) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, pathComparison);
    }
}
