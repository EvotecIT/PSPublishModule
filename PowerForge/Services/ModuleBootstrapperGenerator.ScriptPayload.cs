using System;
using System.IO;
using System.Text;

namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    private const string ScriptPreambleStartMarker = "# PowerForge script preamble begin";
    private const string ScriptPreambleEndMarker = "# PowerForge script preamble end";
    private const string ScriptPayloadStartMarker = "# PowerForge script payload begin";
    private const string ScriptPayloadEndMarker = "# PowerForge script payload end";

    /// <summary>
    /// Replaces the folder-based script loader in a generated binary-module bootstrapper with the
    /// merged script payload so release packages no longer need loose source folders.
    /// </summary>
    /// <param name="psm1Path">Path to the generated module bootstrapper.</param>
    /// <param name="mergedScriptContent">Merged Classes, Enums, Private, and Public script content.</param>
    internal static void InlineMergedScriptPayload(string psm1Path, string mergedScriptContent)
    {
        if (string.IsNullOrWhiteSpace(psm1Path))
            throw new ArgumentException("Bootstrapper PSM1 path is required.", nameof(psm1Path));
        if (!File.Exists(psm1Path))
            throw new FileNotFoundException("Generated module bootstrapper was not found.", psm1Path);

        var scriptPreamble = ModuleMergeComposer.ExtractMergedScriptPreamble(mergedScriptContent, out var scriptPayload);
        var authoritativeExportBlock = ModuleMergeComposer.ExtractTrailingExportBlock(scriptPayload, out scriptPayload);
        var deferredScriptPayload = BuildDeferredScriptPayload(scriptPayload);
        var bootstrapper = File.ReadAllText(psm1Path);
        bootstrapper = ReplaceMarkedSection(
            bootstrapper,
            ScriptPreambleStartMarker,
            ScriptPreambleEndMarker,
            scriptPreamble,
            psm1Path);
        var inlinedBootstrapper = ReplaceMarkedSection(
            bootstrapper,
            ScriptPayloadStartMarker,
            ScriptPayloadEndMarker,
            deferredScriptPayload,
            psm1Path);

        var generatedExportBlock = ModuleMergeComposer.ExtractTrailingExportBlock(inlinedBootstrapper, out var bootstrapperWithoutExportBlock);
        if (string.IsNullOrWhiteSpace(generatedExportBlock))
        {
            throw new InvalidOperationException(
                $"Cannot inline merged scripts because '{Path.GetFileName(psm1Path)}' does not contain a generated export block.");
        }

        if (!string.IsNullOrWhiteSpace(authoritativeExportBlock))
        {
            inlinedBootstrapper = bootstrapperWithoutExportBlock.TrimEnd() +
                                  Environment.NewLine + Environment.NewLine +
                                  authoritativeExportBlock.TrimEnd() +
                                  Environment.NewLine;
        }

        WritePowerShellFile(psm1Path, inlinedBootstrapper);
    }

    private static string BuildDeferredScriptPayload(string scriptPayload)
    {
        if (string.IsNullOrWhiteSpace(scriptPayload))
            return string.Empty;

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(scriptPayload));
        var builder = new StringBuilder(encoded.Length + 512);
        builder.AppendLine("$PowerForgeMergedScriptPayloadBase64 = @'");
        for (var offset = 0; offset < encoded.Length; offset += 120)
        {
            var length = Math.Min(120, encoded.Length - offset);
            builder.AppendLine(encoded.Substring(offset, length));
        }
        builder.AppendLine("'@");
        builder.AppendLine("$PowerForgeMergedScriptPayload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($PowerForgeMergedScriptPayloadBase64))");
        builder.AppendLine("try {");
        builder.AppendLine("    . ([scriptblock]::Create($PowerForgeMergedScriptPayload))");
        builder.AppendLine("} finally {");
        builder.AppendLine("    Remove-Variable -Name PowerForgeMergedScriptPayload, PowerForgeMergedScriptPayloadBase64 -ErrorAction SilentlyContinue");
        builder.Append('}');
        return builder.ToString();
    }

    private static string ReplaceMarkedSection(
        string bootstrapper,
        string startMarker,
        string endMarker,
        string content,
        string psm1Path)
    {
        var contentStart = bootstrapper.IndexOf(startMarker, StringComparison.Ordinal);
        var contentEnd = bootstrapper.IndexOf(endMarker, StringComparison.Ordinal);
        if (contentStart < 0 || contentEnd <= contentStart)
        {
            throw new InvalidOperationException(
                $"Cannot inline merged scripts because '{Path.GetFileName(psm1Path)}' is not a compatible generated PowerForge bootstrapper.");
        }

        contentStart += startMarker.Length;
        var normalizedContent = (content ?? string.Empty).Trim('\r', '\n');
        return bootstrapper.Substring(0, contentStart) +
               Environment.NewLine +
               normalizedContent +
               Environment.NewLine +
               bootstrapper.Substring(contentEnd);
    }
}
