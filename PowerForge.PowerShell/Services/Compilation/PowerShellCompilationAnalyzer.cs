using System.Management.Automation.Language;
using System.Globalization;

namespace PowerForge;

/// <summary>
/// Uses the PowerShell parser to build conservative whole-unit typed-compilation plans.
/// </summary>
public sealed partial class PowerShellCompilationAnalyzer
{
    private static readonly HashSet<string> SupportedBinaryOperators = new(StringComparer.Ordinal)
    {
        "Plus", "Minus", "Multiply", "Divide", "Rem",
        "Ieq", "Ceq", "Ine", "Cne", "Ilt", "Clt", "Ile", "Cle", "Igt", "Cgt", "Ige", "Cge",
        "And", "Or", "Isplit", "Csplit", "Join"
    };

    private static readonly HashSet<string> SupportedUnaryOperators = new(StringComparer.Ordinal)
    {
        "Plus", "Minus", "Not", "Exclaim", "PlusPlus", "MinusMinus", "PostfixPlusPlus", "PostfixMinusMinus"
    };

    private static readonly HashSet<string> SupportedAssignmentOperators = new(StringComparer.Ordinal)
    {
        "Equals", "PlusEquals", "MinusEquals", "MultiplyEquals", "DivideEquals", "RemEquals"
    };

    private static readonly HashSet<Type> SupportedParameterTypes = new()
    {
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(string),
        typeof(System.Collections.IDictionary), typeof(System.Collections.Hashtable), typeof(System.Collections.Specialized.OrderedDictionary)
    };

    private static PowerShellCompilationFilePlan AnalyzeFile(
        string file,
        string basePath,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(file, out tokens, out errors);
        var relativePath = FrameworkCompatibility.GetRelativePath(basePath, file);
        if (errors.Length > 0)
        {
            var diagnostics = errors.Select(error => CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.ParseError,
                error.Message,
                file,
                error.Extent)).ToArray();
            return new PowerShellCompilationFilePlan(file, relativePath, Array.Empty<PowerShellCompilationUnitPlan>(), diagnostics);
        }

