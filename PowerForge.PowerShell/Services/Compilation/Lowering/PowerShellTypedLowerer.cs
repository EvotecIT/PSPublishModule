namespace PowerForge;

/// <summary>
/// Selects typed CLR operations from analyzed bound nodes. It does not render target-language source.
/// </summary>
internal sealed partial class PowerShellTypedLowerer
{
    internal PowerShellLoweredProgram Lower(
        PowerShellBoundProgram program,
        PowerShellCompilationCapability targetCapabilities = PowerShellCompilationCapability.None)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        var diagnostics = new List<PowerShellSemanticDiagnostic>(program.Diagnostics);
        var functions = new List<PowerShellLoweredFunction>();
        var boundParameterBindings = PropagateHostRequirement(program, function =>
            ContainsBoundParameterPresence(function.Body) ||
            function.Parameters.Any(parameter =>
                parameter.Contract.DefaultValue is not null ||
                !parameter.Contract.IsMandatory &&
                parameter.Contract.Validations.Length > 0 &&
                targetCapabilities.HasFlag(PowerShellCompilationCapability.BoundParameters)));
        var runtimeStateBindings = PropagateHostRequirement(program, static function => RequiresRuntimeStateHostBinding(function.Body));
        var moduleStateReadBindings = PropagateHostRequirement(program, static function =>
            ContainsPowerShellModuleStateRead(function.Body));
        var moduleStateWriteBindings = PropagateHostRequirement(program, static function =>
            ContainsPowerShellModuleStateWrite(function.Body));
        var streamBindings = PropagateHostRequirement(program, static function => ContainsPowerShellStreamWrite(function.Body));
        var providerCancellationBindings = PropagateHostRequirement(program, static function => ContainsCooperativeProvider(function.Body));
        var commandRegionBindings = PropagateHostRequirement(program, static function => ContainsPowerShellCommandRegion(function.Body));
        var bySymbol = program.Functions.ToDictionary(
            static function => function.Symbol.StableKey,
            function => new LoweringFunctionContext(
                function,
                boundParameterBindings.Contains(function.Symbol.StableKey),
                streamBindings.Contains(function.Symbol.StableKey),
                providerCancellationBindings.Contains(function.Symbol.StableKey),
                commandRegionBindings.Contains(function.Symbol.StableKey),
                runtimeStateBindings.Contains(function.Symbol.StableKey),
                moduleStateReadBindings.Contains(function.Symbol.StableKey),
                moduleStateWriteBindings.Contains(function.Symbol.StableKey)),
            StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (function.Disposition.Kind != PowerShellExecutionDispositionKind.Typed)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    string.IsNullOrWhiteSpace(function.Disposition.ReasonCode) ? "PSL1001" : function.Disposition.ReasonCode,
                    function.Disposition.Explanation,
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellLanguageOperators) &&
                !targetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageOperators))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1002",
                    "PowerShell comparison, wildcard, or membership semantics require the PowerShell language-operator target capability.",
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellLanguageConversions) &&
                !targetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1007",
                    "PowerShell language conversions require the PowerShell language-conversion target capability.",
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.RuntimeStateIntrinsics) &&
                !targetCapabilities.HasFlag(PowerShellCompilationCapability.RuntimeStateIntrinsics))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1003",
                    "Runtime-state semantics require the runtime-state-intrinsics target capability.",
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellModuleState) &&
                !targetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellModuleState))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1011",
                    "Live script-module state requires the Hybrid module-state target capability.",
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellHostTypes) &&
                !targetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1004",
                    "PSVersion semantics require the PowerShell-host-types target capability.",
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStreams) &&
                !targetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1005",
                    "WhatIf and ShouldProcess semantics require a stream-backed PowerShell host.",
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.RuntimeFreeProviderOperations) &&
                !targetCapabilities.HasFlag(PowerShellCompilationCapability.RuntimeFreeProviderOperations))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1010",
                    "External provider operations require the runtime-free provider-operation target capability.",
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.CommandRegion) &&
                !targetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1006",
                    "Hosted command regions require the stream-backed PowerShell target capability.",
                    function.Symbol.Declaration));
                continue;
            }
            if (function.Capabilities.HasFlag(PowerShellRequiredCapability.NativeProcess))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1008",
                    "Native process creation is not available to typed compilation targets and cannot be certified runtime-free.",
                    function.Symbol.Declaration));
                continue;
            }

            var generatedHostParameterCollision = FindGeneratedHostParameterCollision(
                function,
                boundParameterBindings.Contains(function.Symbol.StableKey),
                streamBindings.Contains(function.Symbol.StableKey),
                providerCancellationBindings.Contains(function.Symbol.StableKey),
                commandRegionBindings.Contains(function.Symbol.StableKey),
                runtimeStateBindings.Contains(function.Symbol.StableKey),
                moduleStateReadBindings.Contains(function.Symbol.StableKey),
                moduleStateWriteBindings.Contains(function.Symbol.StableKey));
            if (generatedHostParameterCollision is not null)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSL1009",
                    $"Parameter '${generatedHostParameterCollision}' collides with compiler-owned host parameter '{generatedHostParameterCollision}'.",
                    function.Symbol.Declaration));
                continue;
            }

            var statements = new List<PowerShellLoweredStatement>();
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var localTypes = function.Locals.ToDictionary(static local => local.Symbol.StableKey, static local => local.Type.ClrType, StringComparer.Ordinal);
            var symbolTypes = function.Parameters.ToDictionary(static parameter => parameter.Symbol.StableKey, static parameter => parameter.Type.ClrType, StringComparer.Ordinal);
            foreach (var local in function.Locals) symbolTypes[local.Symbol.StableKey] = local.Type.ClrType;
            var names = new LoweredNameAllocator(function.Parameters.Select(static parameter => parameter.Symbol.Name)
                .Concat(function.Locals.Select(static local => local.Symbol.Name)));
            var topLevelAssignments = function.Body.Statements.OfType<PowerShellBoundAssignmentStatement>()
                .GroupBy(static assignment => assignment.Target.StableKey, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Min(assignment => assignment.Span.StartOffset), StringComparer.Ordinal);
            var predeclared = EnumerateNestedAssignments(function.Body)
                .Where(item => localTypes.ContainsKey(item.Key))
                .Where(item => !topLevelAssignments.TryGetValue(item.Key, out var topLevelOffset) || item.Offset < topLevelOffset)
                .Select(static item => item.Key)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();
            foreach (var key in predeclared)
            {
                var local = function.Locals.Single(candidate => candidate.Symbol.StableKey == key);
                statements.Add(new PowerShellLoweredLocalDeclarationStatement(local.Symbol.Declaration, local.Symbol, localTypes[key]));
                declared.Add(key);
            }
            foreach (var statement in function.Body.Statements)
                statements.Add(LowerStatement(statement, bySymbol, symbolTypes, localTypes, declared, names, targetCapabilities));

            functions.Add(new PowerShellLoweredFunction(
                function.Symbol,
                PowerShellCSharpSymbolRenderer.Identifier(function.Symbol.Name),
                function.ReturnType.ClrType,
                function.Parameters.Select(static parameter => new PowerShellLoweredParameter(parameter.Symbol, parameter.Type.ClrType, parameter.Contract)).ToArray(),
                function.Locals.Select(static local => new PowerShellLoweredLocal(local.Symbol, local.Type.ClrType)).ToArray(),
                function.Help,
                function.Aliases.ToArray(),
                function.CommandBinding,
                function.DeclaredOutputType,
                function.DeclaredOutputTypeName,
                boundParameterBindings.Contains(function.Symbol.StableKey),
                streamBindings.Contains(function.Symbol.StableKey),
                function.Capabilities.HasFlag(PowerShellRequiredCapability.RuntimeFreeProviderOperations),
                function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStreams),
                providerCancellationBindings.Contains(function.Symbol.StableKey),
                commandRegionBindings.Contains(function.Symbol.StableKey),
                runtimeStateBindings.Contains(function.Symbol.StableKey),
                moduleStateReadBindings.Contains(function.Symbol.StableKey),
                moduleStateWriteBindings.Contains(function.Symbol.StableKey),
                function.OutputCardinality,
                PowerShellSemanticAnalyzer.EnumerateStatements(function.Body)
                    .Select(PowerShellSemanticAnalyzer.GetSuccessOutputExpression)
                    .Where(static expression => expression is not null)
                    .Select(static expression => expression!.ValueState)
                    .Distinct()
                    .OrderBy(static state => state)
                    .ToArray(),
                ResolveCollectionElementType(function),
                statements.ToArray(),
                function.Body.Span));
        }

        return new PowerShellLoweredProgram(
            functions.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray(),
            diagnostics.OrderBy(static diagnostic => diagnostic.Span.DocumentId, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Span.StartOffset)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToArray(),
            targetCapabilities);
    }

    private static Type? ResolveCollectionElementType(PowerShellBoundFunction function)
    {
        if (function.OutputCardinality != PowerShellOutputCardinality.Collection)
            return null;
        var elementTypes = PowerShellSemanticAnalyzer.EnumerateStatements(function.Body)
            .Select(PowerShellSemanticAnalyzer.GetSuccessOutputExpression)
            .Where(static expression => expression is not null)
            .SelectMany(static expression => expression is PowerShellBoundArrayExpression array
                ? array.Elements.Select(static element => element.Type.ClrType)
                : new[] { expression!.Type.ClrType.GetElementType() ?? typeof(object) })
            .Distinct()
            .ToArray();
        return elementTypes.Length == 1 ? elementTypes[0] : typeof(object);
    }

    private static IEnumerable<(string Key, int Offset)> EnumerateNestedAssignments(PowerShellBoundBlock block)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundIfStatement conditional)
            {
                foreach (var clause in conditional.Clauses)
                {
                    foreach (var assignment in EnumerateAssignments(clause.Body)) yield return assignment;
                }
                if (conditional.ElseBlock is not null)
                {
                    foreach (var assignment in EnumerateAssignments(conditional.ElseBlock)) yield return assignment;
                }
            }
            else if (statement is PowerShellBoundWhileStatement loop)
            {
                foreach (var assignment in EnumerateAssignments(loop.Body)) yield return assignment;
            }
            else if (statement is PowerShellBoundForStatement forLoop)
            {
                if (forLoop.Initializer is not null) yield return (forLoop.Initializer.Target.StableKey, forLoop.Span.StartOffset);
                foreach (var assignment in EnumerateAssignments(forLoop.Body)) yield return assignment;
            }
            else if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
                if (!forEachLoop.DeclareVariable)
                    yield return (forEachLoop.Variable.StableKey, forEachLoop.Span.StartOffset);
                foreach (var assignment in EnumerateAssignments(forEachLoop.Body).Where(item => item.Key != forEachLoop.Variable.StableKey)) yield return assignment;
            }
            else if (statement is PowerShellBoundSwitchStatement switchStatement)
            {
                foreach (var clause in switchStatement.Clauses)
                foreach (var assignment in EnumerateAssignments(clause.Body))
                    yield return assignment;
                if (switchStatement.DefaultBlock is not null)
                foreach (var assignment in EnumerateAssignments(switchStatement.DefaultBlock))
                    yield return assignment;
            }
            else if (statement is PowerShellBoundTryStatement tryStatement)
            {
                foreach (var assignment in EnumerateAssignments(tryStatement.Body)) yield return assignment;
                foreach (var clause in tryStatement.Catches)
                foreach (var assignment in EnumerateAssignments(clause.Body))
                    yield return assignment;
                if (tryStatement.FinallyBlock is not null)
                foreach (var assignment in EnumerateAssignments(tryStatement.FinallyBlock))
                    yield return assignment;
            }
        }
    }

    private static IEnumerable<(string Key, int Offset)> EnumerateAssignments(PowerShellBoundBlock block)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundAssignmentStatement assignment) yield return (assignment.Target.StableKey, assignment.Span.StartOffset);
            if (statement is PowerShellBoundIfStatement conditional)
            {
                foreach (var clause in conditional.Clauses)
                foreach (var nested in EnumerateAssignments(clause.Body))
                    yield return nested;
                if (conditional.ElseBlock is not null)
                foreach (var nested in EnumerateAssignments(conditional.ElseBlock))
                    yield return nested;
            }
            else if (statement is PowerShellBoundWhileStatement loop)
            {
                foreach (var nested in EnumerateAssignments(loop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundForStatement forLoop)
            {
                if (forLoop.Initializer is not null) yield return (forLoop.Initializer.Target.StableKey, forLoop.Span.StartOffset);
                foreach (var nested in EnumerateAssignments(forLoop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
                if (!forEachLoop.DeclareVariable)
                    yield return (forEachLoop.Variable.StableKey, forEachLoop.Span.StartOffset);
                foreach (var nested in EnumerateAssignments(forEachLoop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundSwitchStatement switchStatement)
            {
                foreach (var clause in switchStatement.Clauses)
                foreach (var nested in EnumerateAssignments(clause.Body))
                    yield return nested;
                if (switchStatement.DefaultBlock is not null)
                foreach (var nested in EnumerateAssignments(switchStatement.DefaultBlock))
                    yield return nested;
            }
            else if (statement is PowerShellBoundTryStatement tryStatement)
            {
                foreach (var nested in EnumerateAssignments(tryStatement.Body)) yield return nested;
                foreach (var clause in tryStatement.Catches)
                foreach (var nested in EnumerateAssignments(clause.Body))
                    yield return nested;
                if (tryStatement.FinallyBlock is not null)
                foreach (var nested in EnumerateAssignments(tryStatement.FinallyBlock))
                    yield return nested;
            }
        }
    }

    private static PowerShellLoweredStatement LowerStatement(
        PowerShellBoundStatement statement,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => new PowerShellLoweredAssignmentStatement(
                assignment.Span,
                assignment.Target,
                symbolTypes[assignment.Target.StableKey],
                LowerExpression(assignment.Value, functions, names, targetCapabilities),
                localTypes.ContainsKey(assignment.Target.StableKey) && declared.Add(assignment.Target.StableKey),
                assignment.Operation,
                assignment.NormalizeNullString,
                assignment.CheckedIntegral),
            PowerShellBoundModuleVariableAssignmentStatement assignment => new PowerShellLoweredModuleVariableAssignmentStatement(
                assignment.Span,
                assignment.Name,
                LowerExpression(assignment.Value, functions, names, targetCapabilities)),
            PowerShellBoundIndexAssignmentStatement assignment => new PowerShellLoweredIndexAssignmentStatement(
                assignment.Span,
                LowerExpression(assignment.Target, functions, names, targetCapabilities),
                LowerExpression(assignment.Index, functions, names, targetCapabilities),
                LowerExpression(assignment.Value, functions, names, targetCapabilities),
                assignment.Kind,
                assignment.UsePowerShellRuntimeErrors),
            PowerShellBoundClrMemberAssignmentStatement assignment => new PowerShellLoweredClrMemberAssignmentStatement(
                assignment.Span,
                assignment.Receiver is null ? null : LowerExpression(assignment.Receiver, functions, names, targetCapabilities),
                assignment.DeclaringType,
                assignment.MemberName,
                assignment.ReceiverBehavior,
                LowerExpression(assignment.Value, functions, names, targetCapabilities)),
            PowerShellBoundReturnStatement returned => new PowerShellLoweredReturnStatement(
                returned.Span,
                returned.Expression is null ? null : LowerExpression(returned.Expression, functions, names, targetCapabilities),
                returned.EmitsValue),
            PowerShellBoundExpressionStatement expression => LowerExpressionStatement(
                expression,
                functions,
                names,
                targetCapabilities),
            PowerShellBoundStreamWriteStatement stream => new PowerShellLoweredStreamWriteStatement(
                stream.Span,
                stream.Kind,
                stream.Provider,
                LowerExpression(stream.Message, functions, names, targetCapabilities)),
            PowerShellBoundCommandRegionStatement region => new PowerShellLoweredCommandRegionStatement(
                region.Span,
                region.HostedFallbackSource,
                region.Arguments.Select(static argument => new PowerShellLoweredCommandRegionArgument(argument.Symbol, argument.IsSwitch)).ToArray(),
                LowerCommandStages(region.Stages)),
            PowerShellBoundCommandCaptureStatement capture => LowerCommandCapture(capture, localTypes, declared),
            PowerShellBoundIfStatement conditional => new PowerShellLoweredIfStatement(
                conditional.Span,
                conditional.Clauses.Select(clause => new PowerShellLoweredConditionalClause(
                    LowerExpression(clause.Condition, functions, names, targetCapabilities),
                    LowerStatements(clause.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities))).ToArray(),
                conditional.ElseBlock is null ? null : LowerStatements(conditional.ElseBlock, functions, symbolTypes, localTypes, declared, names, targetCapabilities)),
            PowerShellBoundWhileStatement loop => new PowerShellLoweredWhileStatement(
                loop.Span,
                loop.Kind switch
                {
                    PowerShellBoundLoopKind.While => PowerShellLoweredLoopKind.While,
                    PowerShellBoundLoopKind.DoWhile => PowerShellLoweredLoopKind.DoWhile,
                    PowerShellBoundLoopKind.DoUntil => PowerShellLoweredLoopKind.DoUntil,
                    _ => throw new InvalidOperationException($"Unsupported bound loop kind '{loop.Kind}'.")
                },
                LowerExpression(loop.Condition, functions, names, targetCapabilities),
                LowerStatements(loop.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities)),
            PowerShellBoundForStatement loop => LowerFor(loop, functions, symbolTypes, localTypes, declared, names, targetCapabilities),
            PowerShellBoundForEachStatement loop => LowerForEach(loop, functions, symbolTypes, localTypes, declared, names, targetCapabilities),
            PowerShellBoundSwitchStatement switchStatement => new PowerShellLoweredSwitchStatement(
                switchStatement.Span,
                LowerExpression(switchStatement.Value, functions, names, targetCapabilities),
                switchStatement.Clauses.Select(clause => new PowerShellLoweredSwitchClause(
                    LowerExpression(clause.Value, functions, names, targetCapabilities),
                    LowerStatements(clause.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities))).ToArray(),
                switchStatement.DefaultBlock is null ? null : LowerStatements(switchStatement.DefaultBlock, functions, symbolTypes, localTypes, declared, names, targetCapabilities),
                switchStatement.MatchMode,
                switchStatement.CaseSensitive),
            PowerShellBoundThrowStatement thrown => new PowerShellLoweredThrowStatement(
                thrown.Span,
                thrown.Expression is null ? null : LowerExpression(thrown.Expression, functions, names, targetCapabilities)),
            PowerShellBoundTryStatement tryStatement => new PowerShellLoweredTryStatement(
                tryStatement.Span,
                LowerStatements(tryStatement.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities),
                tryStatement.Catches.Select(clause => new PowerShellLoweredCatchClause(
                    clause.ExceptionTypes.ToArray(),
                    LowerStatements(clause.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities))).ToArray(),
                tryStatement.FinallyBlock is null ? null : LowerStatements(tryStatement.FinallyBlock, functions, symbolTypes, localTypes, declared, names, targetCapabilities)),
            PowerShellBoundBreakStatement => new PowerShellLoweredBreakStatement(statement.Span),
            PowerShellBoundContinueStatement => new PowerShellLoweredContinueStatement(statement.Span),
            _ => throw new InvalidOperationException($"Bound statement '{statement.GetType().Name}' reached typed lowering without an owner.")
        };

    private static PowerShellLoweredStatement LowerExpressionStatement(
        PowerShellBoundExpressionStatement statement,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
    {
        var expression = LowerExpression(statement.Expression, functions, names, targetCapabilities);
        return statement.EmitsOutput && expression.ClrType != typeof(void)
            ? new PowerShellLoweredReturnStatement(statement.Span, expression, emitsValue: true)
            : new PowerShellLoweredExpressionStatement(
                statement.Span,
                expression,
                discardValue: !statement.EmitsOutput && expression.ClrType != typeof(void));
    }

    private static PowerShellLoweredForStatement LowerFor(
        PowerShellBoundForStatement loop,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
    {
        var declareInitializer = loop.Initializer is not null &&
                                 localTypes.ContainsKey(loop.Initializer.Target.StableKey) &&
                                 declared.Add(loop.Initializer.Target.StableKey);
        return new PowerShellLoweredForStatement(
            loop.Span,
            loop.Initializer is null ? null : (PowerShellLoweredMutationExpression)LowerExpression(loop.Initializer, functions, names, targetCapabilities),
            loop.Condition is null ? null : LowerExpression(loop.Condition, functions, names, targetCapabilities),
            loop.Iterator is null ? null : (PowerShellLoweredMutationExpression)LowerExpression(loop.Iterator, functions, names, targetCapabilities),
            LowerStatements(loop.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities),
            declareInitializer);
    }

    private static PowerShellLoweredForEachStatement LowerForEach(
        PowerShellBoundForEachStatement loop,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
    {
        declared.Add(loop.Variable.StableKey);
        return new PowerShellLoweredForEachStatement(
            loop.Span,
            loop.Variable,
            loop.ElementType,
            LowerExpression(loop.Collection, functions, names, targetCapabilities),
            loop.ScalarString,
            LowerStatements(loop.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities),
            loop.DeclareVariable,
            loop.NullCollectionElement is null
                ? null
                : LowerExpression(loop.NullCollectionElement, functions, names, targetCapabilities),
            loop.SystemArray);
    }

    private static PowerShellLoweredStatement[] LowerStatements(
        PowerShellBoundBlock block,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
        => block.Statements.Select(statement => LowerStatement(statement, functions, symbolTypes, localTypes, declared, names, targetCapabilities)).ToArray();

    private static PowerShellLoweredExpression LowerExpression(
        PowerShellBoundExpression expression,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
        => expression switch
        {
            PowerShellBoundLiteralExpression literal => new PowerShellLoweredLiteralExpression(literal.Span, literal.Type.ClrType, literal.Value),
            PowerShellBoundVariableExpression variable => new PowerShellLoweredVariableExpression(variable.Span, variable.Type.ClrType, variable.Symbol),
            PowerShellBoundConstantBooleanDelegateExpression booleanDelegate => new PowerShellLoweredConstantBooleanDelegateExpression(
                booleanDelegate.Span,
                booleanDelegate.Type.ClrType,
                booleanDelegate.ParameterTypes.ToArray(),
                Enumerable.Range(0, booleanDelegate.ParameterTypes.Length)
                    .Select(_ => names.Allocate("pf_delegate_argument"))
                    .ToArray(),
                booleanDelegate.Value),
            PowerShellBoundRuntimeStateExpression runtime => new PowerShellLoweredRuntimeStateExpression(
                runtime.Span,
                runtime.Type.ClrType,
                runtime.Kind,
                runtime.TargetFramework,
                runtime.SemanticProfileId,
                runtime.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                runtime.Provider),
            PowerShellBoundCommandAvailabilityExpression discovery => new PowerShellLoweredCommandAvailabilityExpression(
                discovery.Span,
                LowerExpression(discovery.Name, functions, names, targetCapabilities),
                discovery.ErrorAction,
                discovery.Provider),
            PowerShellBoundHostedBooleanCommandExpression hostedBoolean => new PowerShellLoweredHostedBooleanCommandExpression(
                hostedBoolean.Span,
                hostedBoolean.Provider,
                hostedBoolean.Arguments.Select(argument => new PowerShellLoweredHostedCommandArgument(
                    argument.ParameterName,
                    argument.Value is null ? null : LowerExpression(argument.Value, functions, names, targetCapabilities))).ToArray()),
            PowerShellBoundParameterPresenceExpression presence => new PowerShellLoweredParameterPresenceExpression(presence.Span, presence.ParameterName),
            PowerShellBoundConversionExpression conversion => new PowerShellLoweredConversionExpression(
                conversion.Span,
                conversion.Type.ClrType,
                LowerExpression(conversion.Operand, functions, names, targetCapabilities),
                conversion.UsePowerShellLanguageRuntime,
                conversion.UsePowerShellTruthiness),
            PowerShellBoundBinaryExpression binary => new PowerShellLoweredBinaryExpression(
                binary.Span,
                binary.Type.ClrType,
                binary.Operation,
                LowerExpression(binary.Left, functions, names, targetCapabilities),
                LowerExpression(binary.Right, functions, names, targetCapabilities),
                IsNullOrderedComparison(binary.Operation) ? names.Allocate("pf_null_order_left") : null,
                IsNullOrderedComparison(binary.Operation) ? names.Allocate("pf_null_order_right") : null),
            PowerShellBoundUnaryExpression unary => new PowerShellLoweredUnaryExpression(
                unary.Span,
                unary.Type.ClrType,
                unary.Operation,
                LowerExpression(unary.Operand, functions, names, targetCapabilities)),
            PowerShellBoundTypeTestExpression typeTest => new PowerShellLoweredTypeTestExpression(
                typeTest.Span,
                LowerExpression(typeTest.Operand, functions, names, targetCapabilities),
                typeTest.TargetType,
                typeTest.Negate),
            PowerShellBoundRegexExpression regex => new PowerShellLoweredRegexExpression(
                regex.Span,
                regex.Type.ClrType,
                regex.Operation,
                LowerExpression(regex.Input, functions, names, targetCapabilities),
                LowerExpression(regex.Pattern, functions, names, targetCapabilities),
                regex.Replacement is null ? null : LowerExpression(regex.Replacement, functions, names, targetCapabilities),
                regex.IgnoreCase),
            PowerShellBoundWildcardExpression wildcard => new PowerShellLoweredWildcardExpression(
                wildcard.Span,
                LowerExpression(wildcard.Input, functions, names, targetCapabilities),
                LowerExpression(wildcard.Pattern, functions, names, targetCapabilities),
                wildcard.IgnoreCase,
                wildcard.Negate,
                names.Allocate("pf_wildcard_left"),
                names.Allocate("pf_wildcard_right")),
            PowerShellBoundMembershipExpression membership => new PowerShellLoweredMembershipExpression(
                membership.Span,
                LowerExpression(membership.Left, functions, names, targetCapabilities),
                LowerExpression(membership.Right, functions, names, targetCapabilities),
                membership.ElementType,
                membership.CollectionOnRight,
                membership.IgnoreCase,
                membership.Negate,
                names.Allocate("pf_membership_left"),
                names.Allocate("pf_membership_right"),
                names.Allocate("pf_membership_item")),
            PowerShellBoundStringSplitExpression split => new PowerShellLoweredStringSplitExpression(
                split.Span,
                LowerExpression(split.Input, functions, names, targetCapabilities),
                LowerExpression(split.Pattern, functions, names, targetCapabilities),
                split.IgnoreCase),
            PowerShellBoundStringJoinExpression join => new PowerShellLoweredStringJoinExpression(
                join.Span,
                LowerExpression(join.Values, functions, names, targetCapabilities),
                LowerExpression(join.Separator, functions, names, targetCapabilities),
                names.Allocate("pf_join_left"),
                names.Allocate("pf_join_right")),
            PowerShellBoundInterpolatedStringExpression interpolated => new PowerShellLoweredInterpolatedStringExpression(
                interpolated.Span,
                interpolated.Parts.Select(part => new PowerShellLoweredInterpolatedStringPart(
                    part.Text,
                    part.Expression is null ? null : LowerExpression(part.Expression, functions, names, targetCapabilities))).ToArray()),
            PowerShellBoundMutationExpression mutation => new PowerShellLoweredMutationExpression(
                mutation.Span,
                mutation.Type.ClrType,
                mutation.Target,
                mutation.TargetClrType,
                mutation.Operation,
                mutation.Value is null ? null : LowerExpression(mutation.Value, functions, names, targetCapabilities),
                mutation.NormalizeNullString,
                mutation.CheckedIntegral),
            PowerShellBoundArrayExpression array => new PowerShellLoweredArrayExpression(
                array.Span,
                array.Type.ClrType,
                array.Kind,
                array.Elements.Select(element => LowerExpression(element, functions, names, targetCapabilities)).ToArray()),
            PowerShellBoundArrayConcatenationExpression concatenation => new PowerShellLoweredArrayConcatenationExpression(
                concatenation.Span,
                LowerExpression(concatenation.Left, functions, names, targetCapabilities),
                LowerExpression(concatenation.Right, functions, names, targetCapabilities),
                concatenation.EnumerateRight),
            PowerShellBoundDictionaryExpression dictionary => new PowerShellLoweredDictionaryExpression(
                dictionary.Span,
                dictionary.Type.ClrType,
                dictionary.Kind,
                dictionary.Entries.Select(entry => new PowerShellLoweredDictionaryEntry(
                    LowerExpression(entry.Key, functions, names, targetCapabilities),
                    LowerExpression(entry.Value, functions, names, targetCapabilities))).ToArray()),
            PowerShellBoundPowerShellObjectExpression powerShellObject => new PowerShellLoweredPowerShellObjectExpression(
                powerShellObject.Span,
                powerShellObject.Properties.Select(property => new PowerShellLoweredNoteProperty(
                    property.Name,
                    LowerExpression(property.Value, functions, names, targetCapabilities))).ToArray(),
                names.Allocate("object")),
            PowerShellBoundIndexExpression index => new PowerShellLoweredIndexExpression(
                index.Span,
                index.Type.ClrType,
                LowerExpression(index.Target, functions, names, targetCapabilities),
                LowerExpression(index.Index, functions, names, targetCapabilities),
                index.Kind,
                index.UsePowerShellRuntimeErrors),
            PowerShellBoundClrMemberExpression member => new PowerShellLoweredClrMemberExpression(
                member.Span,
                member.Type.ClrType,
                member.DeclaringType,
                member.MemberName,
                member.IsStatic,
                member.Receiver is null ? null : LowerExpression(member.Receiver, functions, names, targetCapabilities),
                member.ReceiverBehavior,
                member.ReceiverBehavior is PowerShellClrReceiverBehavior.DictionaryKeyLookup or PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback
                    ? names.Allocate("pf_dictionary")
                    : string.Empty,
                member.ReceiverBehavior is PowerShellClrReceiverBehavior.DictionaryKeyLookup or PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback
                    ? names.Allocate("pf_value")
                    : string.Empty),
            PowerShellBoundClrInvocationExpression invocation => new PowerShellLoweredClrInvocationExpression(
                invocation.Span,
                invocation.Type.ClrType,
                invocation.DeclaringType,
                invocation.MemberName,
                invocation.InvocationKind,
                invocation.Receiver is null ? null : LowerExpression(invocation.Receiver, functions, names, targetCapabilities),
                invocation.ReceiverBehavior,
                invocation.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                invocation.ParameterTypes.ToArray()),
            PowerShellBoundInvocationExpression invocation when functions.TryGetValue(invocation.Target.StableKey, out var target) =>
                new PowerShellLoweredInvocationExpression(
                    invocation.Span,
                    target.Function.ReturnType.ClrType,
                    invocation.Target,
                    invocation.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                    invocation.AuthoredEvaluationOrder.ToArray(),
                    invocation.BoundParameterNames.ToArray(),
                    CreateEvaluationTemporaryNames(invocation, names),
                    target.RequiresPowerShellBoundParameters,
                    target.RequiresPowerShellStreams,
                    target.RequiresProviderCancellation,
                    target.RequiresPowerShellCommandRegions,
                    target.RequiresPowerShellRuntimeState,
                    target.RequiresPowerShellModuleStateRead,
                    target.RequiresPowerShellModuleStateWrite),
            _ => throw new InvalidOperationException($"Bound expression '{expression.GetType().Name}' reached typed lowering without an owner.")
        };

    private static bool IsNullOrderedComparison(PowerShellBoundBinaryOperator operation)
        => operation is PowerShellBoundBinaryOperator.NullOrderedLessThan or
            PowerShellBoundBinaryOperator.NullOrderedLessThanOrEqual or
            PowerShellBoundBinaryOperator.NullOrderedGreaterThan or
            PowerShellBoundBinaryOperator.NullOrderedGreaterThanOrEqual;

    private static string?[] CreateEvaluationTemporaryNames(
        PowerShellBoundInvocationExpression invocation,
        LoweredNameAllocator names)
    {
        var result = new string?[invocation.Arguments.Length];
        if (invocation.AuthoredEvaluationOrder.SequenceEqual(invocation.AuthoredEvaluationOrder.OrderBy(static index => index)))
            return result;
        foreach (var parameterIndex in invocation.AuthoredEvaluationOrder)
            result[parameterIndex] = names.Allocate("pf_local_argument");
        return result;
    }

    private static PowerShellLoweredCommandCaptureStatement LowerCommandCapture(
        PowerShellBoundCommandCaptureStatement capture,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared)
        => new(
            capture.Span,
            capture.Target,
            capture.TargetType,
            localTypes.ContainsKey(capture.Target.StableKey) && declared.Add(capture.Target.StableKey),
            capture.HostedFallbackSource,
            capture.Arguments.Select(static argument => new PowerShellLoweredCommandRegionArgument(argument.Symbol, argument.IsSwitch)).ToArray(),
            LowerCommandStages(capture.Stages));

    private static PowerShellLoweredCommandStage[] LowerCommandStages(IEnumerable<PowerShellBoundCommandStage> stages)
        => stages.Select<PowerShellBoundCommandStage, PowerShellLoweredCommandStage>(static stage =>
        {
            var symbols = stage.PipelineSymbols.Select(static symbol => symbol.Symbol).ToArray();
            return stage switch
            {
                PowerShellBoundProjectionCommandStage => new PowerShellLoweredProjectionCommandStage(stage.Span, stage.Provider, symbols),
                PowerShellBoundFilteringCommandStage => new PowerShellLoweredFilteringCommandStage(stage.Span, stage.Provider, symbols),
                PowerShellBoundMappingCommandStage => new PowerShellLoweredMappingCommandStage(stage.Span, stage.Provider, symbols),
                PowerShellBoundSortingCommandStage => new PowerShellLoweredSortingCommandStage(stage.Span, stage.Provider, symbols),
                _ => new PowerShellLoweredHostedCommandStage(stage.Span, stage.Provider, symbols)
            };
        }).ToArray();

}
