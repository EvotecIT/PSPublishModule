using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Converts line endings across a project with optional restrictions and final newline enforcement.
/// </summary>
public sealed class LineEndingConverter
{
    /// <summary>
    /// Executes line ending conversion for a project.
    /// </summary>
    public ProjectConversionResult Convert(LineEndingConversionOptions options)
    {
        var root = options.Enumeration.RootPath;
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Project path '{root}' not found.");

        var files = ProjectFileEnumerator.Enumerate(options.Enumeration)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<FileConversion>(files.Length);
        int converted = 0, skipped = 0, errors = 0;

        if (options.CreateBackups && !string.IsNullOrWhiteSpace(options.BackupDirectory)) Directory.CreateDirectory(options.BackupDirectory!);

        string targetNewLine = options.Target == LineEnding.CRLF ? "\r\n" : "\n";

        foreach (var file in files)
        {
            try
            {
                var readEnc = DetectEncodingForRead(file);
                var text = File.ReadAllText(file, readEnc);

                bool hasCrlf = text.Contains("\r\n");
                bool hasLf = text.Contains("\n");
                bool mixed = hasCrlf && (text.Replace("\r\n", "").Contains("\n"));
                bool missingFinal = text.Length == 0 || !(text.EndsWith("\n") || text.EndsWith("\r"));

                if (options.OnlyMixed && !mixed) { skipped++; results.Add(new FileConversion(file, readEnc.WebName, readEnc.WebName, "Skipped", null, null)); continue; }
                if (options.OnlyMissingNewline && !missingFinal) { skipped++; results.Add(new FileConversion(file, readEnc.WebName, readEnc.WebName, "Skipped", null, null)); continue; }

                if (!options.Force)
                {
                    // If already consistent with target and final newline condition satisfied, skip
                    var targetNowKind = hasCrlf ? LineEnding.CRLF : LineEnding.LF;
                    if (targetNowKind == options.Target && (!options.EnsureFinalNewline || !missingFinal))
                    { skipped++; results.Add(new FileConversion(file, readEnc.WebName, readEnc.WebName, "Skipped", null, null)); continue; }
                }

                string normalized = NormalizeEndings(text, targetNewLine);
                if (options.EnsureFinalNewline && !normalized.EndsWith(targetNewLine))
                    normalized += targetNewLine;

                var backup = options.CreateBackups ? MakeBackupPath(file, root, options.BackupDirectory) : null;
                if (backup != null)
                {
                    var bdir = Path.GetDirectoryName(backup)!;
                    if (!Directory.Exists(bdir)) Directory.CreateDirectory(bdir);
                    File.Copy(file, backup, true);
                }

                // Choose encoding for write: prefer BOM for PowerShell sources when requested
                var writeEnc = SelectWriteEncoding(file, readEnc, options.PreferUtf8BomForPowerShell);
                File.WriteAllText(file, normalized, writeEnc);
                converted++;
                results.Add(new FileConversion(file, readEnc.WebName, writeEnc.WebName, "Converted", backup, null));
            }
            catch (Exception ex)
            {
                errors++;
                results.Add(new FileConversion(file, null, null ?? string.Empty, "Error", null, ex.Message));
            }
        }

        return new ProjectConversionResult(files.Length, converted, skipped, errors, results);
    }

    private static string MakeBackupPath(string file, string root, string? backupRoot)
        => string.IsNullOrWhiteSpace(backupRoot)
            ? file + ".bak"
            : Path.Combine(backupRoot!, ComputeRelativePath(root, file));

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(baseDir));
            var pathUri = new Uri(fullPath);
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch { return Path.GetFileName(fullPath); }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString()) ? path : path + Path.DirectorySeparatorChar;

    private static Encoding DetectEncodingForRead(string file)
    {
        var bom = new byte[4];
        using (var fs = File.OpenRead(file)) { _ = fs.Read(bom, 0, 4); }
        if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return new UTF8Encoding(true);
        if (bom[0] == 0xff && bom[1] == 0xfe && bom[2] == 0 && bom[3] == 0) return Encoding.UTF32;
        if (bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xfe && bom[3] == 0xff) return Encoding.GetEncoding(12001);
        if (bom[0] == 0xff && bom[1] == 0xfe) return Encoding.Unicode;
        if (bom[0] == 0xfe && bom[1] == 0xff) return Encoding.BigEndianUnicode;
        return new UTF8Encoding(false);
    }

    private static Encoding SelectWriteEncoding(string file, Encoding readEncoding, bool preferUtf8BomForPs)
    {
        if (preferUtf8BomForPs && IsPowerShellFile(file)) return new UTF8Encoding(true);
        return readEncoding;
    }

    private static bool IsPowerShellFile(string path)
        => path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ps1xml", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEndings(string text, string target)
    {
        if (target == "\r\n") return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return text.Replace("\r\n", "\n");
    }
}

