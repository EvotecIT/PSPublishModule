using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Edits PowerShell data files (PSD1) safely using the PowerShell AST, preserving file layout.
/// Only the targeted value text is replaced; comments and other content remain untouched.
/// </summary>
public static partial class ManifestEditor
{
    private static readonly string NewLine = Environment.NewLine;

    private static void WriteManifest(string filePath, string originalContent, string updatedContent)
    {
        var newLine = DetectNewLine(originalContent);
        var normalized = updatedContent
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", newLine);
        File.WriteAllText(filePath, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string DetectNewLine(string content)
    {
        var crlf = 0;
        var lf = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != '\n') continue;
            if (index > 0 && content[index - 1] == '\r') crlf++;
            else lf++;
        }

        if (crlf > lf) return "\r\n";
        if (lf > 0) return "\n";
        return Environment.NewLine;
    }

}
