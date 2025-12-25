using System;
using System.Collections.Generic;
using System.IO;

namespace PSPublishModule;

internal static class ProjectFileAnalysis
{
    internal static string DetectEncodingName(string path)
    {
        // Match the legacy Get-FileEncoding helper: BOM first; fallback to ASCII vs UTF8 scan.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bom = new byte[4];
        _ = fs.Read(bom, 0, 4);

        // Avoid referencing internal framework encoding types; return stable names.
        if (bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xfe && bom[3] == 0xff) return "UTF32";
        if (bom[0] == 0xff && bom[1] == 0xfe && bom[2] == 0x00 && bom[3] == 0x00) return "UTF32";
        if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return "UTF8BOM";
        if (bom[0] == 0xff && bom[1] == 0xfe) return "Unicode";
        if (bom[0] == 0xfe && bom[1] == 0xff) return "BigEndianUnicode";
        if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76) return "UTF7";

        fs.Position = 0;
        var buf = new byte[4096];
        int read;
        while ((read = fs.Read(buf, 0, buf.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buf[i] > 0x7F) return "UTF8";
            }
        }
        return "ASCII";
    }

    internal static (string LineEnding, bool HasFinalNewline) DetectLineEnding(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return ("None", true);

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

            var typesFound = new List<string>(3);
            if (crlfCount > 0) typesFound.Add("CRLF");
            if (lfOnlyCount > 0) typesFound.Add("LF");
            if (crOnlyCount > 0) typesFound.Add("CR");

            var type = typesFound.Count switch
            {
                0 => "None",
                1 => typesFound[0],
                _ => "Mixed"
            };

            return (type, hasFinalNewline);
        }
        catch
        {
            return ("Error", false);
        }
    }

    internal static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(System.IO.Path.GetFullPath(baseDir)));
            var pathUri = new Uri(System.IO.Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', System.IO.Path.DirectorySeparatorChar);
        }
        catch
        {
            return System.IO.Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + System.IO.Path.DirectorySeparatorChar;
}

