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
    private const string DeferredPayloadStartMarker = "$PowerForgeMergedScriptPayloadBase64 = @'";
    private const string DeferredPayloadEndMarker = "'@";

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
        ModuleMergeComposer.TryResolveMergedSourceMarkers(scriptPayload, out var sourceStartMarker, out var sourceEndMarker);
        var deferredScriptPayload = BuildDeferredScriptPayload(scriptPreamble, scriptPayload, sourceStartMarker, sourceEndMarker);
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

    private static string BuildDeferredScriptPayload(
        string scriptPreamble,
        string scriptPayload,
        string sourceStartMarker,
        string sourceEndMarker)
    {
        if (string.IsNullOrWhiteSpace(scriptPayload))
            return string.Empty;

        var deferredContent = string.IsNullOrWhiteSpace(scriptPreamble)
            ? scriptPayload
            : scriptPreamble.TrimEnd() + Environment.NewLine + Environment.NewLine + scriptPayload.TrimStart();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(deferredContent));
        var builder = new StringBuilder(encoded.Length + 512);
        builder.AppendLine(DeferredPayloadStartMarker);
        AppendWrappedBase64(builder, encoded);
        builder.AppendLine(DeferredPayloadEndMarker);
        builder.AppendLine("$PowerForgeMergedScriptPayload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($PowerForgeMergedScriptPayloadBase64))");
        builder.AppendLine("$PowerForgeMergedScriptAst = [System.Management.Automation.Language.Parser]::ParseInput($PowerForgeMergedScriptPayload, [ref] $null, [ref] $null)");
        builder.AppendLine("$PowerForgeMergedScriptPathReferences = @($PowerForgeMergedScriptAst.FindAll({");
        builder.AppendLine("    param([System.Management.Automation.Language.Ast] $Ast)");
        builder.AppendLine("    if ($Ast -is [System.Management.Automation.Language.VariableExpressionAst]) {");
        builder.AppendLine("        return $Ast.VariablePath.UserPath -iin @('PSScriptRoot', 'PSCommandPath')");
        builder.AppendLine("    }");
        builder.AppendLine("    if ($Ast -isnot [System.Management.Automation.Language.MemberExpressionAst] -or $Ast.Static) { return $false }");
        builder.AppendLine("    if ($Ast.Member.Value -ine 'Path' -or $Ast.Expression -isnot [System.Management.Automation.Language.MemberExpressionAst]) { return $false }");
        builder.AppendLine("    $PowerForgeMergedScriptMyCommandReference = $Ast.Expression");
        builder.AppendLine("    $PowerForgeMergedScriptMyCommandReference.Member.Value -ieq 'MyCommand' -and");
        builder.AppendLine("        $PowerForgeMergedScriptMyCommandReference.Expression -is [System.Management.Automation.Language.VariableExpressionAst] -and");
        builder.AppendLine("        $PowerForgeMergedScriptMyCommandReference.Expression.VariablePath.UserPath -ieq 'MyInvocation'");
        builder.AppendLine("}, $true))");
        builder.AppendLine("if ($PowerForgeMergedScriptPathReferences.Count -gt 0) {");
        builder.AppendLine("    $PowerForgeMergedScriptBuilder = [Text.StringBuilder]::new($PowerForgeMergedScriptPayload)");
        builder.AppendLine("    foreach ($PowerForgeMergedScriptPathReference in @($PowerForgeMergedScriptPathReferences | Sort-Object { $_.Extent.StartOffset } -Descending)) {");
        builder.AppendLine("        $PowerForgeMergedScriptPathReplacement = if ($PowerForgeMergedScriptPathReference -is [System.Management.Automation.Language.VariableExpressionAst] -and $PowerForgeMergedScriptPathReference.VariablePath.UserPath -ieq 'PSScriptRoot') {");
        builder.AppendLine("            if ($PowerForgeMergedScriptPathReference.Extent.Text.StartsWith('${', [StringComparison]::Ordinal)) { '${PowerForgeModuleRoot}' } else { '$PowerForgeModuleRoot' }");
        builder.AppendLine("        } elseif ($PowerForgeMergedScriptPathReference -is [System.Management.Automation.Language.VariableExpressionAst] -and $PowerForgeMergedScriptPathReference.Extent.Text.StartsWith('${', [StringComparison]::Ordinal)) {");
        builder.AppendLine("            '${PowerForgeModulePath}'");
        builder.AppendLine("        } else {");
        builder.AppendLine("            '$PowerForgeModulePath'");
        builder.AppendLine("        }");
        builder.AppendLine("        $null = $PowerForgeMergedScriptBuilder.Remove($PowerForgeMergedScriptPathReference.Extent.StartOffset, $PowerForgeMergedScriptPathReference.Extent.EndOffset - $PowerForgeMergedScriptPathReference.Extent.StartOffset)");
        builder.AppendLine("        $null = $PowerForgeMergedScriptBuilder.Insert($PowerForgeMergedScriptPathReference.Extent.StartOffset, $PowerForgeMergedScriptPathReplacement)");
        builder.AppendLine("    }");
        builder.AppendLine("    $PowerForgeMergedScriptPayload = $PowerForgeMergedScriptBuilder.ToString()");
        builder.AppendLine("}");
        builder.AppendLine("$PowerForgeMergedScriptSourceStartMarker = '" + EscapePsSingleQuoted(sourceStartMarker) + "'");
        builder.AppendLine("$PowerForgeMergedScriptSourceEndMarker = '" + EscapePsSingleQuoted(sourceEndMarker) + "'");
        builder.AppendLine("$PowerForgeMergedScriptSegments = [Collections.Generic.List[string]]::new()");
        builder.AppendLine("$PowerForgeMergedScriptFirstSource = $PowerForgeMergedScriptPayload.IndexOf($PowerForgeMergedScriptSourceStartMarker, [StringComparison]::Ordinal)");
        builder.AppendLine("if ($PowerForgeMergedScriptFirstSource -lt 0) {");
        builder.AppendLine("    $PowerForgeMergedScriptSegments.Add($PowerForgeMergedScriptPayload)");
        builder.AppendLine("} else {");
        builder.AppendLine("    $PowerForgeMergedScriptPreamble = $PowerForgeMergedScriptPayload.Substring(0, $PowerForgeMergedScriptFirstSource).Trim()");
        builder.AppendLine("    $PowerForgeMergedScriptCursor = $PowerForgeMergedScriptFirstSource");
        builder.AppendLine("    while ($PowerForgeMergedScriptCursor -lt $PowerForgeMergedScriptPayload.Length) {");
        builder.AppendLine("        $PowerForgeMergedScriptSourceStart = $PowerForgeMergedScriptPayload.IndexOf($PowerForgeMergedScriptSourceStartMarker, $PowerForgeMergedScriptCursor, [StringComparison]::Ordinal)");
        builder.AppendLine("        if ($PowerForgeMergedScriptSourceStart -lt 0) { break }");
        builder.AppendLine("        $PowerForgeMergedScriptContentStart = $PowerForgeMergedScriptSourceStart + $PowerForgeMergedScriptSourceStartMarker.Length");
        builder.AppendLine("        $PowerForgeMergedScriptSourceEnd = $PowerForgeMergedScriptPayload.IndexOf($PowerForgeMergedScriptSourceEndMarker, $PowerForgeMergedScriptContentStart, [StringComparison]::Ordinal)");
        builder.AppendLine("        if ($PowerForgeMergedScriptSourceEnd -lt 0) { throw 'Merged script source boundary is incomplete.' }");
        builder.AppendLine("        $PowerForgeMergedScriptSegment = $PowerForgeMergedScriptPayload.Substring($PowerForgeMergedScriptContentStart, $PowerForgeMergedScriptSourceEnd - $PowerForgeMergedScriptContentStart).Trim()");
        builder.AppendLine("        if (-not [string]::IsNullOrWhiteSpace($PowerForgeMergedScriptSegment)) {");
        builder.AppendLine("            if (-not [string]::IsNullOrWhiteSpace($PowerForgeMergedScriptPreamble)) {");
        builder.AppendLine("                $PowerForgeMergedScriptSegment = $PowerForgeMergedScriptPreamble + [Environment]::NewLine + [Environment]::NewLine + $PowerForgeMergedScriptSegment");
        builder.AppendLine("            }");
        builder.AppendLine("            $PowerForgeMergedScriptSegments.Add($PowerForgeMergedScriptSegment)");
        builder.AppendLine("        }");
        builder.AppendLine("        $PowerForgeMergedScriptCursor = $PowerForgeMergedScriptSourceEnd + $PowerForgeMergedScriptSourceEndMarker.Length");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine("try {");
        builder.AppendLine("    foreach ($PowerForgeMergedScriptSegment in $PowerForgeMergedScriptSegments) {");
        builder.AppendLine("        . ([scriptblock]::Create($PowerForgeMergedScriptSegment))");
        builder.AppendLine("    }");
        builder.AppendLine("} finally {");
        builder.AppendLine("    $PowerForgeMergedScriptVariablesToRemove = @('PowerForgeMergedScriptAst', 'PowerForgeMergedScriptBuilder', 'PowerForgeMergedScriptContentStart', 'PowerForgeMergedScriptCursor', 'PowerForgeMergedScriptFirstSource', 'PowerForgeMergedScriptMyCommandReference', 'PowerForgeMergedScriptPathReference', 'PowerForgeMergedScriptPathReferences', 'PowerForgeMergedScriptPathReplacement', 'PowerForgeMergedScriptPayload', 'PowerForgeMergedScriptPayloadBase64', 'PowerForgeMergedScriptPreamble', 'PowerForgeMergedScriptSegment', 'PowerForgeMergedScriptSegments', 'PowerForgeMergedScriptSourceEnd', 'PowerForgeMergedScriptSourceEndMarker', 'PowerForgeMergedScriptSourceStart', 'PowerForgeMergedScriptSourceStartMarker')");
        builder.AppendLine("    foreach ($PowerForgeMergedScriptVariableToRemove in $PowerForgeMergedScriptVariablesToRemove) {");
        builder.AppendLine("        if ($null -ne $ExecutionContext.SessionState.PSVariable.Get($PowerForgeMergedScriptVariableToRemove)) {");
        builder.AppendLine("            Remove-Variable -Name $PowerForgeMergedScriptVariableToRemove -Force");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    Remove-Variable -Name PowerForgeMergedScriptVariableToRemove, PowerForgeMergedScriptVariablesToRemove -Force");
        builder.Append('}');
        return builder.ToString();
    }

    internal static string RewriteDeferredScriptPayload(string moduleContent, Func<string, string> rewrite)
        => RewriteDeferredScriptPayload(moduleContent, rewrite, rewriteOuterContent: null);

    internal static string RewriteDeferredScriptPayload(
        string moduleContent,
        Func<string, string> rewritePayload,
        Func<string, string>? rewriteOuterContent)
    {
        if (string.IsNullOrWhiteSpace(moduleContent) || rewritePayload is null)
            return moduleContent ?? string.Empty;

        var markerStart = moduleContent.IndexOf(DeferredPayloadStartMarker, StringComparison.Ordinal);
        if (markerStart < 0)
            return rewriteOuterContent?.Invoke(moduleContent) ?? moduleContent;

        var payloadStart = markerStart + DeferredPayloadStartMarker.Length;
        var payloadEnd = moduleContent.IndexOf(DeferredPayloadEndMarker, payloadStart, StringComparison.Ordinal);
        if (payloadEnd <= payloadStart)
            return rewriteOuterContent?.Invoke(moduleContent) ?? moduleContent;

        var encoded = moduleContent.Substring(payloadStart, payloadEnd - payloadStart);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var rewritten = rewritePayload(decoded) ?? string.Empty;
        var prefix = moduleContent.Substring(0, payloadStart);
        var suffix = moduleContent.Substring(payloadEnd);
        var rewrittenPrefix = rewriteOuterContent?.Invoke(prefix) ?? prefix;
        var rewrittenSuffix = rewriteOuterContent?.Invoke(suffix) ?? suffix;
        if (string.Equals(decoded, rewritten, StringComparison.Ordinal) &&
            string.Equals(prefix, rewrittenPrefix, StringComparison.Ordinal) &&
            string.Equals(suffix, rewrittenSuffix, StringComparison.Ordinal))
        {
            return moduleContent;
        }

        var updatedEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(rewritten));
        var wrapped = new StringBuilder(updatedEncoded.Length + 64);
        AppendWrappedBase64(wrapped, updatedEncoded);
        return rewrittenPrefix +
               Environment.NewLine +
               wrapped.ToString().TrimEnd('\r', '\n') +
               Environment.NewLine +
               rewrittenSuffix;
    }

    private static void AppendWrappedBase64(StringBuilder builder, string encoded)
    {
        for (var offset = 0; offset < encoded.Length; offset += 120)
        {
            var length = Math.Min(120, encoded.Length - offset);
            builder.AppendLine(encoded.Substring(offset, length));
        }
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
