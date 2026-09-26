using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    /// <summary>
    /// Binds one closed <c>@(foreach { ... })</c> transformation into a fresh stable-scalar vector.
    /// The capture owns only implicit success records whose element type is known before lowering;
    /// it never grants a general PowerShell stream or enumerable contract to detached regions.
    /// </summary>
    private bool TryBindStableScalarVectorCapture(
        ParsedSourceDocument document,
        AssignmentStatementAst assignment,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        out PowerShellBoundStatement? capture)
    {
        capture = null;
        // Native invocation owns authored variable constraints and final conversion.
        // Detached regions still use the closed stable-vector contract below.
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            assignment.Operator != TokenKind.Equals ||
            UnwrapExpression(assignment.Right) is not ArrayExpressionAst
            {
                SubExpression.Traps: null or { Count: 0 },
                SubExpression.Statements.Count: 1
            } collected ||
            collected.SubExpression.Statements[0] is not ForEachStatementAst ||
            assignment.Left is not AttributedExpressionAst
            {
                Attribute: TypeConstraintAst constraint,
                Child: VariableExpressionAst variable
            } ||
            !variable.VariablePath.IsUnqualified)
            return false;

        var vectorType = constraint.TypeName.GetReflectionType();
        if (vectorType is null || !PowerShellRegionTransferTypePolicy.IsSupported(vectorType) ||
            !vectorType.IsArray || vectorType.GetArrayRank() != 1 ||
            vectorType.GetElementType() is not { } elementType ||
            !PowerShellStableScalarTypePolicy.IsSupported(elementType) ||
            !symbols.TryGetValue(variable.VariablePath.UserPath, out var target) ||
            target.Symbol.Kind != PowerShellSymbolKind.Local ||
            target.Type.ClrType != vectorType)
            return false;

        // The temporary stream capability lets ordinary nested statements bind their implicit
        // success records. The closed-capture validation below consumes that capability and
        // rejects every provider, explicit stream, dynamic enumeration, and native operation.
        var captureCapabilities = (capabilities &
                                   ~(PowerShellCompilationCapability.NativeFunctionBinding |
                                     PowerShellCompilationCapability.PowerShellStatementErrors |
                                     PowerShellCompilationCapability.PowerShellLanguageConversions |
                                     PowerShellCompilationCapability.PowerShellLanguageOperators)) |
                                  PowerShellCompilationCapability.PowerShellStreams |
                                  PowerShellCompilationCapability.PipelineParameterBinding;
        var statement = BindStatementCore(
            document,
            collected.SubExpression.Statements[0],
            symbols,
            functions,
            diagnostics,
            isTerminal: false,
            targetFramework,
            captureCapabilities);
        if (statement is null) return true;

        var body = new PowerShellBoundBlock(statement.Span, new[] { statement });
        if (!IsClosedStableScalarVectorCapture(body, elementType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2937",
                "Typed foreach collection capture requires only closed scalar success records and statically bounded scalar or vector enumeration.",
                PowerShellSourceParser.GetSpan(document, collected.Extent)));
            return true;
        }

        capture = new PowerShellBoundOutputCaptureStatement(
            PowerShellSourceParser.GetSpan(document, assignment.Extent),
            target.Symbol,
            body,
            kind: PowerShellOutputCaptureKind.StableScalarVector,
            capturedElementType: elementType);
        target.Refine(new PowerShellTypeFact(vectorType, PowerShellTypeFactProvenance.Explicit,
            "A closed foreach capture constructs the authored stable-scalar vector."), PowerShellValueState.Known);
        diagnostics.Add(new PowerShellSemanticDiagnostic(
            "PSB2938",
            "Closed stable-scalar vector capture is eligible only for a guarded region; retained PowerShell owns downstream vector enumeration.",
            PowerShellSourceParser.GetSpan(document, assignment.Extent)));
        return true;
    }

    private static bool IsClosedStableScalarVectorCapture(PowerShellBoundBlock body, Type elementType)
    {
        var statements = PowerShellSemanticAnalyzer.EnumerateStatements(body).ToArray();
        var writes = statements.OfType<PowerShellBoundStreamWriteStatement>().ToArray();
        if (writes.Length == 0 || statements.Any(static statement =>
                statement is PowerShellBoundReturnStatement or PowerShellBoundThrowStatement or
                    PowerShellBoundOutputCaptureStatement or PowerShellBoundCommandRegionStatement or
                    PowerShellBoundCommandCaptureStatement))
            return false;
        if (writes.Any(write => write.Kind != PowerShellStreamCommandKind.Success || write.Provider is not null ||
                write.OutputBinding != PowerShellOutputBindingKind.Default || write.UsesNativeInvocation ||
                write.UsesCommandHostEnumeration || write.Message.Type.ClrType != elementType ||
                write.Message.ValueState is PowerShellValueState.AutomationNull or PowerShellValueState.Missing))
            return false;
        return statements.Where(static statement => statement is not PowerShellBoundStreamWriteStatement)
            .All(static statement => statement is PowerShellBoundIfStatement or PowerShellBoundWhileStatement or
                    PowerShellBoundForStatement or PowerShellBoundForEachStatement or PowerShellBoundSwitchStatement ||
                !statement.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStreams));
    }

    // Native pipeline capture deliberately erases expression types. Inspect only
    // authored output positions; conditions and iterators remain bound by their
    // existing native owners. No command, cast, callback or extra body can supply records.
    private static bool HasClosedNativeTypedRecords(StatementBlockAst block, Type elementType)
        => block.Traps is null or { Count: 0 } && block.Statements.Count > 0 &&
           block.Statements.All(statement => statement switch
           {
               PipelineAst { PipelineElements.Count: 1 } pipeline when
                   pipeline.PipelineElements[0] is CommandExpressionAst { Redirections.Count: 0 } command
                   => command.Expression is ConstantExpressionAst constant &&
                      constant.Value is not null && constant.Value.GetType() == elementType,
               IfStatementAst conditional => conditional.Clauses.All(clause =>
                   HasClosedNativeTypedRecords(clause.Item2, elementType)) &&
                   (conditional.ElseClause is null || HasClosedNativeTypedRecords(conditional.ElseClause, elementType)),
               ForEachStatementAst loop => HasClosedNativeTypedRecords(loop.Body, elementType),
               _ => false
           });

    private static IEnumerable<Type> NativeCaptureConstraintTypes(AssignmentStatementAst assignment,
        VariableExpressionAst? destination, PowerShellSemanticSymbolBinding? target)
    {
        if (target?.Type.Provenance == PowerShellTypeFactProvenance.Explicit) yield return target.Type.ClrType;
        if (destination is null) yield break;
        static string Identity(VariableExpressionAst variable)
            => variable.VariablePath.IsLocal ? variable.VariablePath.UserPath.Substring(6) : variable.VariablePath.UserPath;
        var name = Identity(destination);
        var body = FindOwningFunctionBody(assignment);
        var targets = body is null ? new[] { assignment.Left } : body.FindAll(node =>
                node is AssignmentStatementAst prior &&
                PowerShellAssignmentTargetPolicy.FindDirectVariable(prior.Left, true) is { } variable &&
                Identity(variable).Equals(name, StringComparison.OrdinalIgnoreCase), false)
            .Cast<AssignmentStatementAst>().Select(prior => prior.Left);
        foreach (var left in targets)
            for (var attributed = left as AttributedExpressionAst; attributed is not null;
                 attributed = attributed.Child as AttributedExpressionAst)
                if (attributed.Attribute is TypeConstraintAst constraint && constraint.TypeName.GetReflectionType() is { } type)
                    yield return type;
        if (body is null) yield break;
        foreach (var parameter in PowerShellParameterSyntax.GetParameters(body))
            if (parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase))
                foreach (var constraint in parameter.Attributes.OfType<TypeConstraintAst>())
                    if (constraint.TypeName.GetReflectionType() is { } type) yield return type;
    }

    private PowerShellBoundStatement? BindOutputCapture(ParsedSourceDocument document, AssignmentStatementAst assignment,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework, PowerShellCompilationCapability capabilities,
        ArrayExpressionAst? collectedArray = null)
    {
        var span = PowerShellSourceParser.GetSpan(document, assignment.Extent);
        var usesNativeInvocation = capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding);
        if (collectedArray is not null && assignment.Operator != TokenKind.Equals)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2939",
                "Native statement arrays require simple assignment so collection ownership is explicit.", span));
            return null;
        }
        var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left, usesNativeInvocation);
        var operation = PowerShellMutationSemanticBinder.GetAssignmentOperator(assignment.Operator);
        var nativeConditionalAccess = usesNativeInvocation && assignment.Right is IfStatementAst &&
                                      assignment.Operator == TokenKind.Equals &&
                                      IsNativeConditionalAccessCaptureTarget(assignment.Left);
        PowerShellSemanticSymbolBinding? target = null;
        if (variable is not null) symbols.TryGetValue(variable.VariablePath.UserPath, out target);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) ||
            variable is null && !nativeConditionalAccess || operation is null ||
            !usesNativeInvocation && (variable is null || assignment.Operator != TokenKind.Equals || assignment.Left is not VariableExpressionAst ||
                IsRuntimeOwnedScope(variable.VariablePath.UserPath) || target is null ||
                target.Type.Provenance != PowerShellTypeFactProvenance.Unknown && target.Type.ClrType != typeof(object) ||
                target.Type.Provenance is PowerShellTypeFactProvenance.Explicit or PowerShellTypeFactProvenance.Int32OrDouble))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2930",
                "Captured statement output requires a qualified command success-stream host and either a native variable target or an unconstrained local.", span));
            return null;
        }
        if (!usesNativeInvocation && assignment.Right.FindAll(static node => node is ReturnStatementAst, false).Any())
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2931",
                "A return inside captured statement output requires an explicit enclosing-function transfer contract.", span));
            return null;
        }
        if (usesNativeInvocation && PowerShellControlFlowBindingPolicy.HasLoopTransferLeavingCapture(assignment.Right))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2936",
                "A loop transfer leaving native captured output requires an explicit enclosing-control-flow transfer contract.", span));
            return null;
        }
        // The assignment owns errors that escape the captured statement (including
        // conditions and iterators). Handling them inside the collector would assign partial
        // records after a failed RHS. Inner authored statements keep their boundaries.
        PowerShellBoundBlock? body;
        if (collectedArray is null)
        {
            var statement = BindStatementCore(document, assignment.Right, symbols, functions, diagnostics, false,
                targetFramework, capabilities, allowNonTerminalSuccessOutput: true);
            body = statement is null ? null : new PowerShellBoundBlock(statement.Span, new[] { statement });
        }
        else
            body = BindBlock(document, collectedArray.SubExpression, symbols, functions, diagnostics, targetFramework,
                capabilities, allowNonTerminalSuccessOutput: true);
        if (body is null) return null;
        // Binding the body can merge assignments and declarations back into its enclosing
        // symbol table. The capture destination must still accept its collapsed result.
        if (!usesNativeInvocation && (target!.Type.ClrType != typeof(object) ||
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
        {
            if (collectedArray is not null && NativeCaptureConstraintTypes(assignment, variable, target)
                .Any(vector => vector.IsArray && vector.GetElementType() is { } element &&
                    PowerShellStableScalarTypePolicy.IsSupported(element) &&
                    !HasClosedNativeTypedRecords(collectedArray.SubExpression, element)))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2940",
                    "Typed native statement-array capture requires records already matching the declared stable-scalar element type; element-conversion error semantics remain hosted.", span));
                return null;
            }
            return new PowerShellBoundOutputCaptureStatement(span, target?.Symbol,
                body,
                new PowerShellNativeAssignmentTarget(assignment.Left.Extent.Text, document.Path, document.Text,
                    PowerShellSourceParser.GetSpan(document, assignment.Left.Extent),
                    assignment.Left.Extent.StartOffset, assignment.Left.Extent.EndOffset), operation.Value,
                collectedArray is null ? PowerShellOutputCaptureKind.CollapsedPowerShellValue : PowerShellOutputCaptureKind.NativeObjectArray,
                shareEmptyArray: collectedArray is not null &&
                                 _semanticProfile.Family == PowerShellCompilationSemanticHostFamily.PowerShell7);
        }
        target!.Refine(new PowerShellTypeFact(typeof(object), PowerShellTypeFactProvenance.Inferred,
            "Captured success output collapses to null, a single record, or an Object array."), PowerShellValueState.Unknown);
        target.SetModuleStateDerived(target.IsModuleStateDerived ||
            PowerShellSemanticAnalyzer.EnumerateStatements(body)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
                .Any(PowerShellModuleStateOriginPolicy.IsDerived));
        return new PowerShellBoundOutputCaptureStatement(span, target.Symbol, body);
    }
}
