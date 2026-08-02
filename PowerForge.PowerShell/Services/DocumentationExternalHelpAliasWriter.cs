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

    public static void PruneGeneratedAliases(string externalHelpDirectory)
    {
        if (string.IsNullOrWhiteSpace(externalHelpDirectory) || !Directory.Exists(externalHelpDirectory)) return;

        foreach (var aliasPath in Directory.EnumerateFiles(
                     externalHelpDirectory,
                     "*.dll-Help.xml",
                     SearchOption.TopDirectoryOnly))
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
        var primaryFileName = Path.GetFileName(externalHelpFilePath);
        var aliasNames = (payload.Commands ?? new List<DocumentationCommandHelp>())
            .Where(command => command is not null && !string.IsNullOrWhiteSpace(command.AssemblyPath))
            .Select(command => Path.GetFileName(command.AssemblyPath!.Trim().Trim('"')))
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => fileName + "-Help.xml")
            .Where(fileName => !string.Equals(fileName, primaryFileName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase);

        foreach (var aliasName in aliasNames)
        {
            var aliasPath = Path.Combine(directory, aliasName);
            var content = File.ReadAllText(externalHelpFilePath);
            var declarationEnd = content.IndexOf("?>", StringComparison.Ordinal);
            content = declarationEnd >= 0
                ? content.Insert(declarationEnd + 2, Environment.NewLine + GeneratedAliasMarker)
                : GeneratedAliasMarker + Environment.NewLine + content;
            File.WriteAllText(aliasPath, content, new System.Text.UTF8Encoding(false));
            output.Add(aliasPath);
        }

        return output;
    }
}
