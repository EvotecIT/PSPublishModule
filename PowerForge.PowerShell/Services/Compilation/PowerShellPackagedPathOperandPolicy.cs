using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Selects only the provider path operand; other command values cannot retrieve a variable.</summary>
internal static class PowerShellPackagedPathOperandPolicy
{
    internal static ExpressionAst? Select(CommandAst command)
    {
        var name = command.GetCommandName();
        if (name is null) return null;
        var resolution = PowerShellCommandSemanticRegistry.Default.Resolve(name);
        var provider = resolution.Contract;
        if (provider is null || !provider.ProviderId.StartsWith("powerforge.command.path-read.", StringComparison.Ordinal)) return null;
        if (resolution.Status != PowerShellCommandResolutionStatus.Resolved)
            throw Refusal(command, "ambiguous provider identity");
        ExpressionAst? named = null;
        ExpressionAst? positional = null;
        var elements = command.CommandElements;
        for (var index = 1; index < elements.Count; index++)
        {
            var element = elements[index];
            if (element is VariableExpressionAst { Splatted: true }) throw Refusal(command, "splatted path binding");
            if (element is CommandParameterAst parameter)
            {
                var contract = provider.Parameters.SingleOrDefault(candidate =>
                    candidate.Name.Equals(parameter.ParameterName, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Aliases.Any(alias => alias.Equals(parameter.ParameterName, StringComparison.OrdinalIgnoreCase)));
                if (contract is null) throw Refusal(command, "unreviewed or abbreviated parameter binding");
                if (PowerShellCommandSemanticRegistry.IsPathReadSwitch(provider.ProviderId, contract.Name)) continue;
                var argument = parameter.Argument;
                if (argument is null)
                {
                    if (++index >= elements.Count || elements[index] is not ExpressionAst next)
                        throw Refusal(command, "missing named parameter operand");
                    argument = next;
                }
                if (argument is VariableExpressionAst { Splatted: true }) throw Refusal(command, "splatted operand");
                if (contract.Name is "Path" or "LiteralPath")
                {
                    if (named is not null) throw Refusal(command, "multiple path parameters");
                    named = argument;
                }
                continue;
            }
            if (element is not ExpressionAst expression || positional is not null)
                throw Refusal(command, "ambiguous positional path binding");
            positional = expression;
        }
        if (named is not null && positional is not null) throw Refusal(command, "named and positional path binding");
        if (named is not null || positional is not null) return named ?? positional;
        if (IsImplicitCurrentLocationRead(command))
        {
            ValidateImplicitCurrentLocationShape(command);
            return null;
        }
        throw Refusal(command, "implicit or pipeline-provided path binding");
    }

    internal static bool IsImplicitCurrentLocationRead(CommandAst command)
        => command.CommandElements.Count == 1 && command.GetCommandName() is { } name &&
           PowerShellCommandSemanticRegistry.Default.Resolve(name).Contract is { } contract &&
           PowerShellCommandSemanticRegistry.AllowsImplicitPathReadCurrentLocation(contract.ProviderId);

    private static void ValidateImplicitCurrentLocationShape(CommandAst command)
    {
        if (command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0 ||
            command.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            PowerShellCommandRegionSemanticBinder.IsBackground(pipeline))
            throw Refusal(command, "implicit current-location reads require a bare standalone pipeline without redirection");
    }

    internal static bool MayRetrieveVariable(ExpressionAst operand, string variableName, ScriptBlockAst root)
    {
        if (!TryGetLiteralPaths(operand, out var paths))
        {
            // Preserve a known unsafe assignment refusal without treating root-derived
            // expressions as proof of provider identity. All other dynamic values are guarded.
            var assignment = operand is VariableExpressionAst variable
                ? root.FindAll(static node => node is AssignmentStatementAst, true).Cast<AssignmentStatementAst>()
                    .Where(candidate => candidate.Extent.StartOffset < operand.Extent.StartOffset &&
                        candidate.Left is VariableExpressionAst target &&
                        target.VariablePath.UserPath.Equals(variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase) &&
                        ReferenceEquals(OwningFunction(candidate), OwningFunction(operand)))
                    .OrderByDescending(static candidate => candidate.Extent.StartOffset).FirstOrDefault()
                : null;
            var assigned = assignment?.Right switch
            {
                CommandExpressionAst expression => expression.Expression,
                PipelineAst pipeline => pipeline.GetPureExpression(),
                _ => null
            };
            if (assigned is null || !TryGetLiteralPaths(assigned, out paths)) return false;
        }
        return paths.Any(value => TryGetVariablePattern(value, out var pattern) &&
            (string.IsNullOrEmpty(pattern) || new WildcardPattern(pattern,
                WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant).IsMatch(variableName)));
    }

    internal static bool RequiresRuntimeGuard(ExpressionAst operand)
    {
        if (!TryGetLiteralPaths(operand, out var paths)) return true;
        return paths.Any(path =>
        {
            // Provider-qualified literals identify their provider explicitly; static
            // Variable Host/MyInvocation patterns are rejected by the host policy.
            if (path.IndexOf("::", StringComparison.Ordinal) >= 0) return false;
            var colon = path.IndexOf(':');
            if (colon < 0) return true; // Relative paths depend on the current provider.
            var name = Unscope(path.Substring(colon + 1).TrimStart('\\', '/'));
            // Keep statically named non-host provider data under its existing contract.
            // A drive alias with a reserved/wildcard name still needs actual admission.
            return name.Length == 0 || new WildcardPattern(name, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant).IsMatch("Host") ||
                new WildcardPattern(name, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant).IsMatch("MyInvocation");
        });
    }

    internal static bool RequiresGuardIn(Ast syntax)
        => syntax.FindAll(static node => node is CommandAst, true).Cast<CommandAst>()
            .Any(command => Select(command) is { } operand ? RequiresRuntimeGuard(operand) : IsImplicitCurrentLocationRead(command));

    internal static void ValidateGuardableReads(ScriptBlockAst root)
    {
        foreach (var command in root.FindAll(static node => node is CommandAst, true).Cast<CommandAst>())
        {
            var operand = Select(command);
            if (operand is null ? !IsImplicitCurrentLocationRead(command) : !RequiresRuntimeGuard(operand)) continue;
            for (var parent = command.Parent; parent is not null && parent is not FunctionDefinitionAst &&
                     !ReferenceEquals(parent, root); parent = parent.Parent)
                if (parent is CommandAst or ScriptBlockExpressionAst or ExpandableStringExpressionAst)
                    throw Refusal(command, "nested command-expression or callback path reads are unsupported");
        }
    }

    internal static int[] SelectNativeRegionOperands(Ast region)
    {
        var result = new List<int>();
        foreach (var command in region.FindAll(static node => node is CommandAst, true).Cast<CommandAst>())
        {
            var operand = Select(command);
            var implicitLocation = operand is null && IsImplicitCurrentLocationRead(command);
            if (!implicitLocation && (operand is null || !RequiresRuntimeGuard(operand))) continue;
            if (command.Parent is not PipelineAst pipeline ||
                !ReferenceEquals(pipeline, region) && !ReferenceEquals(command, region))
                throw Refusal(command, "dynamic provider reads inside a nested command expression or callback are not guardable by this native pipeline");
            // A zero-width command-start marker is a current-location admission, not
            // an operand replacement. Other pairs remain exact authored operand extents.
            result.Add(implicitLocation ? command.Extent.StartOffset : operand!.Extent.StartOffset);
            result.Add(implicitLocation ? command.Extent.StartOffset : operand!.Extent.EndOffset);
        }
        return result.ToArray();
    }

    private static bool TryGetLiteralPaths(ExpressionAst operand, out string[] paths)
    {
        if (operand is StringConstantExpressionAst literal) { paths = new[] { literal.Value }; return true; }
        if (operand is ArrayLiteralAst array)
        {
            var values = new List<string>();
            foreach (var element in array.Elements)
            {
                if (!TryGetLiteralPaths(element, out var selected)) { paths = Array.Empty<string>(); return false; }
                values.AddRange(selected);
            }
            paths = values.ToArray(); return true;
        }
        paths = Array.Empty<string>(); return false;
    }

    private static bool TryGetVariablePattern(string value, out string pattern)
    {
        var separator = value.IndexOf("::", StringComparison.Ordinal);
        if (separator >= 0)
        {
            var provider = value.Substring(0, separator).Split('\\').Last();
            if (provider.Equals("Variable", StringComparison.OrdinalIgnoreCase))
            { pattern = Unscope(value.Substring(separator + 2).TrimStart('\\', '/')); return true; }
        }
        else if (value.StartsWith("Variable:", StringComparison.OrdinalIgnoreCase))
        { pattern = Unscope(value.Substring("Variable:".Length).TrimStart('\\', '/')); return true; }
        pattern = string.Empty; return false;
    }

    private static string Unscope(string pattern)
    {
        var colon = pattern.IndexOf(':');
        return colon >= 0 && new[] { "global", "script", "local", "private" }
            .Contains(pattern.Substring(0, colon), StringComparer.OrdinalIgnoreCase) ? pattern.Substring(colon + 1) : pattern;
    }

    private static FunctionDefinitionAst? OwningFunction(Ast syntax)
    {
        for (var parent = syntax.Parent; parent is not null; parent = parent.Parent)
            if (parent is FunctionDefinitionAst function) return function;
        return null;
    }

    private static InvalidOperationException Refusal(CommandAst command, string reason)
        => new($"Packaged executable path read '{command.Extent.Text}' is unsupported: {reason}. Use one explicit Path/LiteralPath operand in a direct pipeline.");
}
