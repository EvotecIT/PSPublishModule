using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static void AppendPublicMethodDocumentation(StringBuilder builder, PowerShellLoweredFunction function)
    {
        var summary = string.IsNullOrWhiteSpace(function.Help?.Synopsis)
            ? $"Invokes the compiled function {function.Symbol.Name}."
            : function.Help!.Synopsis.Trim();
        builder.Append("    /// <summary>")
            .Append(XmlDocumentationText(summary))
            .AppendLine("</summary>");
        foreach (var parameter in function.Parameters.Where(_ => function.NativeFunctionBinding is null))
        {
            var description = function.Help?.Parameters.TryGetValue(parameter.Symbol.Name, out var authored) == true &&
                              !string.IsNullOrWhiteSpace(authored)
                ? authored.Trim()
                : $"Value supplied to the source parameter {parameter.Symbol.Name}.";
            builder.Append("    /// <param name=\"")
                .Append(XmlDocumentationParameterName(parameter.Symbol.Name))
                .Append("\">")
                .Append(XmlDocumentationText(description))
                .AppendLine("</param>");
        }
        foreach (var parameter in CompilerParameters(function))
            builder.Append("    /// <param name=\"")
                .Append(parameter.Name)
                .Append("\">")
                .Append(parameter.Description)
                .AppendLine("</param>");
        if (!function.ReturnType.Equals(typeof(void)))
            builder.AppendLine("    /// <returns>The value produced by the compiled function.</returns>");
    }

    private static IEnumerable<(string Name, string Description)> CompilerParameters(PowerShellLoweredFunction function)
    {
        if (function.NativeFunctionBinding is not null)
            yield return ("__nativeFunction", "PowerShell-hosted invocation context used by this native-bound function.");
        if (function.RequiresPowerShellStatementErrors)
            yield return ("__statementErrors", "Statement-error context used to preserve PowerShell exception semantics.");
        if (function.RequiresPowerShellStopping)
            yield return ("__checkLoopInterrupts", "Callback that throws when the caller requests pipeline interruption.");
        if (function.RequiresPowerShellStreams)
        {
            yield return ("__writeOutput", "Receives success-stream output values.");
            yield return ("__writeVerbose", "Receives verbose-stream messages.");
            yield return ("__writeDebug", "Receives debug-stream messages.");
            yield return ("__writeWarning", "Receives warning-stream messages.");
            yield return ("__writeInformation", "Receives information-stream messages.");
            yield return ("__writeHost", "Receives host-stream messages.");
            yield return ("__writeError", "Receives non-terminating error-stream messages.");
        }
        if (function.RequiresProviderCancellation)
            yield return ("__providerCancellationToken", "Cooperative cancellation token passed to compiled command providers.");
        if (function.RequiresPowerShellCommandRegions && function.NativeFunctionBinding is null)
        {
            yield return ("__invokePowerShellRegion", "Invokes a hosted PowerShell command region without capturing success output.");
            yield return ("__invokePowerShellCapture", "Invokes a hosted PowerShell command region and captures its success output.");
        }
        if (function.RequiresPowerShellRuntimeState)
        {
            yield return ("__shouldProcessTarget", "Evaluates ShouldProcess for a target-only request.");
            yield return ("__shouldProcessAction", "Evaluates ShouldProcess for an action and target request.");
            yield return ("__psVersion", "PowerShell version value exposed to the compiled function.");
            yield return ("__whatIfPreference", "WhatIf preference value exposed to the compiled function.");
            yield return ("__runtimeState", "Read-only PowerShell runtime state exposed to the compiled function.");
        }
        if (function.RequiresPowerShellModuleStateRead)
            yield return ("__readPowerShellModuleVariable", "Reads a PowerShell-hosted module variable.");
        if (function.RequiresPowerShellModuleStateWrite)
            yield return ("__writePowerShellModuleVariable", "Writes a PowerShell-hosted module variable.");
        var requiresBoundParameters = function.NativeFunctionBinding is null &&
                                      (function.RequiresPowerShellBoundParameters || function.Parameters.Any(parameter =>
                                          PowerShellParameterValidationPolicy.RequiresBoundParameterSet(
                                              parameter.Contract,
                                              PowerShellCompilationCapabilities.TypedLibrary)));
        if (requiresBoundParameters)
            yield return ("__boundParameters", "Names of parameters explicitly supplied by the caller.");
    }

    private static string XmlDocumentationText(string value)
        => (System.Security.SecurityElement.Escape(value) ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ');

    private static string XmlDocumentationParameterName(string value)
    {
        var identifier = PowerShellCSharpSymbolRenderer.Identifier(value);
        return identifier.Length > 0 && identifier[0] == '@' ? identifier.Substring(1) : identifier;
    }

    private string QuotePortableSourcePath(string? sourcePath)
    {
        var value = sourcePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return PowerShellCSharpLiteral.QuoteString(string.Empty);
        if (_targetCapabilities != PowerShellCompilationCapabilities.TypedLibrary)
            return PowerShellCSharpLiteral.QuoteString(value);
        var normalized = value.Replace('\\', '/');
        if (!Path.IsPathRooted(value)) return PowerShellCSharpLiteral.QuoteString(normalized);
        var document = _sourceFunction?.Symbol.DocumentId;
        var portable = string.IsNullOrWhiteSpace(document)
            ? Path.GetFileName(value)
            : document + "/" + Path.GetFileName(value);
        return PowerShellCSharpLiteral.QuoteString(portable);
    }
}
