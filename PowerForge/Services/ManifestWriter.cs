using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Writes a minimal, well-formatted PSD1 manifest from provided fields.
/// Use ManifestEditor for subsequent targeted edits.
/// </summary>
public static class ManifestWriter
{
    /// <summary>
    /// Generates a minimal PSD1 with common fields and writes it as UTF-8 BOM.
    /// Functions/Cmdlets/Aliases arrays are initialized empty; use BuildServices to set exports.
    /// </summary>
    public static void Generate(string path, string moduleName, string moduleVersion, string? author, string? companyName, string? description, string[] compatiblePSEditions, string rootModule, string[] scriptsToProcess)
    {
        var sb = new StringBuilder();
        var nl = Environment.NewLine;
        sb.AppendLine("@{");
        AppendKV(sb, "GUID", "'" + Guid.NewGuid().ToString() + "'");
        if (!string.IsNullOrWhiteSpace(author)) AppendKV(sb, "Author", Quote(author));
        if (!string.IsNullOrWhiteSpace(companyName)) AppendKV(sb, "CompanyName", Quote(companyName));
        if (!string.IsNullOrWhiteSpace(description)) AppendKV(sb, "Description", Quote(description));
        AppendKV(sb, "ModuleVersion", Quote(moduleVersion));
        AppendKV(sb, "PowerShellVersion", Quote("5.1"));
        if (compatiblePSEditions != null && compatiblePSEditions.Length > 0)
            AppendKV(sb, "CompatiblePSEditions", ArrayOf(compatiblePSEditions));
        AppendKV(sb, "RootModule", Quote(rootModule));
        AppendKV(sb, "FunctionsToExport", "@()" );
        AppendKV(sb, "CmdletsToExport", "@()" );
        AppendKV(sb, "AliasesToExport", "@()" );
        if (scriptsToProcess != null && scriptsToProcess.Length > 0)
            AppendKV(sb, "ScriptsToProcess", ArrayOf(scriptsToProcess));
        sb.AppendLine("    PrivateData = @{ ");
        sb.AppendLine("        PSData = @{ }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        static void AppendKV(StringBuilder b, string key, string value)
            => b.AppendLine($"    {key} = {value}");
        static string Quote(string s) => "'" + (s?.Replace("'", "''") ?? string.Empty) + "'";
        static string ArrayOf(string[] arr) => "@(" + string.Join(", ", (arr ?? Array.Empty<string>()).Select(a => "'" + (a?.Replace("'", "''") ?? string.Empty) + "'")) + ")";
    }
}

