using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Text;
using PowerForge;

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
        var processed = new CommentRemovalService().Process(new CommentRemovalRequest
        {
            Content = content,
            RemoveEmptyLines = RemoveEmptyLines.IsPresent,
            RemoveAllEmptyLines = RemoveAllEmptyLines.IsPresent,
            RemoveCommentsInParamBlock = RemoveCommentsInParamBlock.IsPresent,
            RemoveCommentsBeforeParamBlock = RemoveCommentsBeforeParamBlock.IsPresent,
            DoNotRemoveSignatureBlock = DoNotRemoveSignatureBlock.IsPresent
        });

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
}