        var units = new List<PowerShellCompilationUnitPlan>();
        var topLevelStatements = GetEndStatements(
            ast,
            excludeFunctionDefinitions: true,
            excludeModuleExports: Path.GetExtension(file).Equals(".psm1", StringComparison.OrdinalIgnoreCase));
        if (topLevelStatements.Length > 0 || ast.ParamBlock is not null || HasUnsupportedNamedBlocks(ast))
        {
            var scriptUnit = AnalyzeUnit("<script>", PowerShellCompilationUnitKind.Script, ast, file, topLevelStatements, capabilities, localFunctionNames);
            if (scriptUnit.IsCompilable && !capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls))
            {
                try
                {
                    var emitted = new PowerShellCSharpMethodEmitter(file, ast, "<script>", "Invoke", topLevelStatements, targetFramework, capabilities, parameterMetadata: scriptUnit.Parameters).Emit();
                    scriptUnit = ReplaceUnit(scriptUnit, emitted.ReturnType, Array.Empty<PowerShellCompilationDiagnostic>());
                }
                catch (PowerShellCSharpEmissionException ex)
                {
                    scriptUnit = ReplaceUnit(
                        scriptUnit,
                        typeof(object),
                        new[]
                        {
                            new PowerShellCompilationDiagnostic(
                                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                                ex.Message,
                                file,
                                ex.Node.Extent.StartLineNumber,
                                ex.Node.Extent.StartColumnNumber)
                        });
                }
            }
            units.Add(scriptUnit);
        }

        var functions = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .Where(function => function.Parent is NamedBlockAst && ReferenceEquals(function.Parent.Parent, ast))
            .OrderBy(static function => function.Extent.StartOffset)
            .ToArray();
        foreach (var function in functions)
        {
            var functionUnit = AnalyzeUnit(
                function.Name,
                PowerShellCompilationUnitKind.Function,
                function.Body,
                file,
                GetEndStatements(function.Body, excludeFunctionDefinitions: false, excludeModuleExports: false),
                capabilities,
                localFunctionNames);
            if (function.IsFilter)
            {
                functionUnit = ReplaceUnit(
                    functionUnit,
                    typeof(object),
                    new[]
                    {
                        CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                            $"Filter '{function.Name}' requires per-pipeline-input PowerShell processing semantics and cannot be compiled as an ordinary CLR method.",
                            file,
                            function.Extent)
                    });
            }
            if (functionUnit.IsCompilable && !capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls))
            {
                try
                {
                    var emitted = new PowerShellCSharpMethodEmitter(file, function, targetFramework, capabilities, parameterMetadata: functionUnit.Parameters).Emit();
                    functionUnit = ReplaceUnit(functionUnit, emitted.ReturnType, Array.Empty<PowerShellCompilationDiagnostic>());
                }
                catch (PowerShellCSharpEmissionException ex)
                {
                    functionUnit = ReplaceUnit(
                        functionUnit,
                        typeof(object),
                        new[]
                        {
                            new PowerShellCompilationDiagnostic(
                                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                                ex.Message,
                                file,
                                ex.Node.Extent.StartLineNumber,
                                ex.Node.Extent.StartColumnNumber)
                        });
                }
            }
            units.Add(functionUnit);
        }

        var collidingMethodNames = units
            .Where(static unit => unit.Kind == PowerShellCompilationUnitKind.Function)
            .GroupBy(static unit => PowerShellCSharpMethodEmitter.SanitizeIdentifier(unit.Name), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (collidingMethodNames.Count > 0)
        {
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                var generatedName = PowerShellCSharpMethodEmitter.SanitizeIdentifier(unit.Name);
                if (unit.Kind != PowerShellCompilationUnitKind.Function || !collidingMethodNames.Contains(generatedName))
                    continue;
                var function = functions.First(candidate => candidate.Name == unit.Name && candidate.Body.Extent.StartLineNumber == unit.StartLine);
                units[index] = ReplaceUnit(
                    unit,
                    typeof(object),
                    new[]
                    {
                        CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                            $"Function '{unit.Name}' collides with another function after CLR identifier normalization to '{generatedName}'.",
                            file,
                            function.Extent)
                    });
            }
        }

        var fileWideDiagnostics = ast.UsingStatements
            .OfType<UsingStatementAst>()
            .Where(static statement => statement.UsingStatementKind != UsingStatementKind.Namespace)
            .Select(statement => CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"Source '{statement.Extent.Text}' has runtime-bearing using semantics that cannot be omitted from a typed artifact; this file must remain on the PowerShell runtime path.",
                file,
                statement.Extent))
            .ToList();
        if (ast.ScriptRequirements is not null)
        {
            fileWideDiagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                "Source #requires directives cannot be omitted from a typed artifact; this file must remain on the PowerShell runtime path.",
                file,
                ast.Extent));
        }
        if (fileWideDiagnostics.Count > 0)
        {
            var diagnostics = fileWideDiagnostics.ToArray();
            for (var index = 0; index < units.Count; index++)
                units[index] = ReplaceUnit(units[index], typeof(object), diagnostics);
            return new PowerShellCompilationFilePlan(
                file,
                relativePath,
                units.ToArray(),
                units.Count == 0 ? diagnostics : Array.Empty<PowerShellCompilationDiagnostic>());
        }

        return new PowerShellCompilationFilePlan(file, relativePath, units.ToArray(), Array.Empty<PowerShellCompilationDiagnostic>());
    }

    private static PowerShellCompilationUnitPlan ReplaceUnit(
        PowerShellCompilationUnitPlan unit,
        Type returnType,
        PowerShellCompilationDiagnostic[] additionalDiagnostics)
        => new(
            unit.Name,
            unit.Kind,
            unit.StartLine,
            returnType.FullName ?? returnType.Name,
            unit.Parameters,
            unit.Diagnostics.Concat(additionalDiagnostics).ToArray());

    private static PowerShellCompilationUnitPlan AnalyzeUnit(
        string name,
        PowerShellCompilationUnitKind kind,
        ScriptBlockAst root,
        string file,
        IReadOnlyCollection<StatementAst> executableStatements,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames)
    {
        var diagnostics = new List<PowerShellCompilationDiagnostic>();
        var localVariables = CollectLocalVariables(root);
        var parameters = AnalyzeParameters(root.ParamBlock, root, file, diagnostics, localVariables, capabilities, localFunctionNames);

        AnalyzeUnsupportedNamedBlock(root.DynamicParamBlock, "dynamicparam", root, file, diagnostics, localVariables, capabilities, localFunctionNames);
        AnalyzeUnsupportedNamedBlock(root.BeginBlock, "begin", root, file, diagnostics, localVariables, capabilities, localFunctionNames);
        AnalyzeUnsupportedNamedBlock(root.ProcessBlock, "process", root, file, diagnostics, localVariables, capabilities, localFunctionNames);
        AnalyzeUnsupportedNamedBlock(GetNamedBlock(root, "CleanBlock"), "clean", root, file, diagnostics, localVariables, capabilities, localFunctionNames);

        foreach (var statement in executableStatements)
        {
            AnalyzeNode(statement, root, file, diagnostics, localVariables, capabilities, localFunctionNames);
        }

        return new PowerShellCompilationUnitPlan(
            name,
            kind,
            root.Extent.StartLineNumber,
            typeof(object).FullName!,
            parameters,
            Deduplicate(diagnostics));
    }

    private static PowerShellCompilationParameter[] AnalyzeParameters(
        ParamBlockAst? paramBlock,
        Ast unitRoot,
        string file,
        List<PowerShellCompilationDiagnostic> diagnostics,
        HashSet<string> localVariables,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames)
    {
        if (paramBlock is null)
            return Array.Empty<PowerShellCompilationParameter>();

        foreach (var attribute in paramBlock.Attributes)
            AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables, capabilities, localFunctionNames);

        var result = new List<PowerShellCompilationParameter>();
        foreach (var parameter in paramBlock.Parameters)
        {
            var type = parameter.StaticType;
            if (!IsSupportedParameterType(type))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedParameterType,
                    $"Parameter '${parameter.Name.VariablePath.UserPath}' must declare a supported scalar or one-dimensional array type; resolved type was '{type.FullName}'.",
                    file,
                    parameter.Extent));
            }

            var isSwitch = type == typeof(System.Management.Automation.SwitchParameter);
            result.Add(new PowerShellCompilationParameter(
                parameter.Name.VariablePath.UserPath,
                (isSwitch ? typeof(bool) : type).FullName ?? type.Name,
                parameter.DefaultValue is not null,
                IsMandatoryParameter(parameter),
                isSwitch,
                GetAliases(parameter),
                HasMetadataAttribute(parameter, "AllowNull"),
                GetValidations(parameter)));

            foreach (var attribute in parameter.Attributes.Where(static attribute => attribute is not TypeConstraintAst))
                AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables, capabilities, localFunctionNames);
            if (parameter.DefaultValue is not null)
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Parameter '${parameter.Name.VariablePath.UserPath}' declares a PowerShell default value, which is not supported by the typed compiler.",
                    file,
                    parameter.DefaultValue.Extent));
                AnalyzeNode(parameter.DefaultValue, unitRoot, file, diagnostics, localVariables, capabilities, localFunctionNames);
            }
        }

        ValidateParameterBindingNames(paramBlock, file, diagnostics);

        return result.ToArray();
    }

    private static bool IsSupportedParameterType(Type type)
        => type == typeof(System.Management.Automation.SwitchParameter) ||
           SupportedParameterTypes.Contains(type) ||
           (type.IsArray && type.GetArrayRank() == 1 && SupportedParameterTypes.Contains(type.GetElementType()!));

    private static void AnalyzeNode(
        Ast node,
        Ast unitRoot,
        string file,
        List<PowerShellCompilationDiagnostic> diagnostics,
        HashSet<string> localVariables,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames)
    {
        foreach (var candidate in node.FindAll(static _ => true, searchNestedScriptBlocks: true))
        {
            if (!ReferenceEquals(candidate, node) && HasBlockingAncestor(candidate, node, unitRoot))
                continue;

            if (candidate is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, unitRoot))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.ScriptBlock,
                    "Nested script blocks and script-block literals require PowerShell runtime semantics.",
                    file,
                    candidate.Extent));
                continue;
            }

            switch (candidate)
            {
                case ScriptBlockExpressionAst:
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.ScriptBlock,
                        "Nested script blocks and script-block literals require PowerShell runtime semantics.",
                        file,
                        candidate.Extent));
                    break;
                case AttributeAst attribute when IsSupportedMetadataAttribute(attribute):
                    break;
                case ConvertExpressionAst conversion when conversion.Parent is AssignmentStatementAst assignment && ReferenceEquals(assignment.Left, conversion) && !IsSupportedParameterType(conversion.StaticType):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Typed local declaration '{conversion.StaticType.FullName}' is not supported by the typed compiler.",
                        file,
                        conversion.Extent));
                    break;
                case ConvertExpressionAst conversion when conversion.Parent is AssignmentStatementAst assignment && ReferenceEquals(assignment.Left, conversion):
                    break;
                case ConvertExpressionAst conversion when IsOrderedHashtableConversion(conversion):
                    break;
                case ConvertExpressionAst conversion when
                    capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects) &&
                    PowerShellObjectConstructionPolicy.IsLiteral(conversion):
                    break;
                case ConvertExpressionAst conversion:
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Explicit conversion to '{conversion.StaticType.FullName}' requires PowerShell runtime conversion semantics.",
                        file,
                        conversion.Extent));
                    break;
                case CommandAst command:
                    var commandName = command.GetCommandName();
                    if (capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls) &&
                        (command.InvocationOperator == TokenKind.Dot ||
                         commandName is not null && localFunctionNames?.Contains(commandName) == true))
                        break;
                    if (capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
                        (PowerShellCommandIslandPolicy.TryGetStreamCommand(command, out _, out _) ||
                         (unitRoot is ScriptBlockAst commandBody &&
                          (PowerShellCommandIslandPolicy.TryGetRuntimeRegion(command, commandBody, localFunctionNames, localVariables, out _) ||
                           PowerShellCommandIslandPolicy.TryGetRuntimeTailRegion(command, commandBody, localFunctionNames, out _)))))
                        break;
                    diagnostics.Add(CreateDiagnostic(
                        commandName is null ? PowerShellCompilationDiagnosticCode.DynamicCommandInvocation : PowerShellCompilationDiagnosticCode.CommandInvocation,
                        commandName is null
                            ? "Dynamic command resolution requires the PowerShell runtime."
                            : $"Command invocation '{commandName}' requires the PowerShell runtime.",
                        file,
                        command.Extent));
                    break;
                case VariableExpressionAst variable when
                    capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters) &&
                    PowerShellBoundParametersPolicy.IsReference(variable) &&
                    PowerShellBoundParametersPolicy.IsSupportedReference(variable):
                    break;
                case VariableExpressionAst variable when IsRuntimeVariable(variable, localVariables):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.RuntimeScope,
                        $"Variable '${variable.VariablePath.UserPath}' depends on PowerShell runtime scope.",
                        file,
                        variable.Extent));
                    break;
                case ExpandableStringExpressionAst expandable when expandable.Extent.Text.Contains("`$", StringComparison.Ordinal):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        "Expandable strings that mix escaped dollar signs with interpolation require PowerShell token-preserving semantics.",
                        file,
                        expandable.Extent));
                    break;
                case BinaryExpressionAst binary when !SupportedBinaryOperators.Contains(binary.Operator.ToString()):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedOperator,
                        $"Binary operator '{binary.Operator}' is not supported by the typed compiler.",
                        file,
                        binary.Extent));
                    break;
                case UnaryExpressionAst unary when !SupportedUnaryOperators.Contains(unary.TokenKind.ToString()):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedOperator,
                        $"Unary operator '{unary.TokenKind}' is not supported by the typed compiler.",
                        file,
                        unary.Extent));
                    break;
                case AssignmentStatementAst discard when
                    capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
                    unitRoot is ScriptBlockAst discardBody &&
                    PowerShellCommandIslandPolicy.IsDiscardAssignment(discard) &&
                    PowerShellCommandIslandPolicy.IsRuntimeRegion(discard, discardBody, localFunctionNames, localVariables):
                    break;
                case AssignmentStatementAst assignment:
                    var assignedVariable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
                    if (assignedVariable is null && IsPotentialTypedMutation(assignment, unitRoot))
                        break;
                    if (assignedVariable is null)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                            "Only direct local-variable assignment and conservative typed index/member mutation are supported; other assignment targets require PowerShell runtime semantics.",
                            file,
                            assignment.Left.Extent));
                    }
                    else if (PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(assignedVariable.VariablePath.UserPath))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                            $"Assignment to read-only automatic variable '${assignedVariable.VariablePath.UserPath}' requires PowerShell runtime semantics.",
                            file,
                            assignment.Left.Extent));
                    }
                    else if (!SupportedAssignmentOperators.Contains(assignment.Operator.ToString()))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedOperator,
                            $"Assignment operator '{assignment.Operator}' is not supported by the typed compiler.",
                            file,
                            assignment.Extent));
                    }
                    break;
                case SwitchStatementAst switchStatement when HasUnsupportedSwitchFlags(switchStatement.Flags):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Switch flags '{switchStatement.Flags}' require PowerShell runtime matching semantics.",
                        file,
                        switchStatement.Extent));
                    break;
                case SwitchStatementAst:
                    break;
                case CatchClauseAst catchClause when catchClause.CatchTypes.Any(static type => !IsSupportedCatchType(type)):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        "Typed catch filters require statically resolvable CLR exception types on the conservative typed path.",
                        file,
                        catchClause.Extent));
                    break;
                case CatchClauseAst:
                case TryStatementAst:
                case ThrowStatementAst:
                    break;
                default:
                    if (!IsSupportedNode(candidate, unitRoot))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                            $"Syntax node '{candidate.GetType().Name}' is not supported by the typed compiler.",
                            file,
                            candidate.Extent));
                    }
                    break;
            }
        }
    }

    private static bool IsRuntimeVariable(VariableExpressionAst variable, HashSet<string> localVariables)
    {
        var name = variable.VariablePath.UserPath;
        if (PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(name) &&
            PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable))
            return false;
        if (name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("null", StringComparison.OrdinalIgnoreCase))
            return false;

        if (name.Contains(':'))
            return true;

        if (localVariables.Contains(name))
            return false;

        return true;
    }

    private static bool IsSupportedNode(Ast candidate, Ast unitRoot)
        => ReferenceEquals(candidate, unitRoot) || candidate is
            NamedBlockAst or StatementBlockAst or ParamBlockAst or ParameterAst or TypeConstraintAst or
            PipelineAst or CommandExpressionAst or AssignmentStatementAst or IfStatementAst or
            SwitchStatementAst or ForStatementAst or WhileStatementAst or ForEachStatementAst or TryStatementAst or CatchClauseAst or ReturnStatementAst or
            ThrowStatementAst or BreakStatementAst or ContinueStatementAst or BinaryExpressionAst or UnaryExpressionAst or
            ParenExpressionAst or ConvertExpressionAst or ConstantExpressionAst or StringConstantExpressionAst or ExpandableStringExpressionAst or
            VariableExpressionAst or ArrayLiteralAst or ArrayExpressionAst or HashtableAst or TypeExpressionAst or MemberExpressionAst or
            InvokeMemberExpressionAst or IndexExpressionAst;

    private static bool HasUnsupportedSwitchFlags(SwitchFlags flags)
        => (flags & (SwitchFlags.File | SwitchFlags.Regex | SwitchFlags.Wildcard | SwitchFlags.Parallel)) != 0;

    private static bool IsSupportedCatchType(TypeConstraintAst constraint)
    {
        var type = constraint.TypeName.GetReflectionType();
        return type is not null && typeof(Exception).IsAssignableFrom(type);
    }

    private static bool IsPotentialTypedMutation(AssignmentStatementAst assignment, Ast unitRoot)
    {
        if (assignment.Operator.ToString() != "Equals")
            return false;
        if (assignment.Left is MemberExpressionAst
            {
                Expression: VariableExpressionAst,
                Member: StringConstantExpressionAst
            })
            return true;
        if (assignment.Left is not IndexExpressionAst { Target: VariableExpressionAst target })
            return false;
        var name = target.VariablePath.UserPath;
        if (unitRoot is ScriptBlockAst scriptBlock &&
            scriptBlock.ParamBlock?.Parameters.FirstOrDefault(parameter =>
                parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } parameter &&
            (parameter.StaticType.IsArray && parameter.StaticType.GetArrayRank() == 1 ||
             typeof(System.Collections.IDictionary).IsAssignableFrom(parameter.StaticType)))
            return true;
        return unitRoot.FindAll(
                node => node is AssignmentStatementAst candidate && candidate.Extent.StartOffset < assignment.Extent.StartOffset,
                searchNestedScriptBlocks: false)
            .OfType<AssignmentStatementAst>()
            .Any(candidate =>
                PowerShellAssignmentTargetPolicy.FindDirectVariable(candidate.Left) is { } variable &&
                variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                (UnwrapDictionaryLiteral(candidate.Right) || IsArrayProducingAssignment(candidate)));
    }

    private static bool IsArrayProducingAssignment(AssignmentStatementAst assignment)
        => assignment.Left is ConvertExpressionAst conversion && conversion.StaticType.IsArray ||
           assignment.Right.FindAll(static node => node is ArrayLiteralAst or ArrayExpressionAst, searchNestedScriptBlocks: false).Any();

    private static bool UnwrapDictionaryLiteral(StatementAst right)
    {
        Ast current = right;
        while (current is PipelineAst { PipelineElements.Count: 1 } pipeline)
            current = pipeline.PipelineElements[0];
        if (current is CommandExpressionAst expression)
            current = expression.Expression;
        return current is HashtableAst || current is ConvertExpressionAst conversion && IsOrderedHashtableConversion(conversion);
    }

    private static bool IsOrderedHashtableConversion(ConvertExpressionAst conversion)
        => conversion.StaticType == typeof(System.Collections.Specialized.OrderedDictionary) &&
           conversion.Child is HashtableAst;

    private static bool IsMandatoryParameter(ParameterAst parameter)
        => parameter.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute => IsAttributeNamed(attribute, "Parameter"))
            .SelectMany(static attribute => attribute.NamedArguments)
            .Any(static argument =>
                argument.ArgumentName.Equals("Mandatory", StringComparison.OrdinalIgnoreCase) &&
                TryGetBooleanAttributeValue(argument, out var value) && value);

    private static bool IsSupportedMetadataAttribute(AttributeAst attribute)
    {
        if (IsAttributeNamed(attribute, "CmdletBinding"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0;
        if (IsAttributeNamed(attribute, "Parameter"))
        {
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.All(static argument =>
                argument.ArgumentName.Equals("Mandatory", StringComparison.OrdinalIgnoreCase) &&
                TryGetBooleanAttributeValue(argument, out _));
        }
        if (IsAttributeNamed(attribute, "Alias"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                   attribute.PositionalArguments.All(static argument => argument is StringConstantExpressionAst { Value.Length: > 0 });
        if (IsAttributeNamed(attribute, "AllowNull") ||
            IsAttributeNamed(attribute, "ValidateNotNull") ||
            IsAttributeNamed(attribute, "ValidateNotNullOrEmpty"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0;
        if (IsAttributeNamed(attribute, "ValidateSet"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                   attribute.PositionalArguments.All(static argument => argument is StringConstantExpressionAst);
        if (IsAttributeNamed(attribute, "ValidatePattern"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 1 &&
                   attribute.PositionalArguments[0] is StringConstantExpressionAst;
        if (IsAttributeNamed(attribute, "ValidateRange"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 2 &&
                   attribute.PositionalArguments.All(static argument => TryGetInvariantNumericLiteral(argument, out _));
        return false;
    }

    private static string[] GetAliases(ParameterAst parameter)
        => parameter.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute => IsAttributeNamed(attribute, "Alias"))
            .SelectMany(static attribute => attribute.PositionalArguments.OfType<StringConstantExpressionAst>())
            .Select(static argument => argument.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasMetadataAttribute(ParameterAst parameter, string name)
        => parameter.Attributes.OfType<AttributeAst>().Any(attribute => IsAttributeNamed(attribute, name));

    private static PowerShellCompilationValidation[] GetValidations(ParameterAst parameter)
    {
        var validations = new List<PowerShellCompilationValidation>();
        foreach (var attribute in parameter.Attributes.OfType<AttributeAst>())
        {
            if (IsAttributeNamed(attribute, "ValidateNotNull"))
                validations.Add(new PowerShellCompilationValidation(PowerShellCompilationValidationKind.NotNull));
            else if (IsAttributeNamed(attribute, "ValidateNotNullOrEmpty"))
                validations.Add(new PowerShellCompilationValidation(PowerShellCompilationValidationKind.NotNullOrEmpty));
            else if (IsAttributeNamed(attribute, "ValidateSet"))
                validations.Add(new PowerShellCompilationValidation(
                    PowerShellCompilationValidationKind.Set,
                    attribute.PositionalArguments.OfType<StringConstantExpressionAst>().Select(static argument => argument.Value).ToArray()));
            else if (IsAttributeNamed(attribute, "ValidatePattern") &&
                     attribute.PositionalArguments.Count == 1 &&
                     attribute.PositionalArguments[0] is StringConstantExpressionAst pattern)
                validations.Add(new PowerShellCompilationValidation(PowerShellCompilationValidationKind.Pattern, new[] { pattern.Value }));
            else if (IsAttributeNamed(attribute, "ValidateRange") && attribute.PositionalArguments.Count == 2)
                validations.Add(new PowerShellCompilationValidation(
                    PowerShellCompilationValidationKind.Range,
                    attribute.PositionalArguments.Select(argument =>
                        TryGetInvariantNumericLiteral(argument, out var literal) ? literal : string.Empty).ToArray()));
        }
        return validations.ToArray();
    }

    private static bool TryGetInvariantNumericLiteral(ExpressionAst expression, out string literal)
    {
        object? value;
        try
        {
            value = expression.SafeGetValue();
        }
        catch (InvalidOperationException)
        {
            literal = string.Empty;
            return false;
        }
        if (value is not byte and not sbyte and not short and not ushort and not int and not uint and not long and not ulong and not float and not double and not decimal)
        {
            literal = string.Empty;
            return false;
        }
        literal = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return literal.Length > 0;
    }

    private static bool TryGetBooleanAttributeValue(NamedAttributeArgumentAst argument, out bool value)
    {
        try
        {
            if (argument.Argument.SafeGetValue() is bool resolved)
            {
                value = resolved;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // Dynamic attribute arguments remain on the PowerShell runtime path.
        }
        value = false;
        return false;
    }

    private static bool IsAttributeNamed(AttributeAst attribute, string name)
        => attribute.TypeName.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
           attribute.TypeName.Name.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase);

    private static StatementAst[] GetEndStatements(ScriptBlockAst scriptBlock, bool excludeFunctionDefinitions, bool excludeModuleExports)
        => scriptBlock.EndBlock?.Statements
            .Where(statement => !excludeFunctionDefinitions || statement is not FunctionDefinitionAst)
            .Where(statement => !excludeModuleExports || !IsExportModuleMemberStatement(statement))
            .ToArray() ?? Array.Empty<StatementAst>();

    private static bool IsExportModuleMemberStatement(StatementAst statement)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandAst command &&
           command.GetCommandName()?.Equals("Export-ModuleMember", StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasUnsupportedNamedBlocks(ScriptBlockAst scriptBlock)
        => scriptBlock.DynamicParamBlock is not null || scriptBlock.BeginBlock is not null || scriptBlock.ProcessBlock is not null || GetNamedBlock(scriptBlock, "CleanBlock") is not null;

    private static NamedBlockAst? GetNamedBlock(ScriptBlockAst scriptBlock, string propertyName)
        => scriptBlock.GetType().GetProperty(propertyName)?.GetValue(scriptBlock) as NamedBlockAst;

    private static HashSet<string> CollectLocalVariables(ScriptBlockAst scriptBlock)
    {
        var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (scriptBlock.ParamBlock is not null)
        {
            foreach (var parameter in scriptBlock.ParamBlock.Parameters)
                variables.Add(parameter.Name.VariablePath.UserPath);
        }

        var assignments = scriptBlock.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: true)
            .Cast<AssignmentStatementAst>();
        foreach (var assignment in assignments)
        {
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
            if (variable is not null &&
                !variable.VariablePath.UserPath.Contains(':') &&
                !PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(variable.VariablePath.UserPath))
                variables.Add(variable.VariablePath.UserPath);
        }

        var loops = scriptBlock.FindAll(static node => node is ForEachStatementAst, searchNestedScriptBlocks: true)
            .Cast<ForEachStatementAst>();
        foreach (var loop in loops)
        {
            if (!loop.Variable.VariablePath.UserPath.Contains(':'))
                variables.Add(loop.Variable.VariablePath.UserPath);
        }

        return variables;
    }

    private static void AnalyzeUnsupportedNamedBlock(
        NamedBlockAst? block,
        string blockName,
        Ast unitRoot,
        string file,
        List<PowerShellCompilationDiagnostic> diagnostics,
        HashSet<string> localVariables,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames)
    {
        if (block is null)
            return;

        diagnostics.Add(CreateDiagnostic(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            $"The '{blockName}' block requires PowerShell pipeline lifecycle semantics.",
            file,
            block.Extent));
        foreach (var statement in block.Statements)
            AnalyzeNode(statement, unitRoot, file, diagnostics, localVariables, capabilities, localFunctionNames);
    }

    private static bool HasBlockingAncestor(Ast candidate, Ast statementRoot, Ast unitRoot)
    {
        for (var ancestor = candidate.Parent; ancestor is not null && !ReferenceEquals(ancestor, statementRoot.Parent); ancestor = ancestor.Parent)
        {
            if (ancestor is CommandAst)
                return true;
            if (ancestor is ScriptBlockAst && !ReferenceEquals(ancestor, unitRoot))
                return true;
            if (!IsSupportedNode(ancestor, unitRoot))
                return true;
            if (ReferenceEquals(ancestor, statementRoot))
                break;
        }

        return false;
    }

    private static PowerShellCompilationDiagnostic CreateDiagnostic(
        PowerShellCompilationDiagnosticCode code,
        string message,
        string file,
        IScriptExtent extent)
        => new(code, message, file, extent.StartLineNumber, extent.StartColumnNumber);

    private static PowerShellCompilationDiagnostic[] Deduplicate(IEnumerable<PowerShellCompilationDiagnostic> diagnostics)
        => diagnostics
            .GroupBy(static diagnostic => new { diagnostic.Code, diagnostic.Line, diagnostic.Column, diagnostic.Message })
            .Select(static group => group.First())
            .OrderBy(static diagnostic => diagnostic.Line)
            .ThenBy(static diagnostic => diagnostic.Column)
            .ToArray();
}
