using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Binds the runtime-free local Get-Help view from the same stable metadata used by
/// generated binary-module help. Wider help discovery and formatting stay hosted.
/// </summary>
internal static class PowerShellCommentHelpSemanticBinder
{
    internal static bool IsCommand(CommandAst command)
        => command.GetCommandName()?.Equals("Get-Help", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool TryGetTargetName(CommandAst command, out string targetName)
    {
        targetName = string.Empty;
        if (!IsCommand(command) || command.InvocationOperator != TokenKind.Unknown ||
            command.Redirections.Count != 0)
            return false;
        StringConstantExpressionAst? target = null;
        if (command.CommandElements.Count == 2 && command.CommandElements[1] is StringConstantExpressionAst positional)
            target = positional;
        else if (command.CommandElements.Count == 2 &&
                 command.CommandElements[1] is CommandParameterAst { Argument: StringConstantExpressionAst inline } parameter &&
                 parameter.ParameterName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            target = inline;
        else if (command.CommandElements.Count == 3 &&
                 command.CommandElements[1] is CommandParameterAst named &&
                 named.ParameterName.Equals("Name", StringComparison.OrdinalIgnoreCase) &&
                 command.CommandElements[2] is StringConstantExpressionAst separate)
            target = separate;
        if (target is null || string.IsNullOrWhiteSpace(target.Value)) return false;
        targetName = target.Value;
        return true;
    }

    internal static bool TryInferType(
        Ast syntax,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        out PowerShellTypeFact type)
    {
        type = PowerShellTypeFact.Unknown;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding) ||
            syntax is not CommandAst command || !TryGetTargetName(command, out var targetName) ||
            !functions.TryGetValue(targetName, out var target) || target.Help is null)
            return false;
        type = CreateTypeFact();
        return true;
    }

    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        CommandAst command,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, command.Extent);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding))
            return Reject(diagnostics, "PSB2930", "The bounded local Get-Help metadata view is available only to runtime-free typed executables.", span);
        if (!TryGetTargetName(command, out var targetName))
            return Reject(diagnostics, "PSB2930", "Runtime-free Get-Help requires one unredirected, statically named local function using positional binding or canonical -Name, without other options.", span);
        if (!functions.TryGetValue(targetName, out var target))
            return Reject(diagnostics, "PSB2931", $"Runtime-free Get-Help target '{targetName}' is not one unambiguous compiled local function.", span);
        if (target.Help is null)
            return Reject(diagnostics, "PSB2932", $"Runtime-free Get-Help target '{targetName}' has no bound comment-based help metadata.", span);

        var entries = new[]
        {
            Entry(span, "Name", target.Symbol.Name),
            Entry(span, "Synopsis", target.Help.Synopsis)
        };
        return new PowerShellBoundDictionaryExpression(
            span,
            CreateTypeFact(),
            PowerShellBoundDictionaryKind.StringDictionary,
            entries);
    }

    private static PowerShellTypeFact CreateTypeFact()
    {
        var stringType = new PowerShellTypeFact(
            typeof(string),
            PowerShellTypeFactProvenance.Literal,
            "The bounded local Get-Help property is embedded from canonical comment metadata.");
        return new PowerShellTypeFact(
            typeof(Dictionary<string, string>),
            PowerShellTypeFactProvenance.CommandContract,
            "The runtime-free Get-Help view exposes immutable Name and Synopsis string properties.",
            new Dictionary<string, PowerShellTypeFact>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = stringType,
                ["Synopsis"] = stringType
            },
            PowerShellDictionaryValueKind.HelpMetadata);
    }

    private static PowerShellBoundDictionaryEntry Entry(SourceSpan span, string name, string value)
        => new(
            new PowerShellBoundLiteralExpression(span, name, StringType("Metadata property name."), PowerShellValueState.Known),
            new PowerShellBoundLiteralExpression(span, value, StringType("Canonical comment metadata value."), PowerShellValueState.Known));

    private static PowerShellTypeFact StringType(string explanation)
        => new(typeof(string), PowerShellTypeFactProvenance.Literal, explanation);

    private static PowerShellBoundExpression? Reject(
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string code,
        string message,
        SourceSpan span)
    {
        diagnostics.Add(new PowerShellSemanticDiagnostic(code, message, span));
        return null;
    }
}
