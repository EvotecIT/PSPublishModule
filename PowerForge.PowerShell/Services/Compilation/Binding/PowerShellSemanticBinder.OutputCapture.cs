using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private PowerShellBoundStatement? BindOutputCapture(ParsedSourceDocument document, AssignmentStatementAst assignment,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework, PowerShellCompilationCapability capabilities)
    {
        var span = PowerShellSourceParser.GetSpan(document, assignment.Extent);
        var usesNativeInvocation = capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) ||
            assignment.Operator != TokenKind.Equals || assignment.Left is not VariableExpressionAst variable ||
            IsRuntimeOwnedScope(variable.VariablePath.UserPath) ||
            !symbols.TryGetValue(variable.VariablePath.UserPath, out var target) ||
            !usesNativeInvocation && (target.Type.Provenance != PowerShellTypeFactProvenance.Unknown && target.Type.ClrType != typeof(object) ||
                target.Type.Provenance is PowerShellTypeFactProvenance.Explicit or PowerShellTypeFactProvenance.Int32OrDouble))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2930",
                "Captured statement output requires an unconstrained local and a qualified command success-stream host.", span));
            return null;
        }
        if (assignment.Right.FindAll(static node => node is ReturnStatementAst, false).Any())
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2931",
                "A return inside captured statement output requires an explicit enclosing-function transfer contract.", span));
            return null;
        }
        // The assignment owns errors that escape the loop (including its condition
        // and iterator). Handling them inside the collector would assign partial
        // records after a failed RHS. Inner authored statements keep their boundaries.
        var body = BindStatementCore(document, assignment.Right, symbols, functions, diagnostics, false, targetFramework, capabilities);
        if (body is null) return null;
        // Binding a loop can merge assignments and declarations back into its enclosing
        // symbol table. The capture destination must still accept its collapsed result.
        if (!usesNativeInvocation && (target.Type.ClrType != typeof(object) ||
            target.Type.Provenance is PowerShellTypeFactProvenance.Explicit or PowerShellTypeFactProvenance.Int32OrDouble))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2935",
                "The captured body changes its destination's type or constraint; the final assignment requires a qualified conversion contract.", span));
            return null;
        }
        if (!usesNativeInvocation && body.Capabilities.HasFlag(PowerShellRequiredCapability.CommandRegion))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2932",
                "Captured statement output cannot yet redirect hosted command-region records into its collector.", span));
            return null;
        }
        if (usesNativeInvocation)
            return new PowerShellBoundOutputCaptureStatement(span, target.Symbol,
                new PowerShellBoundBlock(body.Span, new[] { body }), usesNativeInvocation: true);
        target.Refine(new PowerShellTypeFact(typeof(object), PowerShellTypeFactProvenance.Inferred,
            "Captured success output collapses to null, a single record, or an Object array."), PowerShellValueState.Unknown);
        target.SetModuleStateDerived(target.IsModuleStateDerived ||
            PowerShellSemanticAnalyzer.EnumerateStatements(new PowerShellBoundBlock(body.Span, new[] { body }))
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
                .Any(PowerShellModuleStateOriginPolicy.IsDerived));
        return new PowerShellBoundOutputCaptureStatement(span, target.Symbol, new PowerShellBoundBlock(body.Span, new[] { body }));
    }
}
