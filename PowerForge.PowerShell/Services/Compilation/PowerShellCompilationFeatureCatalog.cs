namespace PowerForge;

/// <summary>Maps stable compiler feature identifiers to concise planning guidance.</summary>
internal static class PowerShellCompilationFeatureCatalog
{
    internal static (string Title, string Recommendation) Describe(string featureId)
    {
        if (featureId.StartsWith("command.", StringComparison.Ordinal))
        {
            var command = featureId.Substring("command.".Length);
            return ($"Command: {command}", "Add an exact intrinsic or a larger typed command region for this command shape; reject unmatched parameter shapes.");
        }
        if (featureId.StartsWith("operator.", StringComparison.Ordinal))
            return ($"Operator: {featureId.Substring("operator.".Length)}", "Add differential PowerShell 5.1/7 coverage before lowering this operator into the typed IR.");
        if (featureId.StartsWith("syntax.", StringComparison.Ordinal))
            return ($"Syntax: {featureId.Substring("syntax.".Length)}", "Model this AST family in the bound typed IR and prove accepted-subset semantics before emission.");

        return featureId switch
        {
            PowerShellCompilationFeatureIds.Input => ("Input discovery", "Improve deterministic source discovery without executing project code."),
            PowerShellCompilationFeatureIds.Parser => ("Parser acceptance", "Fix authored parse errors or add an explicitly compatible parser path; compiler features cannot bypass invalid source."),
            PowerShellCompilationFeatureIds.ParameterType => ("Parameter types", "Expand bindable scalar, collection, or PowerShell parameter types with exact conversion behavior."),
            PowerShellCompilationFeatureIds.ParameterDefault => ("Parameter defaults", "Lower safe defaults after binding while preserving omitted-versus-bound behavior and script path metadata."),
            PowerShellCompilationFeatureIds.ParameterMetadata => ("Parameter metadata", "Model the observed binding or validation attribute in the shared parameter contract."),
            PowerShellCompilationFeatureIds.ParameterBinding => ("Parameter binding", "Extend exact, alias, and abbreviation binding while retaining ambiguity rejection."),
            PowerShellCompilationFeatureIds.DynamicCommand => ("Dynamic commands", "Keep dynamic resolution on the PowerShell path; prefer an authoring change to a statically named command."),
            PowerShellCompilationFeatureIds.ScriptBlock => ("Typed script blocks", "Add typed lambdas only for known consumers with explicit input, output, and automatic-variable contracts."),
            PowerShellCompilationFeatureIds.RuntimeScope => ("PowerShell runtime scope", "Model a safe automatic-state intrinsic or keep this unit in Package/Hybrid mode."),
            PowerShellCompilationFeatureIds.Conversion => ("Conversions", "Add a bound conversion node with differential null, collection, numeric, and culture behavior."),
            PowerShellCompilationFeatureIds.ExpandableString => ("Expandable strings", "Preserve PowerShell interpolation, scalarization, escaping, and current-culture conversion."),
            PowerShellCompilationFeatureIds.AssignmentTarget => ("Rich assignments", "Expand safe indexed/member mutation with single-evaluation and PowerShell missing-value behavior."),
            PowerShellCompilationFeatureIds.AutomaticVariableAssignment => ("Automatic-variable mutation", "Keep runtime-owned state rejected unless a complete host-independent contract exists."),
            PowerShellCompilationFeatureIds.SwitchFlags => ("Switch matching modes", "Add conservative regex, wildcard, or file switch semantics with differential tests."),
            PowerShellCompilationFeatureIds.CatchFilter => ("Typed catch filters", "Resolve exception types against the target surface and preserve PowerShell catch ordering."),
            PowerShellCompilationFeatureIds.PipelineLifecycle => ("Pipeline lifecycle blocks", "Use the bounded typed-executable begin/process/end collection contract, or retain wider lifecycle and clean behavior on a PowerShell-hosted path."),
            PowerShellCompilationFeatureIds.PipelineEnumeration => ("Pipeline enumeration", "Lower only statically typed pipeline shapes with explicit current-item and output-cardinality contracts."),
            PowerShellCompilationFeatureIds.RuntimeUsing => ("Runtime using directives", "Retain these on a PowerShell-backed path or add explicit file-backed dependency semantics."),
            PowerShellCompilationFeatureIds.RequiresDirective => ("Source requirements", "Evaluate compatible requirements during build and preserve observable failure behavior."),
            PowerShellCompilationFeatureIds.FilterFunction => ("Filter functions", "Add per-input pipeline invocation semantics before treating filters as typed methods."),
            PowerShellCompilationFeatureIds.FunctionNameCollision => ("Function name collisions", "Disambiguate generated CLR identities without changing PowerShell command identity."),
            PowerShellCompilationFeatureIds.FunctionGraph => ("Typed function graph", "Broaden whole-call-graph validation and emission while preserving declaration timing and output cardinality."),
            PowerShellCompilationFeatureIds.CommentBasedHelp => ("Comment-based help", "Keep the function on a PowerShell path or generate external help for compiled cmdlets."),
            PowerShellCompilationFeatureIds.BinaryCmdletShape => ("Binary cmdlet shape", "Expand safe PSCmdlet wrapper generation or retain the function in Hybrid fallback."),
            PowerShellCompilationFeatureIds.DictionaryFlow => ("Typed dictionary flow", "Preserve a target-typed dictionary contract through supported calls, returns, and object construction."),
            _ => (featureId, "Inspect representative units and add an explicit typed-IR capability only when the semantics can be proven.")
        };
    }
}
