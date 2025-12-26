using System;
using System.IO;

namespace PowerForge;

internal static class ProjectTextDetector
{
    internal static TextEncodingKind DetectEncodingKind(string path)
    {
        // Match legacy Get-FileEncoding helper: BOM first; fallback to ASCII vs UTF8 scan.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bom = new byte[4];
        _ = fs.Read(bom, 0, 4);

        if (bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xfe && bom[3] == 0xff) return TextEncodingKind.UTF32;
        if (bom[0] == 0xff && bom[1] == 0xfe && bom[2] == 0x00 && bom[3] == 0x00) return TextEncodingKind.UTF32;
        if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return TextEncodingKind.UTF8BOM;
        if (bom[0] == 0xff && bom[1] == 0xfe) return TextEncodingKind.Unicode;
        if (bom[0] == 0xfe && bom[1] == 0xff) return TextEncodingKind.BigEndianUnicode;
        if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76) return TextEncodingKind.UTF7;

        fs.Position = 0;
        var buf = new byte[4096];
        int read;
        while ((read = fs.Read(buf, 0, buf.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buf[i] > 0x7F) return TextEncodingKind.UTF8;
            }
        }
        return TextEncodingKind.Ascii;
    }

    internal static (DetectedLineEndingKind Kind, bool HasFinalNewline) DetectLineEnding(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return (DetectedLineEndingKind.None, true);

        int crlfCount = 0, lfOnlyCount = 0, crOnlyCount = 0;
        bool pendingCr = false;
        int lastByte = -1;

        var buf = new byte[8192];
        int read;
        while ((read = fs.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                byte b = buf[i];
                lastByte = b;

                if (pendingCr)
                {
                    if (b == 10) // LF
                    {
                        crlfCount++;
                        pendingCr = false;
                        continue;
                    }

                    crOnlyCount++;
                    pendingCr = false;
                }

                if (b == 13) // CR
                {
                    pendingCr = true;
                }
                else if (b == 10) // LF
                {
                    lfOnlyCount++;
                }
            }
        }

        if (pendingCr) crOnlyCount++;

        bool hasFinalNewline = lastByte == 10 || lastByte == 13;

        int typesFound = 0;
        if (crlfCount > 0) typesFound++;
        if (lfOnlyCount > 0) typesFound++;
        if (crOnlyCount > 0) typesFound++;

        DetectedLineEndingKind kind = typesFound switch
        {
            0 => DetectedLineEndingKind.None,
            1 when crlfCount > 0 => DetectedLineEndingKind.CRLF,
            1 when lfOnlyCount > 0 => DetectedLineEndingKind.LF,
            1 => DetectedLineEndingKind.CR,
            _ => DetectedLineEndingKind.Mixed
        };

        return (kind, hasFinalNewline);
    }

    internal static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
            var pathUri = new Uri(Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
}

