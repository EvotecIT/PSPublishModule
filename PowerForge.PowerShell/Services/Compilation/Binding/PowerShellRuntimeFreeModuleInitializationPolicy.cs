namespace PowerForge;

/// <summary>Bounds initialization to operations whose failure can roll back instance storage.</summary>
internal static class PowerShellRuntimeFreeModuleInitializationPolicy
{
    internal static bool Validate(PowerShellBoundFunction function,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (function.Symbol.Kind != PowerShellSymbolKind.ModuleInitializer) return true;
        var initialCount = diagnostics.Count;
        var unsupportedEffects = function.Body.Effects &
            ~(PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.TerminatingError);
        if (unsupportedEffects != PowerShellSemanticEffect.None)
            Report(function.Body.Span, "Managed module initialization cannot expose output or external effects before it commits.");
        foreach (var statement in PowerShellSemanticAnalyzer.EnumerateStatements(function.Body))
        {
            if (statement is PowerShellBoundReturnStatement or PowerShellBoundBreakStatement or PowerShellBoundContinueStatement)
                Report(statement.Span, "Early initialization transfers require a complete field-initialization proof and are not yet qualified.");
            foreach (var expression in PowerShellSemanticAnalyzer.EnumerateDirectExpressions(statement)
                         .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions))
            {
                if (expression is PowerShellBoundInvocationExpression)
                    Report(expression.Span, "Initialization calls into module functions require an interprocedural field-initialization proof and are not yet qualified.");
                else if (expression is PowerShellBoundRuntimeStateExpression)
                    Report(expression.Span, "Ambient runtime state reads require explicit constructor inputs for deterministic module initialization.");
                else if (expression is PowerShellBoundClrMemberExpression { IsStatic: true })
                    Report(expression.Span, "Static CLR state reads are not yet qualified for transactional module initialization.");
                else if (expression is PowerShellBoundClrInvocationExpression invocation &&
                         !(invocation.InvocationKind == PowerShellClrInvocationKind.Constructor &&
                           invocation.DeclaringType.Assembly == typeof(Exception).Assembly &&
                           typeof(Exception).IsAssignableFrom(invocation.DeclaringType)))
                    Report(expression.Span, "CLR calls in module initialization require a qualified effect contract; only core exception construction is currently admitted.");
            }
        }
        return diagnostics.Count == initialCount;

        void Report(SourceSpan span, string message)
            => diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2951", message, span));
    }
}
