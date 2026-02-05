using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text;
using System.Text.RegularExpressions;

namespace PSPublishModule;

/// <summary>
/// Removes PowerShell comments from a script file or provided content, with optional empty-line normalization.
/// </summary>
/// <remarks>
/// <para>
/// Uses the PowerShell parser (AST) to remove comments safely rather than relying on fragile regex-only approaches.
/// Useful as a preprocessing step when producing merged/packed scripts.
/// </para>
/// </remarks>
/// <example>
/// <summary>Remove comments from a file and write to a new file</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Remove-Comments -SourceFilePath '.\Public\Get-Thing.ps1' -DestinationFilePath '.\Public\Get-Thing.nocomments.ps1'</code>
/// <para>Writes the cleaned content to the destination file.</para>
/// </example>
/// <example>
/// <summary>Remove comments from content and return the processed text</summary>
/// <prefix>PS&gt; </prefix>
/// <code>$clean = Remove-Comments -Content (Get-Content -Raw .\script.ps1)</code>
/// <para>Returns the processed content when no destination file is specified.</para>
/// </example>
[Cmdlet(VerbsCommon.Remove, "Comments", DefaultParameterSetName = ParameterSetFilePath)]
public sealed class RemoveCommentsCommand : PSCmdlet
{
    private const string ParameterSetFilePath = "FilePath";
    private const string ParameterSetContent = "Content";

    /// <summary>File path to the source file.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetFilePath)]
    [Alias("FilePath", "Path", "LiteralPath")]
    public string SourceFilePath { get; set; } = string.Empty;

    /// <summary>Raw file content to process.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetContent)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// File path to the destination file. If not provided, the content is returned.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetContent)]
    [Parameter(ParameterSetName = ParameterSetFilePath)]
    [Alias("Destination", "OutputFile", "OutputFilePath")]
    public string? DestinationFilePath { get; set; }

    /// <summary>Remove all empty lines from the content.</summary>
    [Parameter(ParameterSetName = ParameterSetContent)]
    [Parameter(ParameterSetName = ParameterSetFilePath)]
    public SwitchParameter RemoveAllEmptyLines { get; set; }

    /// <summary>Remove empty lines if more than one empty line is found.</summary>
    [Parameter(ParameterSetName = ParameterSetContent)]
    [Parameter(ParameterSetName = ParameterSetFilePath)]
    public SwitchParameter RemoveEmptyLines { get; set; }

    /// <summary>Remove comments in the param block. By default comments in the param block are not removed.</summary>
    [Parameter(ParameterSetName = ParameterSetContent)]
    [Parameter(ParameterSetName = ParameterSetFilePath)]
    public SwitchParameter RemoveCommentsInParamBlock { get; set; }

    /// <summary>
    /// Remove comments before the param block. By default comments before the param block are not removed.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetContent)]
    [Parameter(ParameterSetName = ParameterSetFilePath)]
    public SwitchParameter RemoveCommentsBeforeParamBlock { get; set; }

    /// <summary>Do not remove a signature block, if present.</summary>
    [Parameter(ParameterSetName = ParameterSetContent)]
    [Parameter(ParameterSetName = ParameterSetFilePath)]
    public SwitchParameter DoNotRemoveSignatureBlock { get; set; }

    /// <summary>Processes the content and either writes to a file or outputs a string.</summary>
    protected override void ProcessRecord()
    {
        var content = ParameterSetName == ParameterSetFilePath
            ? ReadContentFromFilePath(SourceFilePath)
            : (Content ?? string.Empty);

        var processed = RemoveCommentsCore(
            content,
            removeEmptyLines: RemoveEmptyLines.IsPresent,
            removeAllEmptyLines: RemoveAllEmptyLines.IsPresent,
            removeCommentsInParamBlock: RemoveCommentsInParamBlock.IsPresent,
            removeCommentsBeforeParamBlock: RemoveCommentsBeforeParamBlock.IsPresent,
            doNotRemoveSignatureBlock: DoNotRemoveSignatureBlock.IsPresent);

        if (!string.IsNullOrEmpty(DestinationFilePath))
        {
            var dest = SessionState.Path.GetUnresolvedProviderPathFromPSPath(DestinationFilePath);
            File.WriteAllText(dest, processed, ResolveOutputEncoding());
            return;
        }

        WriteObject(processed, enumerateCollection: false);
    }

