namespace PowerForge;

/// <summary>
/// Selects typed CLR operations from analyzed bound nodes. It does not render target-language source.
/// </summary>
internal sealed class PowerShellTypedLowerer
{
    internal PowerShellLoweredProgram Lower(
        PowerShellBoundProgram program,
        PowerShellCompilationCapability targetCapabilities = PowerShellCompilationCapability.None)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        var diagnostics = new List<PowerShellSemanticDiagnostic>(program.Diagnostics);
        var functions = new List<PowerShellLoweredFunction>();
        var bySymbol = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
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
                    "PowerShell wildcard or membership semantics require the PowerShell language-operator target capability.",
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

            var statements = new List<PowerShellLoweredStatement>();
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var localTypes = function.Locals.ToDictionary(static local => local.Symbol.StableKey, static local => local.Type.ClrType, StringComparer.Ordinal);
            var symbolTypes = function.Parameters.ToDictionary(static parameter => parameter.Symbol.StableKey, static parameter => parameter.Type.ClrType, StringComparer.Ordinal);
            foreach (var local in function.Locals) symbolTypes[local.Symbol.StableKey] = local.Type.ClrType;
            var names = new LoweredNameAllocator(function.Parameters.Select(static parameter => parameter.Symbol.Name)
                .Concat(function.Locals.Select(static local => local.Symbol.Name)));
            var topLevelAssignments = function.Body.Statements.OfType<PowerShellBoundAssignmentStatement>()
                .Select(static assignment => assignment.Target.StableKey)
                .ToHashSet(StringComparer.Ordinal);
            var predeclared = EnumerateNestedAssignments(function.Body)
                .Where(localTypes.ContainsKey)
                .Where(key => !topLevelAssignments.Contains(key))
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
                PowerShellCSharpMethodEmitter.SanitizeIdentifier(function.Symbol.Name),
                function.ReturnType.ClrType,
                function.Parameters.Select(static parameter => new PowerShellLoweredParameter(parameter.Symbol, parameter.Type.ClrType, parameter.Contract)).ToArray(),
                function.Locals.Select(static local => new PowerShellLoweredLocal(local.Symbol, local.Type.ClrType)).ToArray(),
                function.Help,
                function.DeclaredOutputType,
                RequiresRuntimeStateHostBinding(function.Body),
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

    private static bool RequiresRuntimeStateHostBinding(PowerShellBoundBlock block)
        => block.Statements.Any(StatementRequiresRuntimeStateHostBinding);

    private static bool StatementRequiresRuntimeStateHostBinding(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => ExpressionRequiresRuntimeStateHostBinding(assignment.Value),
            PowerShellBoundIndexAssignmentStatement assignment =>
                ExpressionRequiresRuntimeStateHostBinding(assignment.Target) ||
                ExpressionRequiresRuntimeStateHostBinding(assignment.Index) ||
                ExpressionRequiresRuntimeStateHostBinding(assignment.Value),
            PowerShellBoundClrMemberAssignmentStatement assignment =>
                ExpressionRequiresRuntimeStateHostBinding(assignment.Receiver) ||
                ExpressionRequiresRuntimeStateHostBinding(assignment.Value),
            PowerShellBoundReturnStatement returned => returned.Expression is not null && ExpressionRequiresRuntimeStateHostBinding(returned.Expression),
            PowerShellBoundExpressionStatement expression => ExpressionRequiresRuntimeStateHostBinding(expression.Expression),
            PowerShellBoundIfStatement conditional => conditional.Clauses.Any(clause =>
                    ExpressionRequiresRuntimeStateHostBinding(clause.Condition) || RequiresRuntimeStateHostBinding(clause.Body)) ||
                (conditional.ElseBlock is not null && RequiresRuntimeStateHostBinding(conditional.ElseBlock)),
            PowerShellBoundWhileStatement loop => ExpressionRequiresRuntimeStateHostBinding(loop.Condition) || RequiresRuntimeStateHostBinding(loop.Body),
            PowerShellBoundForStatement loop =>
                (loop.Initializer is not null && ExpressionRequiresRuntimeStateHostBinding(loop.Initializer)) ||
                (loop.Condition is not null && ExpressionRequiresRuntimeStateHostBinding(loop.Condition)) ||
                (loop.Iterator is not null && ExpressionRequiresRuntimeStateHostBinding(loop.Iterator)) ||
                RequiresRuntimeStateHostBinding(loop.Body),
            PowerShellBoundForEachStatement loop => ExpressionRequiresRuntimeStateHostBinding(loop.Collection) || RequiresRuntimeStateHostBinding(loop.Body),
            PowerShellBoundSwitchStatement switchStatement =>
                ExpressionRequiresRuntimeStateHostBinding(switchStatement.Value) ||
                switchStatement.Clauses.Any(clause => ExpressionRequiresRuntimeStateHostBinding(clause.Value) || RequiresRuntimeStateHostBinding(clause.Body)) ||
                (switchStatement.DefaultBlock is not null && RequiresRuntimeStateHostBinding(switchStatement.DefaultBlock)),
            PowerShellBoundThrowStatement thrown => thrown.Expression is not null && ExpressionRequiresRuntimeStateHostBinding(thrown.Expression),
            PowerShellBoundTryStatement tryStatement =>
                RequiresRuntimeStateHostBinding(tryStatement.Body) ||
                tryStatement.Catches.Any(clause => RequiresRuntimeStateHostBinding(clause.Body)) ||
                (tryStatement.FinallyBlock is not null && RequiresRuntimeStateHostBinding(tryStatement.FinallyBlock)),
            _ => false
        };

