using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private Dictionary<string, PowerShellSymbolId> _nativeScriptBlocks = new(StringComparer.Ordinal);

    private FunctionDeclaration[] DeclareNativeScriptBlocks(FunctionDeclaration[] declarations, PowerShellCompilationCapability capabilities)
    {
        _nativeScriptBlocks = new Dictionary<string, PowerShellSymbolId>(StringComparer.Ordinal);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding)) return declarations;
        var all = declarations.ToList();
        var names = declarations.Select(static item => item.Syntax.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            foreach (var expression in declaration.Syntax.Body.FindAll(static node => node is ScriptBlockExpressionAst,
                         searchNestedScriptBlocks: true).OfType<ScriptBlockExpressionAst>().Where(IsCompiledValueScriptBlock))
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
        }
        return all.ToArray();
    }

    internal static bool IsAssignedScriptBlock(ScriptBlockExpressionAst expression)
    {
        if (expression.Parent is not CommandExpressionAst command) return false;
        var owner = command.Parent is PipelineAst { PipelineElements.Count: 1 } pipeline ? pipeline.Parent : command.Parent;
        return owner is AssignmentStatementAst { Operator: TokenKind.Equals, Left: VariableExpressionAst variable } &&
               (variable.VariablePath.IsUnqualified || variable.VariablePath.IsLocal);
    }

    /// <summary>Identifies literal block values whose creation can use the native compiled callback owner.</summary>
    /// <remarks>Direct method arguments preserve the native method binder and dynamic invocation scope.
    /// Parameter metadata and hosted command pipelines do not enter this value-binding route.</remarks>
    internal static bool IsCompiledValueScriptBlock(ScriptBlockExpressionAst expression)
        => IsAssignedScriptBlock(expression) || expression.Parent is InvokeMemberExpressionAst invocation &&
           invocation.Arguments.Any(argument => ReferenceEquals(argument, expression));

    private static string ScriptBlockKey(string documentId, int offset)
        => documentId + ":" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private PowerShellBoundExpression? BindNativeScriptBlock(ParsedSourceDocument document, ScriptBlockExpressionAst syntax,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (_nativeScriptBlocks.TryGetValue(ScriptBlockKey(document.DocumentId, syntax.Extent.StartOffset), out var target) &&
            functions.ContainsKey(target.Name))
            return new PowerShellBoundNativeScriptBlockExpression(span, target, document.Text);
        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2960", "The script block requires a completely compiled native body and supported invocation metadata.", span));
        return null;
    }
}
