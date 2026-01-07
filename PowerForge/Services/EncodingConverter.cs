using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Converts file encodings across a project with safeguards (backups, rollback on mismatch).
/// </summary>
public sealed class EncodingConverter
{
    /// <summary>
    /// Executes encoding conversion using <paramref name="options"/>.
    /// </summary>
    public ProjectConversionResult Convert(EncodingConversionOptions options)
    {
        var root = options.Enumeration.RootPath;
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Project path '{root}' not found.");

        var files = ProjectFileEnumerator.Enumerate(options.Enumeration)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<FileConversion>(files.Length);
        int converted = 0, skipped = 0, errors = 0;

        if (options.CreateBackups && !string.IsNullOrWhiteSpace(options.BackupDirectory)) Directory.CreateDirectory(options.BackupDirectory!);

        foreach (var file in files)
        {
            try
            {
                var detected = DetectEncoding(file);
                var targetEnc = GetTarget(file, options);

                if (SameEncoding(detected, targetEnc))
                {
                    skipped++;
                    results.Add(new FileConversion(file, detected?.WebName, targetEnc.WebName, "Skipped", null, null));
                    continue;
                }

                var shouldConvert = ShouldConvert(detected, options.SourceEncoding);
                if (!shouldConvert && !options.Force)
                {
                    skipped++;
                    results.Add(new FileConversion(file, detected?.WebName, targetEnc.WebName, "Skipped", null, null));
                    continue;
                }

                var backup = options.CreateBackups ? MakeBackupPath(file, root, options.BackupDirectory) : null;
                if (backup != null)
                {
                    var backupDir = Path.GetDirectoryName(backup)!;
                    if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                    File.Copy(file, backup, true);
                }

                var content = File.ReadAllText(file, detected ?? new UTF8Encoding(false));
                File.WriteAllText(file, content, targetEnc);

                // quick verification
                var re = File.ReadAllText(file, targetEnc);
                if (!string.Equals(re, content, StringComparison.Ordinal) && !options.NoRollbackOnMismatch)
                {
                    if (backup != null && File.Exists(backup)) File.Copy(backup, file, true);
                    errors++;
                    results.Add(new FileConversion(file, detected?.WebName, targetEnc.WebName, "Error", backup, "Verification failed; restored from backup"));
                    continue;
                }

                converted++;
                results.Add(new FileConversion(file, detected?.WebName, targetEnc.WebName, "Converted", backup, null));
            }
            catch (Exception ex)
            {
                errors++;
                results.Add(new FileConversion(file, null, GetTarget(file, options).WebName, "Error", null, ex.Message));
            }
        }

        return new ProjectConversionResult(files.Length, converted, skipped, errors, results);
    }

    private static bool IsPowerShellFile(string path)
        => path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ps1xml", StringComparison.OrdinalIgnoreCase);

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

    private static Encoding GetTarget(string file, EncodingConversionOptions options)
    {
        if (options.TargetEncodingResolver is not null)
        {
            var resolved = options.TargetEncodingResolver(file);
            if (resolved.HasValue)
                return GetEncodingByKind(resolved.Value);
        }

        if (options.TargetEncoding.HasValue)
            return GetEncodingByKind(options.TargetEncoding.Value);

        // Default target encoding: prefer UTF8 BOM for PowerShell sources when requested
        if (options.PreferUtf8BomForPowerShell && IsPowerShellFile(file))
            return new UTF8Encoding(true);

        return new UTF8Encoding(false);
    }

    private static Encoding? DetectEncoding(string file)
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

    private static bool ShouldConvert(Encoding? detected, TextEncodingKind source)
    {
        if (source == TextEncodingKind.Any) return true;
        var match = GetEncodingByKind(source);
        return detected == null || string.Equals(detected.WebName, match.WebName, StringComparison.OrdinalIgnoreCase);
    }

    private static Encoding GetEncodingByKind(TextEncodingKind kind)
    {
        return kind switch
        {
            TextEncodingKind.Ascii => Encoding.ASCII,
            TextEncodingKind.BigEndianUnicode => Encoding.BigEndianUnicode,     
            TextEncodingKind.Unicode => Encoding.Unicode,
#pragma warning disable SYSLIB0001
            TextEncodingKind.UTF7 => Encoding.UTF7,
#pragma warning restore SYSLIB0001
            TextEncodingKind.UTF8 => new UTF8Encoding(false),
            TextEncodingKind.UTF8BOM => new UTF8Encoding(true),
            TextEncodingKind.UTF32 => Encoding.UTF32,
            TextEncodingKind.Default => Encoding.Default,
            TextEncodingKind.OEM => GetOemEncoding(),
            _ => new UTF8Encoding(true)
        };
    }

    private static Encoding GetOemEncoding()
    {
        try { return Encoding.GetEncoding(437); } catch { return Encoding.Default; }
    }

    private static bool SameEncoding(Encoding? detected, Encoding target)
    {
        if (detected is null) return false;
        if (detected.CodePage != target.CodePage) return false;

        // Differentiate UTF-8 BOM vs no BOM (WebName is the same).
        return detected.GetPreamble().Length == target.GetPreamble().Length;
    }
}

