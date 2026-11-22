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
        var prefixLength = 0;
        var sharedLength = Math.Min(originalContent.Length, updatedContent.Length);
        while (prefixLength < sharedLength && originalContent[prefixLength] == updatedContent[prefixLength])
            prefixLength++;
        if (prefixLength > 0 &&
            ((prefixLength < originalContent.Length && originalContent[prefixLength - 1] == '\r' && originalContent[prefixLength] == '\n') ||
             (prefixLength < updatedContent.Length && updatedContent[prefixLength - 1] == '\r' && updatedContent[prefixLength] == '\n')))
        {
            prefixLength--;
        }

        var suffixLength = 0;
        while (suffixLength < originalContent.Length - prefixLength &&
               suffixLength < updatedContent.Length - prefixLength &&
               originalContent[originalContent.Length - suffixLength - 1] == updatedContent[updatedContent.Length - suffixLength - 1])
        {
            suffixLength++;
        }
        var originalSuffixStart = originalContent.Length - suffixLength;
        var updatedSuffixStart = updatedContent.Length - suffixLength;
        if (suffixLength > 0 &&
            ((originalSuffixStart > 0 && originalContent[originalSuffixStart - 1] == '\r' && originalContent[originalSuffixStart] == '\n') ||
             (updatedSuffixStart > 0 && updatedContent[updatedSuffixStart - 1] == '\r' && updatedContent[updatedSuffixStart] == '\n')))
        {
            suffixLength--;
        }

        var changedLength = updatedContent.Length - prefixLength - suffixLength;
        var changedContent = updatedContent.Substring(prefixLength, changedLength)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", newLine);
        var preserved = originalContent.Substring(0, prefixLength) +
                        changedContent +
                        originalContent.Substring(originalContent.Length - suffixLength);
        File.WriteAllText(filePath, preserved, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
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