    private static bool ExpressionRequiresRuntimeStateHostBinding(PowerShellBoundExpression expression)
        => expression switch
        {
            PowerShellBoundRuntimeStateExpression runtime => runtime.RequiresHostBinding,
            PowerShellBoundConversionExpression conversion => ExpressionRequiresRuntimeStateHostBinding(conversion.Operand),
            PowerShellBoundBinaryExpression binary => ExpressionRequiresRuntimeStateHostBinding(binary.Left) || ExpressionRequiresRuntimeStateHostBinding(binary.Right),
            PowerShellBoundUnaryExpression unary => ExpressionRequiresRuntimeStateHostBinding(unary.Operand),
            PowerShellBoundTypeTestExpression typeTest => ExpressionRequiresRuntimeStateHostBinding(typeTest.Operand),
            PowerShellBoundRegexExpression regex =>
                ExpressionRequiresRuntimeStateHostBinding(regex.Input) ||
                ExpressionRequiresRuntimeStateHostBinding(regex.Pattern) ||
                (regex.Replacement is not null && ExpressionRequiresRuntimeStateHostBinding(regex.Replacement)),
            PowerShellBoundWildcardExpression wildcard => ExpressionRequiresRuntimeStateHostBinding(wildcard.Input) || ExpressionRequiresRuntimeStateHostBinding(wildcard.Pattern),
            PowerShellBoundMembershipExpression membership => ExpressionRequiresRuntimeStateHostBinding(membership.Left) || ExpressionRequiresRuntimeStateHostBinding(membership.Right),
            PowerShellBoundMutationExpression mutation => mutation.Value is not null && ExpressionRequiresRuntimeStateHostBinding(mutation.Value),
            PowerShellBoundArrayExpression array => array.Elements.Any(ExpressionRequiresRuntimeStateHostBinding),
            PowerShellBoundDictionaryExpression dictionary => dictionary.Entries.Any(entry =>
                ExpressionRequiresRuntimeStateHostBinding(entry.Key) || ExpressionRequiresRuntimeStateHostBinding(entry.Value)),
            PowerShellBoundIndexExpression index => ExpressionRequiresRuntimeStateHostBinding(index.Target) || ExpressionRequiresRuntimeStateHostBinding(index.Index),
            PowerShellBoundClrMemberExpression member => member.Receiver is not null && ExpressionRequiresRuntimeStateHostBinding(member.Receiver),
            PowerShellBoundClrInvocationExpression invocation =>
                (invocation.Receiver is not null && ExpressionRequiresRuntimeStateHostBinding(invocation.Receiver)) ||
                invocation.Arguments.Any(ExpressionRequiresRuntimeStateHostBinding),
            PowerShellBoundInvocationExpression invocation => invocation.Arguments.Any(ExpressionRequiresRuntimeStateHostBinding),
            _ => false
        };