    private string ReadContentFromFilePath(string psPath)
    {
        if (string.IsNullOrWhiteSpace(psPath))
            return string.Empty;

        var fullPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(psPath);
        return File.ReadAllText(fullPath, Encoding.UTF8);
    }

    private static Encoding ResolveOutputEncoding()
    {
        // Match legacy script behavior (PS 5.1 uses UTF8 which includes BOM; PS 7 uses UTF8BOM explicitly).
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    }

    private static string RemoveCommentsCore(
        string content,
        bool removeEmptyLines,
        bool removeAllEmptyLines,
        bool removeCommentsInParamBlock,
        bool removeCommentsBeforeParamBlock,
        bool doNotRemoveSignatureBlock)
    {
        var text = content ?? string.Empty;

        // Tokenize and remove comment tokens using the same state machine as the legacy PowerShell implementation.
        var ast = Parser.ParseInput(text, out Token[] tokens, out ParseError[] _);
        var scriptAst = ast as ScriptBlockAst;

        var toRemove = new List<Token>();
        bool doNotRemove = false;
        bool doNotRemoveCommentParam = false;
        int countParams = 0;
        bool paramFound = false;
        bool signatureBlock = false;
        var scriptParamOffset = scriptAst?.ParamBlock?.Extent.StartOffset ?? -1;

        // Group tokens by StartLineNumber to mirror the legacy approach (though ordering is unchanged).
        foreach (var lineGroup in tokens.GroupBy(t => t.Extent.StartLineNumber))
        {
            var lineTokens = lineGroup.ToArray();
            for (int i = 0; i < lineTokens.Length; i++)
            {
                var token = lineTokens[i];
                var extentText = token.Extent.Text;

                // Find comments between function and param block and not remove them (default).
                if (string.Equals(extentText, "function", StringComparison.OrdinalIgnoreCase))
                {
                    if (!removeCommentsBeforeParamBlock)
                        doNotRemove = true;
                    continue;
                }

                if (string.Equals(extentText, "param", StringComparison.OrdinalIgnoreCase))
                {
                    paramFound = true;
                    doNotRemove = false;
                }

                if (doNotRemove)
                    continue;

                // Find comments between param block and end of param block (default: do not remove).
                if (string.Equals(extentText, "param", StringComparison.OrdinalIgnoreCase))
                {
                    if (!removeCommentsInParamBlock)
                        doNotRemoveCommentParam = true;
                    continue;
                }

                if (paramFound && (string.Equals(extentText, "(", StringComparison.Ordinal) || string.Equals(extentText, "@(", StringComparison.Ordinal)))
                {
                    countParams += 1;
                }
                else if (paramFound && string.Equals(extentText, ")", StringComparison.Ordinal))
                {
                    countParams -= 1;
                }

                if (paramFound && string.Equals(extentText, ")", StringComparison.Ordinal))
                {
                    if (countParams == 0)
                    {
                        doNotRemoveCommentParam = false;
                        paramFound = false;
                    }
                }

                if (doNotRemoveCommentParam)
                    continue;

                if (token.Kind != TokenKind.Comment)
                    continue;

                if (!removeCommentsBeforeParamBlock && scriptParamOffset >= 0 && token.Extent.EndOffset <= scriptParamOffset)
                    continue;

                if (doNotRemoveSignatureBlock)
                {
                    if (string.Equals(token.Text, "# SIG # Begin signature block", StringComparison.OrdinalIgnoreCase))
                    {
                        signatureBlock = true;
                        continue;
                    }

                    if (signatureBlock)
                    {
                        if (string.Equals(token.Text, "# SIG # End signature block", StringComparison.OrdinalIgnoreCase))
                        {
                            signatureBlock = false;
                        }
                        continue;
                    }
                }

                toRemove.Add(token);
            }
        }

        foreach (var token in toRemove.OrderByDescending(t => t.Extent.StartOffset))
        {
            var startIndex = token.Extent.StartOffset;
            var howManyChars = token.Extent.EndOffset - token.Extent.StartOffset;
            text = text.Remove(startIndex, howManyChars);
        }

        if (removeEmptyLines)
        {
            text = Regex.Replace(text, @"(?m)^\s*$", string.Empty);
            text = Regex.Replace(text, @"(?:\r?\n|\n|\r)", "\r\n");
        }

        if (removeAllEmptyLines)
        {
            text = Regex.Replace(text, @"(?m)^\s*$(\r?\n)?", string.Empty);
        }

        if (!string.IsNullOrEmpty(text))
            text = text.Trim();

        return text;
    }
}
