using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Creates the assembly-named external-help aliases that PowerShell expects when a binary is imported directly.
/// </summary>
internal static class DocumentationExternalHelpAliasWriter
{
    private const string GeneratedAliasMarker = "<!-- PowerForgeGeneratedExternalHelpAlias -->";

    public static void PruneGeneratedAliases(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return;

        foreach (var aliasPath in Directory.EnumerateFiles(
                     rootDirectory,
                     "*.dll-Help.xml",
                     SearchOption.AllDirectories))
        {
            try
            {
                bool generated;
                using (var reader = new StreamReader(aliasPath))
                {
                    var prefix = new char[512];
                    var read = reader.Read(prefix, 0, prefix.Length);
                    generated = new string(prefix, 0, read)
                        .Contains(GeneratedAliasMarker, StringComparison.Ordinal);
                }
                if (generated)
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
        string externalHelpFilePath)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(externalHelpFilePath) || !File.Exists(externalHelpFilePath))
            return Array.Empty<string>();

        var output = new List<string> { Path.GetFullPath(externalHelpFilePath) };
        var directory = Path.GetDirectoryName(Path.GetFullPath(externalHelpFilePath))!;
        var culture = new DirectoryInfo(directory).Name;
        var stagingRoot = Directory.GetParent(directory)?.FullName ?? directory;
        var primaryFileName = Path.GetFileName(externalHelpFilePath);
        var assemblyPaths = (payload.Commands ?? new List<DocumentationCommandHelp>())
            .Where(command => command is not null && !string.IsNullOrWhiteSpace(command.AssemblyPath))
            .Select(command => command.AssemblyPath!.Trim().Trim('"'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(stagingRoot, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        var content = File.ReadAllText(externalHelpFilePath);
        var declarationEnd = content.IndexOf("?>", StringComparison.Ordinal);
        content = declarationEnd >= 0
            ? content.Insert(declarationEnd + 2, Environment.NewLine + GeneratedAliasMarker)
            : GeneratedAliasMarker + Environment.NewLine + content;

        foreach (var assemblyPath in assemblyPaths)
        {
            if (!IsPathWithinRoot(stagingRoot, assemblyPath))
                continue;

            var aliasName = Path.GetFileName(assemblyPath) + "-Help.xml";
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? stagingRoot;
            var aliasDirectory = Path.Combine(assemblyDirectory, culture);
            var aliasPath = Path.Combine(aliasDirectory, aliasName);
            if (string.Equals(aliasPath, externalHelpFilePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aliasName, primaryFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(aliasDirectory);
            File.WriteAllText(aliasPath, content, new System.Text.UTF8Encoding(false));
            output.Add(aliasPath);
        }

        return output;
    }

    private static bool IsPathWithinRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        return string.Equals(fullRoot, fullPath, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
