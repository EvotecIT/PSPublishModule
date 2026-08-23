using System.Globalization;
using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

internal sealed class PowerShellCSharpMethodEmitter
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
    private readonly StringBuilder _builder = new();
    private readonly PowerShellCSharpMemberEmitter _memberEmitter;
    private int _indent = 1;

    internal PowerShellCSharpMethodEmitter(string filePath, FunctionDefinitionAst function)
        : this(filePath, function.Body, function.Name, SanitizeIdentifier(function.Name), null, initialize: true)
    {
    }

    internal PowerShellCSharpMethodEmitter(
        string filePath,
        ScriptBlockAst body,
        string sourceName,
        string generatedName,
        StatementAst[] statements)
        : this(filePath, body, sourceName, SanitizeIdentifier(generatedName), statements, initialize: true)
    {
    }

    private PowerShellCSharpMethodEmitter(
        string filePath,
        ScriptBlockAst body,
        string sourceName,
        string generatedName,
        StatementAst[]? statements,
        bool initialize)
    {
        _filePath = filePath;
        _body = body;
        _sourceName = sourceName;
        _generatedName = generatedName;
        _statements = statements;
        _memberEmitter = new PowerShellCSharpMemberEmitter(
            InferExpressionType,
            EmitExpression,
            CanAssign,
            GetTypeName,
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
            if (!PowerShellGeneratedTypePolicy.IsSupported(parameter.StaticType))
                throw Error(parameter, $"Parameter '${name}' uses CLR type '{parameter.StaticType.FullName}' outside the generated project reference set.");
            if (_variables.ContainsKey(name))
                throw Error(parameter, $"Parameter '${name}' duplicates another parameter under PowerShell's case-insensitive naming rules.");
            var identifier = SanitizeIdentifier(name);
            if (_variableIdentifiers.Values.Contains(identifier, StringComparer.Ordinal))
                throw Error(parameter, $"Parameter '${name}' collides with another parameter after CLR identifier normalization.");
            _variables.Add(name, parameter.StaticType);
            _variableIdentifiers.Add(name, identifier);
            _explicitlyTypedVariables.Add(name);
        }

        var statements = _statements ?? _body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
        InferLocalTypes(statements);
        ValidateVariableReferences(statements);
        var returnType = InferReturnType(statements);
        if (returnType != typeof(void) && statements.LastOrDefault() is not ReturnStatementAst)
            throw Error(_body, $"Typed non-void unit '{_sourceName}' must end with an explicit return statement on the conservative compilation path.");
        var parameterSource = string.Join(", ", parameters.Select(parameter =>
            $"{GetTypeName(parameter.StaticType)} {SanitizeIdentifier(parameter.Name.VariablePath.UserPath)}"));

        AppendLine($"public static {GetTypeName(returnType)} {_generatedName}({parameterSource})");
        AppendLine("{");
        _indent++;
        AppendLine("checked");
        AppendLine("{");
        _indent++;
        foreach (var parameter in parameters.Where(static parameter => parameter.StaticType == typeof(string)))
        {
            var identifier = GetVariableIdentifier(parameter.Name.VariablePath.UserPath);
            AppendLine($"{identifier} = {identifier} ?? string.Empty;");
        }
        foreach (var statement in statements)
            EmitStatement(statement, returnType);
        _indent--;
        AppendLine("}");
        _indent--;
        AppendLine("}");

        return new PowerShellCSharpMethodEmission(_generatedName, returnType, _builder.ToString().TrimEnd());
    }

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
            if (!collectionType.IsArray || collectionType.GetElementType() is null)
                throw Error(loop.Condition, "foreach currently requires a statically typed one-dimensional array.");
            var name = loop.Variable.VariablePath.UserPath;
            if (_variables.ContainsKey(name))
                throw Error(loop.Variable, $"foreach variable '${name}' cannot reuse another function-scope variable on the conservative compilation path.");
            _variables[name] = collectionType.GetElementType()!;
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

        var name = variable.VariablePath.UserPath;
        var rightType = InferExpressionType(assignment.Right);
        var declaredType = assignment.Left is ConvertExpressionAst conversion
            ? conversion.StaticType
            : rightType;
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
            if (!CanAssign(existingType, rightType))
                throw Error(assignment, $"Assignment changes '${name}' from '{existingType.FullName}' to incompatible type '{rightType.FullName}'.");
            return;
        }

        if (assignment.Parent is not NamedBlockAst && assignment.Parent is not ForStatementAst)
            throw Error(assignment, $"Local '${name}' must be declared at function scope or in a for initializer before it can be compiled safely.");

        _variables.Add(name, declaredType);
        AddVariableIdentifier(name, variable);
        _firstAssignmentOffsets.Add(name, assignment.Extent.StartOffset);
        if (assignment.Parent is ForStatementAst forStatement)
            _loopScopedVariables[name] = (forStatement.Extent.StartOffset, forStatement.Extent.EndOffset);
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
        if (returns.Length == 0)
            return typeof(void);

        Type? result = null;
        foreach (var statement in returns)
        {
            var current = statement.Pipeline is null ? typeof(void) : InferExpressionType(statement.Pipeline);
            if (result is not null && result != current)
                throw Error(statement, $"Return type '{current.FullName}' differs from earlier return type '{result.FullName}'; preserving PowerShell's branch-specific runtime types requires fallback.");
            result ??= current;
        }

        return result ?? typeof(void);
    }

    private void EmitStatement(StatementAst statement, Type returnType)
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
            case ForStatementAst forStatement:
                EmitFor(forStatement, returnType);
                return;
            case WhileStatementAst whileStatement:
                EmitWhile(whileStatement, returnType);
                return;
            case ForEachStatementAst forEachStatement:
                EmitForEach(forEachStatement, returnType);
                return;
            case BreakStatementAst breakStatement when breakStatement.Label is not null:
                throw Error(breakStatement, "Labeled break is not supported by the typed compiler.");
            case BreakStatementAst breakStatement when !HasLoopAncestor(breakStatement):
                throw Error(breakStatement, "break must be inside a supported loop.");
            case BreakStatementAst:
                AppendLine("break;");
                return;
            case ContinueStatementAst continueStatement when continueStatement.Label is not null:
                throw Error(continueStatement, "Labeled continue is not supported by the typed compiler.");
            case ContinueStatementAst continueStatement when !HasLoopAncestor(continueStatement):
                throw Error(continueStatement, "continue must be inside a supported loop.");
            case ContinueStatementAst:
                AppendLine("continue;");
                return;
            case PipelineAst pipeline when pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandExpressionAst:
                throw Error(pipeline, "Implicit PowerShell pipeline output cannot be emitted as a typed return value.");
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
        if (operation != "=" && IsIntegral(_variables[name]) && !_explicitlyTypedVariables.Contains(name))
            throw Error(assignment, $"Integral compound assignment to untyped local '${name}' can promote dynamically in PowerShell and is not eligible for typed compilation.");
        _declaredLocals.Add(name);
        var suffix = terminate ? ";" : string.Empty;
        var right = EmitExpression(assignment.Right);
        if (operation == "=" && _variables[name] == typeof(string) && _explicitlyTypedVariables.Contains(name))
            right = $"({right} ?? string.Empty)";
        AppendLine($"{left} {operation} {right}{suffix}");
    }

    private void EmitIf(IfStatementAst statement, Type returnType)
    {
        for (var index = 0; index < statement.Clauses.Count; index++)
        {
            var clause = statement.Clauses[index];
            AppendLine($"{(index == 0 ? "if" : "else if")} ({EmitBooleanExpression(clause.Item1)})");
            EmitBlock(clause.Item2, returnType);
        }

        if (statement.ElseClause is not null)
        {
            AppendLine("else");
            EmitBlock(statement.ElseClause, returnType);
        }
    }

    private void EmitFor(ForStatementAst statement, Type returnType)
    {
        var initializer = EmitInlinePipeline(statement.Initializer);
        var condition = statement.Condition is null ? "true" : EmitBooleanExpression(statement.Condition);
        var iterator = EmitInlinePipeline(statement.Iterator);
        AppendLine($"for ({initializer}; {condition}; {iterator})");
        EmitBlock(statement.Body, returnType);
    }

    private void EmitWhile(WhileStatementAst statement, Type returnType)
    {
        AppendLine($"while ({EmitBooleanExpression(statement.Condition)})");
        EmitBlock(statement.Body, returnType);
    }

    private void EmitForEach(ForEachStatementAst statement, Type returnType)
    {
        var name = statement.Variable.VariablePath.UserPath;
        var collectionType = InferExpressionType(statement.Condition);
        var elementType = collectionType.GetElementType()
            ?? throw Error(statement.Condition, "foreach requires a statically typed one-dimensional array.");
        var collection = EmitExpression(statement.Condition);
        _declaredLocals.Add(name);
        AppendLine($"foreach ({GetTypeName(_variables[name])} {GetVariableIdentifier(name)} in ({collection} ?? global::System.Array.Empty<{GetTypeName(elementType)}>()))");
        EmitBlock(statement.Body, returnType);
    }

    private void EmitBlock(StatementBlockAst block, Type returnType)
    {
        AppendLine("{");
        _indent++;
        foreach (var statement in block.Statements)
            EmitStatement(statement, returnType);
        _indent--;
        AppendLine("}");
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
            ConstantExpressionAst constant => EmitConstant(constant),
            VariableExpressionAst variable => EmitVariable(variable),
            ParenExpressionAst parenthesized => $"({EmitExpression(parenthesized.Pipeline)})",
            ConvertExpressionAst conversion => throw Error(conversion, "Explicit PowerShell conversion expressions require runtime conversion semantics and are not supported by the typed compiler."),
            BinaryExpressionAst binary => EmitBinary(binary),
            UnaryExpressionAst unary => EmitUnary(unary),
            ArrayLiteralAst array => EmitArray(array),
            InvokeMemberExpressionAst invocation => _memberEmitter.EmitInvocation(invocation),
            MemberExpressionAst member => _memberEmitter.EmitMember(member),
            IndexExpressionAst index => _memberEmitter.EmitIndex(index),
            _ => throw Error(ast, $"Expression '{ast.GetType().Name}' is not implemented by the C# emitter.")
        };
    }

    private string EmitBooleanExpression(Ast ast)
    {
        if (InferExpressionType(ast) != typeof(bool))
            throw Error(ast, "PowerShell truthiness conversion is dynamic; typed conditions must already be Boolean.");
        return EmitExpression(ast);
    }

    private string EmitBinary(BinaryExpressionAst binary)
    {
        var leftType = InferExpressionType(binary.Left);
        var rightType = InferExpressionType(binary.Right);
        var left = EmitExpression(binary.Left);
        var right = EmitExpression(binary.Right);
        var operation = binary.Operator.ToString();
        if (operation is "And" or "Or")
        {
            if (leftType != typeof(bool) || rightType != typeof(bool))
                throw Error(binary, $"Operator '-{operation.ToLowerInvariant()}' requires Boolean operands on the typed compilation path.");
        }
        if (operation is "Plus" or "Minus" or "Multiply" or "Divide" or "Rem")
        {
            if (operation == "Plus" && leftType == typeof(string) && rightType == typeof(string))
            {
                // C# and constrained PowerShell string concatenation agree for this narrow case.
            }
            else if (!IsNumeric(leftType) || !IsNumeric(rightType))
            {
                throw Error(binary, $"Arithmetic operator '{binary.Operator}' requires two numeric operands of known compatible types.");
            }
            else
            {
                if ((leftType == typeof(decimal)) != (rightType == typeof(decimal)))
                    throw Error(binary, "Mixed decimal and non-decimal arithmetic relies on PowerShell coercion and is not supported.");
                if (operation == "Divide" && IsIntegral(leftType) && IsIntegral(rightType))
                    throw Error(binary, "PowerShell integral division changes runtime result type based on the quotient and is not supported by one static CLR return type.");
                if (operation != "Divide" && IsIntegral(leftType) && IsIntegral(rightType))
                    throw Error(binary, "Unconstrained integral arithmetic can promote on overflow in PowerShell; use an explicitly typed accumulator with compound assignment.");
            }
        }
        if (operation is "Ieq" or "Ceq" or "Ine" or "Cne" or "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge")
        {
            if (leftType.IsArray || rightType.IsArray)
                throw Error(binary, "PowerShell array comparison is element-wise and is not supported by the scalar typed compiler.");
            if (IsNullExpression(binary.Left) && IsNonNullableValueType(rightType) ||
                IsNullExpression(binary.Right) && IsNonNullableValueType(leftType))
                throw Error(binary, "Comparing a non-nullable CLR value to $null requires PowerShell runtime semantics.");
            if (leftType != rightType && !IsNullExpression(binary.Left) && !IsNullExpression(binary.Right))
                throw Error(binary, "Comparison operands must have the same static CLR type on the conservative compilation path.");
        }
        if ((operation is "Ieq" or "Ine") && leftType == typeof(string) && rightType == typeof(string))
        {
            var comparison = $"global::System.String.Equals({left}, {right}, global::System.StringComparison.InvariantCultureIgnoreCase)";
            return operation == "Ine" ? $"!({comparison})" : comparison;
        }
        if ((operation is "Ceq" or "Cne") && leftType == typeof(string) && rightType == typeof(string))
        {
            var comparison = $"global::System.String.Equals({left}, {right}, global::System.StringComparison.InvariantCulture)";
            return operation == "Cne" ? $"!({comparison})" : comparison;
        }
        if ((operation is "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge") &&
            leftType == typeof(string) && rightType == typeof(string))
            throw Error(binary, "PowerShell string relational comparison uses culture-aware runtime semantics and is not supported by the typed compiler.");
        if (operation is "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge" &&
            !(IsNumeric(leftType) && IsNumeric(rightType)) &&
            !(leftType == typeof(char) && rightType == typeof(char)))
            throw Error(binary, $"Relational comparison for CLR type '{leftType.FullName}' is not supported by the conservative compiler.");

        var symbol = operation switch
        {
            "Plus" => "+", "Minus" => "-", "Multiply" => "*", "Divide" => "/", "Rem" => "%",
            "Ieq" or "Ceq" => "==", "Ine" or "Cne" => "!=",
            "Ilt" or "Clt" => "<", "Ile" or "Cle" => "<=", "Igt" or "Cgt" => ">", "Ige" or "Cge" => ">=",
            "And" => "&&", "Or" => "||",
            _ => throw Error(binary, $"Binary operator '{binary.Operator}' is not implemented.")
        };
        return $"({left} {symbol} {right})";
    }

    private string EmitUnary(UnaryExpressionAst unary)
    {
        var child = EmitExpression(unary.Child);
        var operation = unary.TokenKind.ToString();
        var childType = InferExpressionType(unary.Child);
        if ((operation is "Not" or "Exclaim") && childType != typeof(bool))
            throw Error(unary, "Typed logical negation requires a Boolean operand.");
        if ((operation is "Plus" or "Minus") && (!IsNumeric(childType) || IsIntegral(childType)))
            throw Error(unary, "Integral unary arithmetic can promote dynamically in PowerShell and is not supported.");
        if (operation is "PlusPlus" or "MinusMinus" or "PostfixPlusPlus" or "PostfixMinusMinus")
        {
            var variable = UnwrapExpression(unary.Child) as VariableExpressionAst
                ?? throw Error(unary, "Increment and decrement require a statically typed local variable.");
            if (!_explicitlyTypedVariables.Contains(variable.VariablePath.UserPath))
                throw Error(unary, $"Increment or decrement of untyped local '${variable.VariablePath.UserPath}' can promote dynamically in PowerShell.");
        }
        return operation switch
        {
            "Plus" => $"(+{child})",
            "Minus" => $"(-{child})",
            "Not" or "Exclaim" => $"(!{child})",
            "PlusPlus" => $"++{child}",
            "MinusMinus" => $"--{child}",
            "PostfixPlusPlus" => $"{child}++",
            "PostfixMinusMinus" => $"{child}--",
            _ => throw Error(unary, $"Unary operator '{unary.TokenKind}' is not implemented.")
        };
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
            ConstantExpressionAst constant => constant.Value?.GetType() ?? typeof(object),
            VariableExpressionAst variable => InferVariableType(variable),
            ParenExpressionAst parenthesized => InferExpressionType(parenthesized.Pipeline),
            ConvertExpressionAst conversion => throw Error(conversion, "Explicit PowerShell conversion expressions require runtime conversion semantics and are not supported by the typed compiler."),
            BinaryExpressionAst binary => InferBinaryType(binary),
            UnaryExpressionAst unary => InferExpressionType(unary.Child),
            ArrayLiteralAst array when array.Elements.Count > 0 && array.Elements.Select(InferExpressionType).Distinct().Count() == 1 => InferExpressionType(array.Elements[0]).MakeArrayType(),
            ArrayLiteralAst array => throw Error(array, "Heterogeneous or empty PowerShell array literals cannot be represented by one inferred CLR array element type."),
            InvokeMemberExpressionAst invocation => _memberEmitter.InferInvocationType(invocation),
            MemberExpressionAst member => _memberEmitter.InferMemberType(member),
            IndexExpressionAst index => _memberEmitter.InferIndexType(index),
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

    private Type InferBinaryType(BinaryExpressionAst binary)
    {
        var operation = binary.Operator.ToString();
        if (operation is "Ieq" or "Ceq" or "Ine" or "Cne" or "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge" or "And" or "Or")
            return typeof(bool);
        if (operation == "Divide")
        {
            var left = InferExpressionType(binary.Left);
            var right = InferExpressionType(binary.Right);
            if (left == typeof(decimal) && right == typeof(decimal)) return typeof(decimal);
            return typeof(double);
        }
        return UnifyTypes(InferExpressionType(binary.Left), InferExpressionType(binary.Right), binary);
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
        => left.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: false)
            .Cast<VariableExpressionAst>()
            .FirstOrDefault();

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

    private static bool CanAssign(Type target, Type source)
    {
        if (target == source || target.IsAssignableFrom(source)) return true;
        if (!IsNumeric(target) || !IsNumeric(source)) return false;
        return source == typeof(sbyte) && (target == typeof(short) || target == typeof(int) || target == typeof(long) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(byte) && (target == typeof(short) || target == typeof(ushort) || target == typeof(int) || target == typeof(uint) || target == typeof(long) || target == typeof(ulong) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(short) && (target == typeof(int) || target == typeof(long) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(ushort) && (target == typeof(int) || target == typeof(uint) || target == typeof(long) || target == typeof(ulong) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(int) && (target == typeof(long) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(uint) && (target == typeof(long) || target == typeof(ulong) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(long) && (target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(ulong) && (target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(float) && target == typeof(double);
    }

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
           type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsIntegral(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

    internal static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Generated";
        var builder = new StringBuilder(value.Length + 1);
        if (!char.IsLetter(value[0]) && value[0] != '_') builder.Append('_');
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        var identifier = builder.ToString();
        return CSharpKeywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed",
        "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return",
        "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    internal static string GetTypeName(Type type)
    {
        if (type.IsArray) return GetTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
            throw new InvalidOperationException($"Constructed generic CLR type '{type.FullName}' is not supported by the conservative generated project.");
        if (type == typeof(void)) return "void";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(char)) return "char";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";
        return "global::" + (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static string EmitConstant(ConstantExpressionAst constant)
    {
        return constant.Value switch
        {
            null => "null",
            bool value => value ? "true" : "false",
            string value => EmitString(value),
            char value => EmitChar(value),
            float value => value.ToString("R", CultureInfo.InvariantCulture) + "F",
            double value => value.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal value => value.ToString(CultureInfo.InvariantCulture) + "M",
            long value => value.ToString(CultureInfo.InvariantCulture) + "L",
            ulong value => value.ToString(CultureInfo.InvariantCulture) + "UL",
            uint value => value.ToString(CultureInfo.InvariantCulture) + "U",
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new PowerShellCSharpEmissionException(constant, $"Constant type '{constant.Value.GetType().FullName}' is not supported.")
        };
    }

    private static string EmitString(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    private static string EmitChar(char value)
        => value switch
        {
            '\\' => "'\\\\'",
            '\'' => "'\\\''",
            '\0' => "'\\0'",
            '\r' => "'\\r'",
            '\n' => "'\\n'",
            '\t' => "'\\t'",
            _ => "'" + value + "'"
        };

    private void AppendLine(string text)
        => _builder.Append(' ', _indent * 4).AppendLine(text);

    private PowerShellCSharpEmissionException Error(Ast node, string message)
        => new(node, $"{_filePath}:{node.Extent.StartLineNumber}:{node.Extent.StartColumnNumber}: {message}");
}
