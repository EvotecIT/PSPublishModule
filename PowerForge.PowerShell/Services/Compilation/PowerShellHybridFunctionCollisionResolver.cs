using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Keeps PowerShell's function replacement semantics when a hybrid module defines one command name more than once.
/// </summary>
internal static class PowerShellHybridFunctionCollisionResolver
{
    internal static PowerShellTypedCompilationResult RouteNameCollisionsToFallback(
        PowerShellTypedCompilationResult typed,
        string? targetFramework)
    {
        var definitions = new List<(string Path, ScriptBlockAst Root, FunctionDefinitionAst Function)>();
        var sources = new List<(string Path, ScriptBlockAst Root)>();
        foreach (var sourcePath in typed.SourcePaths)
        {
            Token[] tokens;
            ParseError[] errors;
            var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
            if (errors.Length > 0)
                return typed;
            sources.Add((sourcePath, ast));
            definitions.AddRange(ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .Select(function => (sourcePath, ast, function)));
        }

        var duplicateNames = definitions
            .GroupBy(static item => item.Function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var earlyAvailabilityNames = definitions
            .Where(definition => sources.Any(source => source.Root.FindAll(
                    node => IsCommandReferenceCandidate(node) &&
                            (!PowerShellCompilationPathSafety.PathEquals(source.Path, definition.Path) ||
                             node.Extent.StartOffset < definition.Function.Extent.StartOffset) &&
                            IsModuleScope(node, source.Root),
                    searchNestedScriptBlocks: true)
                .Any(node => ReferencesFunction(node, definition.Function.Name))))
            .Select(static definition => definition.Function.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallbackNames = duplicateNames
            .Concat(earlyAvailabilityNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludedMethods = typed.Methods
            .Where(method => fallbackNames.Contains(method.SourceName))
            .Select(method => Path.GetFullPath(string.IsNullOrWhiteSpace(method.SourcePath) ? typed.SourcePath : method.SourcePath) + "\0" + method.SourceName + "\0" + method.SourceLine)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excludedMethods.Count == 0)
            return typed;

        var excludedNames = typed.Methods
            .Where(method => fallbackNames.Contains(method.SourceName))
            .Select(static method => method.SourceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = new PowerShellTypedCompilationTranspiler().TranspileExcluding(
            typed.SourcePaths,
            typed.NamespaceName,
            typed.TypeName,
            targetFramework,
            excludedMethods,
            PowerShellCompilationCapabilities.BinaryModule);
        var diagnostics = excludedNames.Select(name =>
        {
            var definition = definitions
                .Where(item => item.Function.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static item => item.Function.Extent.StartOffset)
                .First();
            var message = duplicateNames.Contains(name)
                ? $"Function '{name}' has multiple retained definitions, so hybrid compilation keeps PowerShell's runtime replacement semantics."
                : $"Function '{name}' is referenced by retained module-scope code before or across a separately loaded declaration boundary, so hybrid compilation preserves PowerShell's command-availability timing.";
            return new PowerShellCompilationDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                message,
                definition.Path,
                definition.Function.Extent.StartLineNumber,
                definition.Function.Extent.StartColumnNumber);
        });
        return new PowerShellTypedCompilationResult(
            filtered.SourcePath,
            filtered.NamespaceName,
            filtered.TypeName,
            filtered.SourceCode,
            filtered.Methods,
            filtered.Diagnostics.Concat(diagnostics)
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray(),
            filtered.SourcePaths);
    }

    private static bool IsCommandReferenceCandidate(Ast node)
        => node is CommandAst ||
           node is InvokeMemberExpressionAst invocation && IsInvokeCommandDiscovery(invocation);

    private static bool ReferencesFunction(Ast node, string functionName)
        => node switch
        {
            CommandAst command => ReferencesFunction(command, functionName),
            InvokeMemberExpressionAst invocation => ReferencesFunction(invocation, functionName),
            _ => false
        };

    private static bool ReferencesFunction(InvokeMemberExpressionAst invocation, string functionName)
    {
        if (!IsInvokeCommandDiscovery(invocation))
            return false;
        if (invocation.Arguments.Count == 0)
            return true;
        return invocation.Arguments[0] is StringConstantExpressionAst name
            ? CommandPatternMatches(name.Value, functionName)
            : true;
    }

    private static bool IsInvokeCommandDiscovery(InvokeMemberExpressionAst invocation)
        => invocation.Member is StringConstantExpressionAst member &&
           (member.Value.Equals("GetCommand", StringComparison.OrdinalIgnoreCase) ||
            member.Value.Equals("GetCommands", StringComparison.OrdinalIgnoreCase)) &&
           invocation.Expression is MemberExpressionAst
           {
               Expression: VariableExpressionAst executionContext,
               Member: StringConstantExpressionAst invokeCommand
           } &&
           executionContext.VariablePath.UserPath.Equals("ExecutionContext", StringComparison.OrdinalIgnoreCase) &&
           invokeCommand.Value.Equals("InvokeCommand", StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesFunction(CommandAst command, string functionName)
    {
        if (command.GetCommandName()?.Equals(functionName, StringComparison.OrdinalIgnoreCase) == true)
            return true;
        var commandName = command.GetCommandName();
        if (commandName is null ||
            !commandName.Equals("Get-Command", StringComparison.OrdinalIgnoreCase) &&
            !commandName.EndsWith("\\Get-Command", StringComparison.OrdinalIgnoreCase) &&
            !commandName.Equals("gcm", StringComparison.OrdinalIgnoreCase))
            return false;

        var elements = command.CommandElements.Skip(1).ToArray();
        for (var index = 0; index < elements.Length; index++)
        {
            if (elements[index] is CommandParameterAst parameter)
            {
                if (parameter.ParameterName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    if (parameter.Argument is StringConstantExpressionAst inline)
                        return CommandPatternMatches(inline.Value, functionName);
                    if (parameter.Argument is null && index + 1 < elements.Length &&
                        elements[index + 1] is StringConstantExpressionAst named)
                        return CommandPatternMatches(named.Value, functionName);
                    return true;
                }
                if (parameter.Argument is null && index + 1 < elements.Length && elements[index + 1] is ExpressionAst)
                    index++;
                continue;
            }
            if (elements[index] is StringConstantExpressionAst positional)
                return CommandPatternMatches(positional.Value, functionName);
            return true;
        }
        return true;
    }

    private static bool CommandPatternMatches(string pattern, string functionName)
        => new System.Management.Automation.WildcardPattern(
                pattern,
                System.Management.Automation.WildcardOptions.IgnoreCase |
                System.Management.Automation.WildcardOptions.CultureInvariant)
            .IsMatch(functionName);

    private static bool IsModuleScope(Ast node, ScriptBlockAst root)
    {
        for (var parent = node.Parent; parent is not null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst)
                return false;
            if (parent is ScriptBlockExpressionAst expression && !IsImmediatelyInvoked(expression))
                return false;
            if (parent is ScriptBlockAst scriptBlock &&
                !ReferenceEquals(scriptBlock, root) &&
                (scriptBlock.Parent is not ScriptBlockExpressionAst owner || !IsImmediatelyInvoked(owner)))
                return false;
        }
        return true;
    }

    private static bool IsImmediatelyInvoked(ScriptBlockExpressionAst expression)
        => expression.Parent is CommandAst command &&
           command.InvocationOperator is TokenKind.Ampersand or TokenKind.Dot &&
           command.CommandElements.FirstOrDefault() == expression;
}