    private static IEnumerable<string> EnumerateNestedAssignments(PowerShellBoundBlock block)
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
                foreach (var assignment in EnumerateAssignments(forLoop.Body)) yield return assignment;
            }
            else if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
                foreach (var assignment in EnumerateAssignments(forEachLoop.Body).Where(key => key != forEachLoop.Variable.StableKey)) yield return assignment;
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

    private static IEnumerable<string> EnumerateAssignments(PowerShellBoundBlock block)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundAssignmentStatement assignment) yield return assignment.Target.StableKey;
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
                foreach (var nested in EnumerateAssignments(forLoop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
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
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
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
            PowerShellBoundIndexAssignmentStatement assignment => new PowerShellLoweredIndexAssignmentStatement(
                assignment.Span,
                LowerExpression(assignment.Target, functions, names, targetCapabilities),
                LowerExpression(assignment.Index, functions, names, targetCapabilities),
                LowerExpression(assignment.Value, functions, names, targetCapabilities),
                assignment.Kind),
            PowerShellBoundClrMemberAssignmentStatement assignment => new PowerShellLoweredClrMemberAssignmentStatement(
                assignment.Span,
                LowerExpression(assignment.Receiver, functions, names, targetCapabilities),
                assignment.DeclaringType,
                assignment.MemberName,
                LowerExpression(assignment.Value, functions, names, targetCapabilities)),
            PowerShellBoundReturnStatement returned => new PowerShellLoweredReturnStatement(
                returned.Span,
                returned.Expression is null ? null : LowerExpression(returned.Expression, functions, names, targetCapabilities),
                returned.EmitsValue),
            PowerShellBoundExpressionStatement { EmitsOutput: true } expression => new PowerShellLoweredReturnStatement(
                expression.Span,
                LowerExpression(expression.Expression, functions, names, targetCapabilities),
                emitsValue: true),
            PowerShellBoundExpressionStatement expression => new PowerShellLoweredExpressionStatement(
                expression.Span,
                LowerExpression(expression.Expression, functions, names, targetCapabilities)),
            PowerShellBoundIfStatement conditional => new PowerShellLoweredIfStatement(
                conditional.Span,
                conditional.Clauses.Select(clause => new PowerShellLoweredConditionalClause(
                    LowerExpression(clause.Condition, functions, names, targetCapabilities),
                    LowerStatements(clause.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities))).ToArray(),
                conditional.ElseBlock is null ? null : LowerStatements(conditional.ElseBlock, functions, symbolTypes, localTypes, declared, names, targetCapabilities)),
            PowerShellBoundWhileStatement loop => new PowerShellLoweredWhileStatement(
                loop.Span,
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
                switchStatement.CaseSensitive),
            PowerShellBoundThrowStatement thrown => new PowerShellLoweredThrowStatement(
                thrown.Span,
                thrown.Expression is null ? null : LowerExpression(thrown.Expression, functions, names, targetCapabilities)),
            PowerShellBoundTryStatement tryStatement => new PowerShellLoweredTryStatement(
                tryStatement.Span,
                LowerStatements(tryStatement.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities),
                tryStatement.Catches.Select(clause => new PowerShellLoweredCatchClause(
                    clause.ExceptionTypes,
                    LowerStatements(clause.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities))).ToArray(),
                tryStatement.FinallyBlock is null ? null : LowerStatements(tryStatement.FinallyBlock, functions, symbolTypes, localTypes, declared, names, targetCapabilities)),
            PowerShellBoundBreakStatement => new PowerShellLoweredBreakStatement(statement.Span),
            PowerShellBoundContinueStatement => new PowerShellLoweredContinueStatement(statement.Span),
            _ => throw new InvalidOperationException($"Bound statement '{statement.GetType().Name}' reached typed lowering without an owner.")
        };

    private static PowerShellLoweredForStatement LowerFor(
        PowerShellBoundForStatement loop,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
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
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
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
            LowerStatements(loop.Body, functions, symbolTypes, localTypes, declared, names, targetCapabilities));
    }

    private static PowerShellLoweredStatement[] LowerStatements(
        PowerShellBoundBlock block,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
        => block.Statements.Select(statement => LowerStatement(statement, functions, symbolTypes, localTypes, declared, names, targetCapabilities)).ToArray();

    private static PowerShellLoweredExpression LowerExpression(
        PowerShellBoundExpression expression,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
        => expression switch
        {
            PowerShellBoundLiteralExpression literal => new PowerShellLoweredLiteralExpression(literal.Span, literal.Type.ClrType, literal.Value),
            PowerShellBoundVariableExpression variable => new PowerShellLoweredVariableExpression(variable.Span, variable.Type.ClrType, variable.Symbol),
            PowerShellBoundRuntimeStateExpression runtime => new PowerShellLoweredRuntimeStateExpression(
                runtime.Span,
                runtime.Type.ClrType,
                runtime.Kind,
                runtime.TargetFramework,
                runtime.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray()),
            PowerShellBoundConversionExpression conversion => new PowerShellLoweredConversionExpression(
                conversion.Span,
                conversion.Type.ClrType,
                LowerExpression(conversion.Operand, functions, names, targetCapabilities)),
            PowerShellBoundBinaryExpression binary => new PowerShellLoweredBinaryExpression(
                binary.Span,
                binary.Type.ClrType,
                binary.Operation,
                LowerExpression(binary.Left, functions, names, targetCapabilities),
                LowerExpression(binary.Right, functions, names, targetCapabilities)),
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
                names.Allocate("wildcard_left"),
                names.Allocate("wildcard_right")),
            PowerShellBoundMembershipExpression membership => new PowerShellLoweredMembershipExpression(
                membership.Span,
                LowerExpression(membership.Left, functions, names, targetCapabilities),
                LowerExpression(membership.Right, functions, names, targetCapabilities),
                membership.ElementType,
                membership.CollectionOnRight,
                membership.IgnoreCase,
                membership.Negate,
                names.Allocate("membership_left"),
                names.Allocate("membership_right"),
                names.Allocate("membership_item")),
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
            PowerShellBoundDictionaryExpression dictionary => new PowerShellLoweredDictionaryExpression(
                dictionary.Span,
                dictionary.Type.ClrType,
                dictionary.Kind,
                dictionary.Entries.Select(entry => new PowerShellLoweredDictionaryEntry(
                    LowerExpression(entry.Key, functions, names, targetCapabilities),
                    LowerExpression(entry.Value, functions, names, targetCapabilities))).ToArray()),
            PowerShellBoundIndexExpression index => new PowerShellLoweredIndexExpression(
                index.Span,
                index.Type.ClrType,
                LowerExpression(index.Target, functions, names, targetCapabilities),
                LowerExpression(index.Index, functions, names, targetCapabilities),
                index.Kind),
            PowerShellBoundClrMemberExpression member => new PowerShellLoweredClrMemberExpression(
                member.Span,
                member.Type.ClrType,
                member.DeclaringType,
                member.MemberName,
                member.IsStatic,
                member.Receiver is null ? null : LowerExpression(member.Receiver, functions, names, targetCapabilities),
                member.ReceiverBehavior),
            PowerShellBoundClrInvocationExpression invocation => new PowerShellLoweredClrInvocationExpression(
                invocation.Span,
                invocation.Type.ClrType,
                invocation.DeclaringType,
                invocation.MemberName,
                invocation.InvocationKind,
                invocation.Receiver is null ? null : LowerExpression(invocation.Receiver, functions, names, targetCapabilities),
                invocation.ReceiverBehavior,
                invocation.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                invocation.ParameterTypes),
            PowerShellBoundInvocationExpression invocation when functions.TryGetValue(invocation.Target.StableKey, out var target) =>
                new PowerShellLoweredInvocationExpression(
                    invocation.Span,
                    target.ReturnType.ClrType,
                    invocation.Target,
                    invocation.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                    invocation.AuthoredEvaluationOrder,
                    invocation.BoundParameterNames,
                    CreateEvaluationTemporaryNames(invocation, names),
                    target.Parameters.Any(parameter =>
                        parameter.Contract.DefaultValue is not null ||
                        !parameter.Contract.IsMandatory && parameter.Contract.Validations.Length > 0 && targetCapabilities.HasFlag(PowerShellCompilationCapability.BoundParameters))),
            _ => throw new InvalidOperationException($"Bound expression '{expression.GetType().Name}' reached typed lowering without an owner.")
        };

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

    private sealed class LoweredNameAllocator
    {
        private readonly HashSet<string> _used;
        private int _index;

        internal LoweredNameAllocator(IEnumerable<string> authoredNames)
        {
            _used = authoredNames.Select(PowerShellClrSymbolMapper.MapIdentifier).ToHashSet(StringComparer.Ordinal);
        }

        internal string Allocate(string prefix)
        {
            string candidate;
            do { candidate = $"__{prefix}_{_index++}"; } while (!_used.Add(candidate));
            return candidate;
        }
    }
}
