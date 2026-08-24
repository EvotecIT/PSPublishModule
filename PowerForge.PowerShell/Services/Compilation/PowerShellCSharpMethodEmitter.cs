using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private readonly string _filePath;
    private readonly ScriptBlockAst _body;
    private readonly string _sourceName;
    private readonly string _generatedName;
    private readonly StatementAst[]? _statements;
    private readonly Dictionary<string, Type> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _variableIdentifiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _firstAssignmentOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Start, int End)> _loopScopedVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _explicitlyTypedVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _declaredLocals = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _predeclaredLocals = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _scalarForeachLoops = new();
    private readonly StringBuilder _builder = new();
    private readonly PowerShellCSharpMemberEmitter _memberEmitter;
    private readonly string? _targetFramework;
    private readonly PowerShellCompilationCapability _capabilities;
    private readonly IReadOnlyDictionary<string, PowerShellLocalFunctionSignature> _localFunctions;
    private bool _requiresPowerShellStreams;
    private bool _requiresPowerShellCommandRegions;
    private int _indent = 1;
    private int _switchIndex;

    internal PowerShellCSharpMethodEmitter(
        string filePath,
        FunctionDefinitionAst function,
        string? targetFramework = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None,
        IReadOnlyDictionary<string, PowerShellLocalFunctionSignature>? localFunctions = null)
        : this(filePath, function.Body, function.Name, SanitizeIdentifier(function.Name), null, targetFramework, capabilities, localFunctions, initialize: true)
    {
    }

    internal PowerShellCSharpMethodEmitter(
        string filePath,
        ScriptBlockAst body,
        string sourceName,
        string generatedName,
        StatementAst[] statements,
        string? targetFramework = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None,
        IReadOnlyDictionary<string, PowerShellLocalFunctionSignature>? localFunctions = null)
        : this(filePath, body, sourceName, SanitizeIdentifier(generatedName), statements, targetFramework, capabilities, localFunctions, initialize: true)
    {
    }

    private PowerShellCSharpMethodEmitter(
        string filePath,
        ScriptBlockAst body,
        string sourceName,
        string generatedName,
        StatementAst[]? statements,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        IReadOnlyDictionary<string, PowerShellLocalFunctionSignature>? localFunctions,
        bool initialize)
    {
        _filePath = filePath;
        _body = body;
        _sourceName = sourceName;
        _generatedName = generatedName;
        _statements = statements;
        _targetFramework = targetFramework;
        _capabilities = capabilities;
        _localFunctions = localFunctions ?? new Dictionary<string, PowerShellLocalFunctionSignature>(StringComparer.OrdinalIgnoreCase);
        _memberEmitter = new PowerShellCSharpMemberEmitter(
            InferExpressionType,
            EmitExpression,
            CanAssign,
            GetTypeName,
            type => PowerShellGeneratedTypePolicy.IsSupported(type, _targetFramework),
            member => string.IsNullOrWhiteSpace(_targetFramework) || PowerShellGeneratedMemberPolicy.IsSupported(member, _targetFramework!),
            CanNormalizeNullStringReceiver,
            Error);
    }

    internal PowerShellCSharpMethodEmission Emit()
    {
        var paramBlock = _body.ParamBlock;
        var parameters = paramBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>();
        foreach (var parameter in parameters)
        {
            var name = parameter.Name.VariablePath.UserPath;
            var parameterType = GetCompiledParameterType(parameter);
            if (!PowerShellGeneratedTypePolicy.IsSupported(parameterType, _targetFramework))
                throw Error(parameter, $"Parameter '${name}' uses CLR type '{parameterType.FullName}' outside the generated project reference set.");
            if (_variables.ContainsKey(name))
                throw Error(parameter, $"Parameter '${name}' duplicates another parameter under PowerShell's case-insensitive naming rules.");
            var identifier = SanitizeIdentifier(name);
            if (_variableIdentifiers.Values.Contains(identifier, StringComparer.Ordinal))
                throw Error(parameter, $"Parameter '${name}' collides with another parameter after CLR identifier normalization.");
            _variables.Add(name, parameterType);
            _variableIdentifiers.Add(name, identifier);
            _explicitlyTypedVariables.Add(name);
        }

        var statements = _statements ?? _body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
        _requiresPowerShellStreams = _capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
            statements.SelectMany(static statement => statement.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false))
                .OfType<CommandAst>()
                .Any(static command => PowerShellCommandIslandPolicy.TryGetStreamCommand(command, out _, out _));
        _requiresPowerShellCommandRegions = _capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
            statements.Any(statement => PowerShellCommandIslandPolicy.IsRuntimeRegion(statement, _body));
        InferLocalTypes(statements);
        ValidateVariableReferences(statements);
        var returnType = InferReturnType(statements);
        if (returnType != typeof(void) && !HasTerminalValue(statements))
            throw Error(_body, $"Typed non-void unit '{_sourceName}' must end with an explicit return statement on the conservative compilation path.");
        var parameterParts = parameters.Select(parameter =>
            $"{GetTypeName(GetCompiledParameterType(parameter))} {SanitizeIdentifier(parameter.Name.VariablePath.UserPath)}").ToList();
        if (_requiresPowerShellStreams)
        {
            parameterParts.Add("global::System.Action<string> __writeVerbose");
            parameterParts.Add("global::System.Action<string> __writeDebug");
            parameterParts.Add("global::System.Action<string> __writeWarning");
        }
        if (_requiresPowerShellCommandRegions)
            parameterParts.Add("global::System.Action<string, object?[]> __invokePowerShellRegion");
        var parameterSource = string.Join(", ", parameterParts);

        AppendLine($"public static {GetTypeName(returnType)} {_generatedName}({parameterSource})");
        AppendLine("{");
        _indent++;
        AppendLine("checked");
        AppendLine("{");
        _indent++;
        foreach (var parameter in parameters.Where(static parameter => GetCompiledParameterType(parameter) == typeof(string)))
        {
            var identifier = GetVariableIdentifier(parameter.Name.VariablePath.UserPath);
            AppendLine($"{identifier} = {identifier} ?? string.Empty;");
        }
        foreach (var name in _predeclaredLocals.OrderBy(name => _firstAssignmentOffsets[name]))
        {
            AppendLine($"{GetTypeName(_variables[name])} {GetVariableIdentifier(name)} = default!;");
            _declaredLocals.Add(name);
        }
        for (var index = 0; index < statements.Length; index++)
        {
            if (_requiresPowerShellCommandRegions && PowerShellCommandIslandPolicy.IsRuntimeRegion(statements[index], _body))
            {
                var region = new List<StatementAst> { statements[index] };
                while (index + 1 < statements.Length &&
                       PowerShellCommandIslandPolicy.IsRuntimeRegion(statements[index + 1], _body))
                {
                    region.Add(statements[index + 1]);
                    index++;
                }
                EmitRuntimeRegion(region);
                continue;
            }
            EmitStatement(statements[index], returnType, allowImplicitReturn: index == statements.Length - 1);
        }
        _indent--;
        AppendLine("}");
        _indent--;
        AppendLine("}");

        return new PowerShellCSharpMethodEmission(
            _generatedName,
            returnType,
            _builder.ToString().TrimEnd(),
            _requiresPowerShellStreams,
            _requiresPowerShellCommandRegions);
    }

    private void EmitRuntimeRegion(IReadOnlyList<StatementAst> statements)
    {
        var parameters = _body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>();
        var parameterBlock = "param(" + string.Join(", ", parameters.Select(static parameter => parameter.Name.Extent.Text)) + ")";
        var script = parameterBlock + Environment.NewLine + string.Join(Environment.NewLine, statements.Select(static statement => statement.Extent.Text));
        var arguments = string.Join(", ", parameters.Select(parameter => GetVariableIdentifier(parameter.Name.VariablePath.UserPath)));
        AppendLine($"__invokePowerShellRegion({PowerShellCSharpLiteral.QuoteString(script)}, new object?[] {{ {arguments} }});");
    }

    private static Type GetCompiledParameterType(ParameterAst parameter)
        => parameter.StaticType == typeof(System.Management.Automation.SwitchParameter)
            ? typeof(bool)
            : parameter.StaticType;

    private void InferLocalTypes(IEnumerable<StatementAst> statements)
    {
        var assignments = statements
            .SelectMany(static statement => statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: false))
            .Cast<AssignmentStatementAst>()
            .OrderBy(static assignment => assignment.Extent.StartOffset)
            .ToArray();
        foreach (var assignment in assignments.Where(static assignment => !HasAncestor<ForEachStatementAst>(assignment)))
            InferAssignmentType(assignment);

        var loops = statements
            .SelectMany(static statement => statement.FindAll(static node => node is ForEachStatementAst, searchNestedScriptBlocks: false))
            .Cast<ForEachStatementAst>();
        foreach (var loop in loops)
        {
            var collectionType = InferExpressionType(loop.Condition);
            var elementType = collectionType.IsArray ? collectionType.GetElementType() : CanUseScalarStringForeach(loop.Condition) ? typeof(string) : null;
            if (elementType is null)
                throw Error(loop.Condition, "foreach currently requires a statically typed one-dimensional array or scalar string.");
            var name = loop.Variable.VariablePath.UserPath;
            if (_variables.ContainsKey(name))
                throw Error(loop.Variable, $"foreach variable '${name}' cannot reuse another function-scope variable on the conservative compilation path.");
            _variables[name] = elementType;
            if (!collectionType.IsArray)
                _scalarForeachLoops.Add(loop.Extent.StartOffset);
            AddVariableIdentifier(name, loop.Variable);
            _firstAssignmentOffsets[name] = loop.Extent.StartOffset;
            _loopScopedVariables[name] = (loop.Extent.StartOffset, loop.Extent.EndOffset);
        }

        foreach (var assignment in assignments.Where(static assignment => HasAncestor<ForEachStatementAst>(assignment)))
            InferAssignmentType(assignment);
    }

    private void InferAssignmentType(AssignmentStatementAst assignment)
    {
        var variable = FindAssignedVariable(assignment.Left);
        if (variable is null)
            throw Error(assignment.Left, "Only local-variable assignment can be translated to typed CLR code.");
        if (PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(variable.VariablePath.UserPath))
            throw Error(assignment.Left, $"Assignment to read-only automatic variable '${variable.VariablePath.UserPath}' cannot be translated to typed CLR code.");

        var name = variable.VariablePath.UserPath;
        var rightType = InferExpressionType(assignment.Right);
        if (rightType == typeof(void))
            throw Error(assignment.Right, $"Assignment to '${name}' uses a void CLR invocation whose PowerShell null result cannot be represented by an inferred CLR local.");
        var declaredType = assignment.Left is ConvertExpressionAst conversion
            ? conversion.StaticType
            : rightType;
        if (!PowerShellGeneratedTypePolicy.IsSupported(declaredType, _targetFramework))
            throw Error(assignment.Left, $"Local '${name}' uses CLR type '{declaredType.FullName}' outside the generated project reference set.");
        if (!CanAssign(declaredType, rightType))
            throw Error(assignment, $"Assignment requires PowerShell conversion from '{rightType.FullName}' to '{declaredType.FullName}', which is not an implicit CLR conversion.");
        if (_variables.TryGetValue(name, out var existingType))
        {
            if (assignment.Parent is ForStatementAst parentFor &&
                _loopScopedVariables.TryGetValue(name, out var declaredLoop) &&
                (declaredLoop.Start != parentFor.Extent.StartOffset || declaredLoop.End != parentFor.Extent.EndOffset))
                throw Error(assignment, $"Loop-local '${name}' cannot be redeclared in another for loop on the conservative compilation path.");
            if (assignment.Left is ConvertExpressionAst && existingType != declaredType)
                throw Error(assignment, $"Assignment changes the explicit type of '${name}' from '{existingType.FullName}' to '{declaredType.FullName}'.");
            if (assignment.Operator.ToString() != "Equals")
            {
                if (!PowerShellCSharpOperatorPolicy.SupportsCompoundAssignment(assignment.Operator.ToString(), existingType, rightType))
                    throw Error(assignment, $"Compound assignment '{assignment.Operator}' is not defined for CLR types '{existingType.FullName}' and '{rightType.FullName}' on the conservative compilation path.");
                return;
            }
            if (!CanAssign(existingType, rightType))
                throw Error(assignment, $"Assignment changes '${name}' from '{existingType.FullName}' to incompatible type '{rightType.FullName}'.");
            return;
        }

        if (assignment.Operator.ToString() != "Equals")
            throw Error(assignment, $"Compound assignment to undeclared local '${name}' is not eligible for typed compilation.");

        if (assignment.Parent is not NamedBlockAst && assignment.Parent is not ForStatementAst && assignment.Parent is not IfStatementAst)
            throw Error(assignment, $"Local '${name}' must be declared at function scope or in a for initializer before it can be compiled safely.");
        if (assignment.Parent is IfStatementAst && IsNonNullableValueType(declaredType))
            throw Error(assignment, $"Conditionally assigned value-type local '${name}' may remain unassigned on a reachable PowerShell path and cannot be predeclared with a CLR default.");

        _variables.Add(name, declaredType);
        AddVariableIdentifier(name, variable);
        _firstAssignmentOffsets.Add(name, assignment.Extent.StartOffset);
        if (assignment.Parent is ForStatementAst forStatement)
            _loopScopedVariables[name] = (forStatement.Extent.StartOffset, forStatement.Extent.EndOffset);
        if (assignment.Parent is IfStatementAst)
            _predeclaredLocals.Add(name);
        if (assignment.Left is ConvertExpressionAst)
            _explicitlyTypedVariables.Add(name);
    }

    private static bool HasAncestor<TAst>(Ast node) where TAst : Ast
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TAst) return true;
        }
        return false;
    }

    private static bool HasBreakableAncestor(Ast node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ForStatementAst or WhileStatementAst or ForEachStatementAst or SwitchStatementAst)
                return true;
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst)
                return false;
        }
        return false;
    }

    private static bool HasLoopAncestor(Ast node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ForStatementAst or WhileStatementAst or ForEachStatementAst)
                return true;
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst)
                return false;
        }
        return false;
    }

    private static bool HasContinuableAncestor(Ast node)
        => HasLoopAncestor(node) || HasAncestor<SwitchStatementAst>(node);

    private void ValidateVariableReferences(IEnumerable<StatementAst> statements)
    {
        foreach (var variable in statements
                     .SelectMany(static statement => statement.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: false))
                     .Cast<VariableExpressionAst>())
        {
            var name = variable.VariablePath.UserPath;
            if (name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                _body.ParamBlock?.Parameters.Any(parameter => parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) == true)
                continue;
            if (_firstAssignmentOffsets.TryGetValue(name, out var firstAssignment) && variable.Extent.StartOffset < firstAssignment)
                throw Error(variable, $"Local '${name}' is read before its first assignment; that relies on dynamic PowerShell null semantics.");
            if (_loopScopedVariables.TryGetValue(name, out var loopExtent) &&
                (variable.Extent.StartOffset < loopExtent.Start || variable.Extent.EndOffset > loopExtent.End))
                throw Error(variable, $"Loop-local '${name}' is used outside the loop scope that the generated CLR code can preserve.");
        }

        foreach (var assignment in statements
                     .SelectMany(static statement => statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: false))
                     .Cast<AssignmentStatementAst>())
        {
            var variable = FindAssignedVariable(assignment.Left);
            if (variable is null || !_firstAssignmentOffsets.TryGetValue(variable.VariablePath.UserPath, out var firstAssignment) || firstAssignment != assignment.Extent.StartOffset)
                continue;
            if (assignment.Right.FindAll(
                    node => node is VariableExpressionAst reference && reference.VariablePath.UserPath.Equals(variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase),
                    searchNestedScriptBlocks: false).Any())
                throw Error(assignment, $"Local '${variable.VariablePath.UserPath}' reads its dynamic pre-assignment value in its first assignment.");
        }
    }

    private void AddVariableIdentifier(string name, Ast node)
    {
        var identifier = SanitizeIdentifier(name);
        if (_variableIdentifiers.Values.Contains(identifier, StringComparer.Ordinal))
            throw Error(node, $"Variable '${name}' collides with another variable after CLR identifier normalization.");
        _variableIdentifiers[name] = identifier;
    }

    private Type InferReturnType(IEnumerable<StatementAst> statements)
    {
        var returns = statements
            .SelectMany(static statement => statement.FindAll(static node => node is ReturnStatementAst, searchNestedScriptBlocks: false))
            .Cast<ReturnStatementAst>()
            .ToArray();
        Type? result = null;
        foreach (var statement in returns)
        {
            var current = statement.Pipeline is null ? typeof(void) : InferExpressionType(statement.Pipeline);
            if (result is not null && result != current)
                throw Error(statement, $"Return type '{current.FullName}' differs from earlier return type '{result.FullName}'; preserving PowerShell's branch-specific runtime types requires fallback.");
            result ??= current;
        }

        var terminal = statements.LastOrDefault();
        if (terminal is PipelineAst { PipelineElements.Count: 1 } terminalPipeline &&
            (terminalPipeline.PipelineElements[0] is CommandExpressionAst || IsLocalFunctionPipeline(terminalPipeline)))
        {
            var current = InferExpressionType(terminal);
            if (current != typeof(void))
                result = result is null ? current : UnifyTypes(result, current, terminal);
        }

        return result ?? typeof(void);
    }

    private void EmitStatement(StatementAst statement, Type returnType, bool allowImplicitReturn = false)
    {
        switch (statement)
        {
            case AssignmentStatementAst assignment:
                EmitAssignment(assignment, terminate: true);
                return;
            case ReturnStatementAst returnStatement:
                if (returnStatement.Pipeline is null)
                    AppendLine("return;");
                else if (InferExpressionType(returnStatement.Pipeline) == typeof(void))
                {
                    AppendLine($"{EmitExpression(UnwrapTransparentExpression(returnStatement.Pipeline))};");
                    AppendLine("return;");
                }
                else
                    AppendLine($"return {EmitExpression(returnStatement.Pipeline)};");
                return;
            case IfStatementAst ifStatement:
                EmitIf(ifStatement, returnType);
                return;
            case SwitchStatementAst switchStatement:
                EmitSwitch(switchStatement, returnType);
                return;
            case ForStatementAst forStatement:
                EmitFor(forStatement, returnType);
                return;
            case WhileStatementAst whileStatement:
                EmitWhile(whileStatement, returnType);
                return;
            case ForEachStatementAst forEachStatement:
                EmitForEach(forEachStatement, returnType);
                return;
            case TryStatementAst tryStatement:
                EmitTry(tryStatement, returnType);
                return;
            case BreakStatementAst breakStatement when breakStatement.Label is not null:
                throw Error(breakStatement, "Labeled break is not supported by the typed compiler.");
            case BreakStatementAst breakStatement when !HasBreakableAncestor(breakStatement):
                throw Error(breakStatement, "break must be inside a supported loop or scalar switch.");
            case BreakStatementAst:
                AppendLine("break;");
                return;
            case ContinueStatementAst continueStatement when continueStatement.Label is not null:
                throw Error(continueStatement, "Labeled continue is not supported by the typed compiler.");
            case ContinueStatementAst continueStatement when !HasContinuableAncestor(continueStatement):
                throw Error(continueStatement, "continue must be inside a supported loop or scalar switch.");
            case ContinueStatementAst:
                AppendLine("continue;");
                return;
            case PipelineAst pipeline when
                _requiresPowerShellStreams &&
                pipeline.PipelineElements.Count == 1 &&
                pipeline.PipelineElements[0] is CommandAst command &&
                PowerShellCommandIslandPolicy.TryGetStreamCommand(command, out var streamKind, out var message):
                var sink = streamKind switch
                {
                    PowerShellStreamCommandKind.Verbose => "__writeVerbose",
                    PowerShellStreamCommandKind.Debug => "__writeDebug",
                    PowerShellStreamCommandKind.Warning => "__writeWarning",
                    _ => throw Error(command, "Unsupported PowerShell stream command island.")
                };
                AppendLine($"{sink}(global::System.Convert.ToString({EmitExpression(message)}, global::System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty);");
                return;
            case PipelineAst pipeline when IsLocalFunctionPipeline(pipeline):
                var localType = InferLocalFunctionType(pipeline);
                var localCall = EmitLocalFunctionCall(pipeline);
                if (localType == typeof(void))
                {
                    AppendLine($"{localCall};");
                    return;
                }
                if (allowImplicitReturn && returnType == localType)
                {
                    AppendLine($"return {localCall};");
                    return;
                }
                throw Error(pipeline, "A value-producing local function call is supported only when returned, assigned, or used as the terminal typed value.");
            case PipelineAst pipeline when pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandExpressionAst:
                var expressionType = InferExpressionType(pipeline);
                if (expressionType == typeof(void))
                {
                    AppendLine($"{EmitExpression(pipeline)};");
                    return;
                }
                if (allowImplicitReturn && returnType == expressionType)
                {
                    AppendLine($"return {EmitExpression(pipeline)};");
                    return;
                }
                throw Error(pipeline, "Implicit PowerShell pipeline output is supported only for the terminal typed value.");
            default:
                throw Error(statement, $"Statement '{statement.GetType().Name}' is not implemented by the C# emitter.");
        }
    }

    private void EmitAssignment(AssignmentStatementAst assignment, bool terminate)
    {
        var variable = FindAssignedVariable(assignment.Left)
            ?? throw Error(assignment.Left, "Only local-variable assignment is supported.");
        var name = variable.VariablePath.UserPath;
        var identifier = GetVariableIdentifier(name);
        var isParameter = _body.ParamBlock?.Parameters.Any(parameter => parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;
        var declaration = !_declaredLocals.Contains(name) && !isParameter;
        var left = declaration ? $"{GetTypeName(_variables[name])} {identifier}" : identifier;
        var operation = assignment.Operator.ToString() switch
        {
            "Equals" => "=",
            "PlusEquals" => "+=",
            "MinusEquals" => "-=",
            "MultiplyEquals" => "*=",
            "DivideEquals" => "/=",
            "RemEquals" => "%=",
            _ => throw Error(assignment, $"Assignment operator '{assignment.Operator}' is not implemented.")
        };
        var rightType = InferExpressionType(assignment.Right);
        if (operation != "=" && !PowerShellCSharpOperatorPolicy.SupportsCompoundAssignment(assignment.Operator.ToString(), _variables[name], rightType))
            throw Error(assignment, $"Compound assignment '{assignment.Operator}' is not defined for CLR types '{_variables[name].FullName}' and '{rightType.FullName}' on the conservative compilation path.");
        if (operation != "=" && IsIntegral(_variables[name]) && !_explicitlyTypedVariables.Contains(name))
            throw Error(assignment, $"Integral compound assignment to untyped local '${name}' can promote dynamically in PowerShell and is not eligible for typed compilation.");
        _declaredLocals.Add(name);
        var suffix = terminate ? ";" : string.Empty;
        var right = EmitExpression(assignment.Right);
        if (operation == "=" && _variables[name] == typeof(string) && _explicitlyTypedVariables.Contains(name))
            right = $"({right} ?? string.Empty)";
        AppendLine($"{left} {operation} {right}{suffix}");
    }

    private string EmitInlinePipeline(PipelineBaseAst? pipeline)
    {
        if (pipeline is null)
            return string.Empty;
        var expression = UnwrapExpression(pipeline);
        if (expression is AssignmentStatementAst assignment)
        {
            var before = _builder.Length;
            EmitAssignment(assignment, terminate: false);
            var text = _builder.ToString(before, _builder.Length - before).Trim();
            _builder.Length = before;
            return text;
        }

        return EmitExpression(expression);
    }

    private string EmitExpression(Ast ast)
    {
        ast = UnwrapExpression(ast);
        return ast switch
        {
            StringConstantExpressionAst text => EmitString(text.Value),
            ExpandableStringExpressionAst expandable => EmitExpandableString(expandable),
            ConstantExpressionAst constant => EmitConstant(constant),
            VariableExpressionAst variable => EmitVariable(variable),
            ParenExpressionAst parenthesized => $"({EmitExpression(parenthesized.Pipeline)})",
            ConvertExpressionAst conversion when IsOrderedHashtableConversion(conversion) => EmitOrderedStringDictionary(conversion),
            ConvertExpressionAst conversion => throw Error(conversion, "Explicit PowerShell conversion expressions require runtime conversion semantics and are not supported by the typed compiler."),
            BinaryExpressionAst binary => EmitBinary(binary),
            UnaryExpressionAst unary => EmitUnary(unary),
            ArrayLiteralAst array => EmitArray(array),
            HashtableAst hashtable => EmitStringDictionary(hashtable),
            AssignmentStatementAst assignment => EmitAssignmentExpression(assignment),
            InvokeMemberExpressionAst invocation => _memberEmitter.EmitInvocation(invocation),
            MemberExpressionAst member => _memberEmitter.EmitMember(member),
            IndexExpressionAst index => _memberEmitter.EmitIndex(index),
            PipelineAst pipeline when IsLocalFunctionPipeline(pipeline) => EmitLocalFunctionCall(pipeline),
            CommandAst command when IsLocalFunctionCommand(command) => EmitLocalFunctionCall(command),
            _ => throw Error(ast, $"Expression '{ast.GetType().Name}' is not implemented by the C# emitter.")
        };
    }

    private string EmitExpandableString(ExpandableStringExpressionAst expandable)
    {
        if (expandable.Extent.Text.Contains("`$", StringComparison.Ordinal))
            throw Error(expandable, "Expandable strings that mix escaped dollar signs with interpolation require PowerShell token-preserving semantics.");

        var parts = new List<string>();
        var cursor = 0;
        foreach (var nested in expandable.NestedExpressions)
        {
            if (nested is not VariableExpressionAst variable || InferVariableType(variable) != typeof(string))
                throw Error(nested, "Typed expandable strings currently accept only statically typed string variables; subexpressions and runtime string conversion remain on the PowerShell path.");
            var token = nested.Extent.Text;
            var tokenIndex = expandable.Value.IndexOf(token, cursor, StringComparison.Ordinal);
            if (tokenIndex < 0)
                throw Error(nested, "Expandable string source could not be mapped losslessly to its parsed interpolation token.");
            if (tokenIndex > cursor)
                parts.Add(EmitString(expandable.Value.Substring(cursor, tokenIndex - cursor)));
            parts.Add($"({EmitVariable(variable)} ?? string.Empty)");
            cursor = tokenIndex + token.Length;
        }
        if (cursor < expandable.Value.Length)
            parts.Add(EmitString(expandable.Value.Substring(cursor)));
        if (parts.Count == 0)
            return EmitString(expandable.Value);
        if (parts.Count == 1)
            return parts[0];
        return $"global::System.String.Concat(new string[] {{ {string.Join(", ", parts)} }})";
    }

    private string EmitBooleanExpression(Ast ast)
    {
        var type = InferExpressionType(ast);
        if (type == typeof(string) && UnwrapExpression(ast) is AssignmentStatementAst)
            return $"!global::System.String.IsNullOrEmpty({EmitExpression(ast)})";
        if (type != typeof(bool))
            throw Error(ast, "PowerShell truthiness conversion is dynamic; typed conditions must already be Boolean.");
        return EmitExpression(ast);
    }

    private string EmitArray(ArrayLiteralAst array)
    {
        if (array.Elements.Count == 0)
            throw Error(array, "Empty array literals require an explicit element type.");
        var elementTypes = array.Elements.Select(InferExpressionType).Distinct().ToArray();
        if (elementTypes.Length != 1)
            throw Error(array, "Heterogeneous PowerShell array literals preserve per-element runtime types and are not supported by one CLR array element type.");
        var elementType = elementTypes[0];
        return $"new {GetTypeName(elementType)}[] {{ {string.Join(", ", array.Elements.Select(EmitExpression))} }}";
    }

    private string EmitVariable(VariableExpressionAst variable)
    {
        var name = variable.VariablePath.UserPath;
        if (name.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
        if (name.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
        if (name.Equals("null", StringComparison.OrdinalIgnoreCase)) return "null";
        if (!_variables.ContainsKey(name))
            throw Error(variable, $"Variable '${name}' does not have a statically resolved local type.");
        return GetVariableIdentifier(name);
    }

    private bool CanNormalizeNullStringReceiver(ExpressionAst expression)
    {
        var receiver = UnwrapTransparentExpression(expression);
        if (receiver is StringConstantExpressionAst)
            return true;
        return receiver is VariableExpressionAst variable &&
               _explicitlyTypedVariables.Contains(variable.VariablePath.UserPath);
    }

    private static Ast UnwrapTransparentExpression(Ast ast)
    {
        ast = UnwrapExpression(ast);
        while (ast is ParenExpressionAst parenthesized)
            ast = UnwrapExpression(parenthesized.Pipeline);
        return ast;
    }

    private bool CanUseScalarStringForeach(Ast condition)
    {
        var expression = UnwrapTransparentExpression(condition);
        return expression is StringConstantExpressionAst ||
               expression is VariableExpressionAst variable &&
               _explicitlyTypedVariables.Contains(variable.VariablePath.UserPath) &&
               InferVariableType(variable) == typeof(string);
    }

    private string GetVariableIdentifier(string name)
        => _variableIdentifiers.TryGetValue(name, out var identifier)
            ? identifier
            : throw Error(_body, $"Variable '${name}' does not have a canonical generated identifier.");

    private static bool IsNullExpression(Ast ast)
    {
        ast = UnwrapExpression(ast);
        return ast is VariableExpressionAst variable && variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase) ||
               ast is ConstantExpressionAst constant && constant.Value is null;
    }

    private static bool IsNonNullableValueType(Type type)
        => type.IsValueType && Nullable.GetUnderlyingType(type) is null;

    private Type InferExpressionType(Ast ast)
    {
        ast = UnwrapExpression(ast);
        return ast switch
        {
            StringConstantExpressionAst => typeof(string),
            ExpandableStringExpressionAst => typeof(string),
            ConstantExpressionAst constant => constant.Value?.GetType() ?? typeof(object),
            VariableExpressionAst variable => InferVariableType(variable),
            ParenExpressionAst parenthesized => InferExpressionType(parenthesized.Pipeline),
            ConvertExpressionAst conversion when IsOrderedHashtableConversion(conversion) => InferOrderedStringDictionaryType(conversion),
            ConvertExpressionAst conversion => throw Error(conversion, "Explicit PowerShell conversion expressions require runtime conversion semantics and are not supported by the typed compiler."),
            BinaryExpressionAst binary => InferBinaryType(binary),
            UnaryExpressionAst unary when IsIncrementOrDecrement(unary) => typeof(void),
            UnaryExpressionAst unary => InferExpressionType(unary.Child),
            ArrayLiteralAst array when array.Elements.Count > 0 && array.Elements.Select(InferExpressionType).Distinct().Count() == 1 => InferExpressionType(array.Elements[0]).MakeArrayType(),
            ArrayLiteralAst array => throw Error(array, "Heterogeneous or empty PowerShell array literals cannot be represented by one inferred CLR array element type."),
            HashtableAst hashtable => InferStringDictionaryType(hashtable),
            AssignmentStatementAst assignment => InferExpressionType(assignment.Right),
            InvokeMemberExpressionAst invocation => _memberEmitter.InferInvocationType(invocation),
            MemberExpressionAst member => _memberEmitter.InferMemberType(member),
            IndexExpressionAst index => _memberEmitter.InferIndexType(index),
            PipelineAst pipeline when IsLocalFunctionPipeline(pipeline) => InferLocalFunctionType(pipeline),
            CommandAst command when IsLocalFunctionCommand(command) => InferLocalFunctionType(command),
            _ => throw Error(ast, $"The CLR type of '{ast.GetType().Name}' cannot be inferred.")
        };
    }

    private Type InferVariableType(VariableExpressionAst variable)
    {
        var name = variable.VariablePath.UserPath;
        if (name.Equals("true", StringComparison.OrdinalIgnoreCase) || name.Equals("false", StringComparison.OrdinalIgnoreCase)) return typeof(bool);
        if (name.Equals("null", StringComparison.OrdinalIgnoreCase)) return typeof(object);
        if (_variables.TryGetValue(name, out var type)) return type;
        throw Error(variable, $"Variable '${name}' does not have a statically resolved local type.");
    }

    private static Ast UnwrapExpression(Ast ast)
    {
        while (true)
        {
            switch (ast)
            {
                case PipelineAst pipeline when pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandExpressionAst commandExpression:
                    ast = commandExpression.Expression;
                    continue;
                case CommandExpressionAst commandExpression:
                    ast = commandExpression.Expression;
                    continue;
                default:
                    return ast;
            }
        }
    }

    private static VariableExpressionAst? FindAssignedVariable(ExpressionAst left)
        => PowerShellAssignmentTargetPolicy.FindDirectVariable(left);

    private static Type UnifyTypes(Type left, Type right, Ast node)
    {
        if (left == right) return left;
        if (CanAssign(left, right)) return left;
        if (CanAssign(right, left)) return right;
        if (IsNumeric(left) && IsNumeric(right))
        {
            foreach (var candidate in new[] { typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(decimal), typeof(float), typeof(double) })
            {
                if (CanAssign(candidate, left) && CanAssign(candidate, right)) return candidate;
            }
        }
        throw new PowerShellCSharpEmissionException(node, $"Types '{left.FullName}' and '{right.FullName}' cannot be unified without dynamic PowerShell coercion.");
    }

    private void AppendLine(string text)
        => _builder.Append(' ', _indent * 4).AppendLine(text);

    private PowerShellCSharpEmissionException Error(Ast node, string message)
        => new(node, $"{_filePath}:{node.Extent.StartLineNumber}:{node.Extent.StartColumnNumber}: {message}");
}
