using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private Dictionary<string, PowerShellSymbolId> _nativeScriptBlocks = new(StringComparer.Ordinal);

    private FunctionDeclaration[] DeclareNativeScriptBlocks(FunctionDeclaration[] declarations, PowerShellCompilationCapability capabilities,
        string? targetFramework)
    {
        _nativeScriptBlocks = new Dictionary<string, PowerShellSymbolId>(StringComparer.Ordinal);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding)) return declarations;
        var all = declarations.ToList();
        var names = declarations.Select(static item => item.Syntax.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            foreach (var expression in declaration.Syntax.Body.FindAll(static node => node is ScriptBlockExpressionAst,
                         searchNestedScriptBlocks: true).OfType<ScriptBlockExpressionAst>().Where(expression =>
                         capabilities.HasFlag(PowerShellCompilationCapability.PowerShellModuleState) &&
                         IsCompiledValueScriptBlock(expression, targetFramework) &&
                         (capabilities != PowerShellCompilationCapabilities.HybridModule || !IsTransportedScriptBlock(expression))))
            {
                var document = declaration.Document;
                var key = ScriptBlockKey(document.DocumentId, expression.Extent.StartOffset);
                if (_nativeScriptBlocks.ContainsKey(key)) continue;
                var name = "__PowerForgeScriptBlock_" + document.DocumentId + "_" + expression.Extent.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
                while (!names.Add(name)) name += "_";
                var span = PowerShellSourceParser.GetSpan(document, expression.Extent);
                var symbol = new PowerShellSymbolId(PowerShellSymbolKind.NativeScriptBlock, document.DocumentId, name, span);
                var function = new FunctionDefinitionAst(expression.Extent, false, false, name, null,
                    (ScriptBlockAst)expression.ScriptBlock.Copy());
                _nativeScriptBlocks.Add(key, symbol);
                all.Add(new FunctionDeclaration(document, function, symbol));
            }
            foreach (var nested in declaration.Syntax.Body.FindAll(static node => node is FunctionDefinitionAst,
                         searchNestedScriptBlocks: true).OfType<FunctionDefinitionAst>().Where(IsCompiledNestedFunction))
            {
                var document = declaration.Document;
                var key = ScriptBlockKey(document.DocumentId, nested.Extent.StartOffset);
                if (_nativeScriptBlocks.ContainsKey(key)) continue;
                var name = "__PowerForgeNestedFunction_" + document.DocumentId + "_" + nested.Extent.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
                while (!names.Add(name)) name += "_";
                var span = PowerShellSourceParser.GetSpan(document, nested.Extent);
                var symbol = new PowerShellSymbolId(PowerShellSymbolKind.NativeScriptBlock, document.DocumentId, name, span);
                var function = CreateNativeFunctionSyntax(nested, name);
                _nativeScriptBlocks.Add(key, symbol);
                all.Add(new FunctionDeclaration(document, function, symbol, nativeNestedDeclaration: true));
            }
        }
        return all.ToArray();
    }

    private static FunctionDefinitionAst CreateNativeFunctionSyntax(FunctionDefinitionAst original, string name)
        => new(original.Extent, false, original.IsFilter, name,
            original.Parameters?.Select(static parameter => (ParameterAst)parameter.Copy()),
            (ScriptBlockAst)original.Body.Copy());

    internal static bool IsAssignedScriptBlock(ScriptBlockExpressionAst expression)
    {
        if (expression.Parent is not CommandExpressionAst command) return false;
        var owner = command.Parent is PipelineAst { PipelineElements.Count: 1 } pipeline ? pipeline.Parent : command.Parent;
        return owner is AssignmentStatementAst { Operator: TokenKind.Equals, Left: VariableExpressionAst variable } &&
               (variable.VariablePath.IsUnqualified || variable.VariablePath.IsLocal);
    }

    /// <summary>Identifies literal block values whose creation can use the native compiled callback owner.</summary>
    /// <remarks>Values use native conversion and dynamic invocation scope, including returned and collection values.
    /// Parameter metadata and hosted command pipelines retain their native source owner.</remarks>
    internal static bool IsCompiledValueScriptBlock(ScriptBlockExpressionAst expression, string? targetFramework = null)
    {
        if (IsSwitchPredicate(expression)) return true;
        if (expression.Parent is MemberExpressionAst member && ReferenceEquals(member.Member, expression)) return false;
        for (var parent = expression.Parent; parent is not null && parent is not NamedBlockAst; parent = parent.Parent)
            if (parent is AttributeAst or ParameterAst or CommandAst) return false;
        return true;
    }

    internal static bool IsSwitchPredicate(ScriptBlockExpressionAst expression)
        => expression.Parent is SwitchStatementAst statement &&
           statement.Clauses.Any(clause => ReferenceEquals(clause.Item1, expression));

    // Preserve filter process clauses and header parameters in the canonical child contract.
    // Workflow declarations require a different execution owner and remain hosted.
    internal static bool IsCompiledNestedFunction(FunctionDefinitionAst function)
        => !function.IsWorkflow;

    private PowerShellBoundStatement? BindNativeFunctionDeclaration(ParsedSourceDocument document,
        FunctionDefinitionAst syntax, IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (IsCompiledNestedFunction(syntax) &&
            _nativeScriptBlocks.TryGetValue(ScriptBlockKey(document.DocumentId, syntax.Extent.StartOffset), out var target) &&
            functions.ContainsKey(target.Name))
            return new PowerShellBoundExpressionStatement(span,
                new PowerShellBoundNativeScriptBlockExpression(span, target, document.Text, syntax.Name), emitsOutput: false);
        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2961",
            "The nested declaration requires a completely compiled native body and qualified declaration metadata; workflows and unsupported child bodies remain hosted.", span));
        return null;
    }

    private static string ScriptBlockKey(string documentId, int offset)
        => documentId + ":" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private PowerShellBoundExpression? BindNativeScriptBlock(ParsedSourceDocument document, ScriptBlockExpressionAst syntax,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions, ICollection<PowerShellSemanticDiagnostic> diagnostics,
        PowerShellCompilationCapability capabilities)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (capabilities == PowerShellCompilationCapabilities.HybridModule && IsTransportedScriptBlock(syntax))
            return new PowerShellBoundTransportedScriptBlockExpression(span, document.Text);
        if (_nativeScriptBlocks.TryGetValue(ScriptBlockKey(document.DocumentId, syntax.Extent.StartOffset), out var target) &&
            functions.ContainsKey(target.Name))
            return new PowerShellBoundNativeScriptBlockExpression(span, target, document.Text,
                isSwitchPredicate: IsSwitchPredicate(syntax));
        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2960", "The script block requires a completely compiled native body and supported invocation metadata.", span));
        return null;
    }

    private static bool IsTransportedScriptBlock(ScriptBlockExpressionAst syntax)
    {
        if (!Generated.Runtime.PowerShellNativeFunctionContext.RequiresNativeCallbackBody(syntax.ScriptBlock) ||
            syntax.Parent is not CommandExpressionAst commandExpression)
            return false;
        var statement = commandExpression.Parent is PipelineAst { PipelineElements.Count: 1 } pipeline
            ? pipeline.Parent : commandExpression.Parent;
        if (statement is not AssignmentStatementAst { Operator: TokenKind.Equals } assignment) return false;
        if (assignment.Left is MemberExpressionAst { Static: true, Expression: TypeExpressionAst, Member: StringConstantExpressionAst })
            return true;
        if (!IsAssignedScriptBlock(syntax) || FindOwningFunctionBody(syntax) is not { } owner)
            return false;
        var target = (VariableExpressionAst)assignment.Left;
        var name = target.VariablePath.UserPath;
        var references = owner.FindAll(node => node is VariableExpressionAst variable &&
                variable.Extent.StartOffset > syntax.Extent.EndOffset &&
                variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase),
            searchNestedScriptBlocks: false).OfType<VariableExpressionAst>()
            .OrderBy(static variable => variable.Extent.StartOffset)
            .TakeWhile(variable => variable.Parent is not AssignmentStatementAst write || !ReferenceEquals(variable, write.Left))
            .ToArray();
        return references.Length > 0 && references.All(variable =>
            variable.Parent is CommandAst command &&
            command.CommandElements.Skip(1).Zip(command.CommandElements, (value, parameter) => (value, parameter)).Any(pair =>
                ReferenceEquals(pair.value, variable) && pair.parameter is CommandParameterAst { Argument: null } parameter &&
                (parameter.ParameterName.Equals("ScriptBlock", StringComparison.OrdinalIgnoreCase) ||
                 parameter.ParameterName.Equals("Parallel", StringComparison.OrdinalIgnoreCase))));
    }
}
