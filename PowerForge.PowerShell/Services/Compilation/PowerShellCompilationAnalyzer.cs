using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Uses the PowerShell parser to build conservative whole-unit typed-compilation plans.
/// </summary>
public sealed partial class PowerShellCompilationAnalyzer
{
    private readonly PowerShellCommandSemanticRegistry _commandRegistry;
    private readonly string _semanticProfileId;

    /// <summary>Creates an analyzer with the built-in deterministic command providers.</summary>
    public PowerShellCompilationAnalyzer()
        : this(PowerShellCommandSemanticRegistry.Default, PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
    }

    /// <summary>Creates an analyzer with additional compile-time-only command providers.</summary>
    public PowerShellCompilationAnalyzer(IEnumerable<PowerShellCompilationCommandProviderContract> commandProviders)
        : this(PowerShellCommandSemanticRegistry.Create(commandProviders), PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
    }

    /// <summary>Creates an analyzer with additional providers under one exact named semantic profile.</summary>
    public PowerShellCompilationAnalyzer(IEnumerable<PowerShellCompilationCommandProviderContract> commandProviders, string semanticProfileId)
        : this(PowerShellCommandSemanticRegistry.Create(commandProviders), semanticProfileId)
    {
    }

    internal PowerShellCompilationAnalyzer(PowerShellCommandSemanticRegistry commandRegistry)
        : this(commandRegistry, PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
    }

    internal PowerShellCompilationAnalyzer(PowerShellCommandSemanticRegistry commandRegistry, string semanticProfileId)
    {
        _commandRegistry = commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
        _semanticProfileId = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId).ProfileId;
    }

    private static readonly HashSet<string> SupportedBinaryOperators = new(StringComparer.Ordinal)
    {
        "Plus", "Minus", "Multiply", "Divide", "Rem",
        "Ieq", "Ceq", "Ine", "Cne", "Ilt", "Clt", "Ile", "Cle", "Igt", "Cgt", "Ige", "Cge",
        "And", "Or", "Isplit", "Csplit", "Join"
    };

    private static readonly HashSet<string> SupportedUnaryOperators = new(StringComparer.Ordinal)
    {
        "Plus", "Minus", "Not", "Exclaim", "Bnot", "PlusPlus", "MinusMinus", "PostfixPlusPlus", "PostfixMinusMinus"
    };

    private static readonly HashSet<string> SupportedAssignmentOperators = new(StringComparer.Ordinal)
    {
        "Equals", "PlusEquals", "MinusEquals", "MultiplyEquals", "DivideEquals", "RemEquals"
    };

    private PowerShellCompilationFilePlan AnalyzeFile(
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
            var scriptUnit = AnalyzeUnit("<script>", PowerShellCompilationUnitKind.Script, ast, file, topLevelStatements, targetFramework, capabilities, localFunctionNames);
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
                targetFramework,
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
                            function.Extent,
                            PowerShellCompilationFeatureIds.FilterFunction)
                    });
            }
            units.Add(functionUnit);
        }

        var fileWideDiagnostics = ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false)
            .OfType<UsingStatementAst>()
            .Where(static statement => statement.UsingStatementKind != UsingStatementKind.Namespace)
            .Select(statement => CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"Source '{statement.Extent.Text}' has runtime-bearing using semantics that cannot be omitted from a typed artifact; this file must remain on the PowerShell runtime path.",
                file,
                statement.Extent,
                PowerShellCompilationFeatureIds.RuntimeUsing))
            .ToList();
        if (ast.ScriptRequirements is not null)
        {
            fileWideDiagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                "Source #requires directives cannot be omitted from a typed artifact; this file must remain on the PowerShell runtime path.",
                file,
                ast.Extent,
                PowerShellCompilationFeatureIds.RequiresDirective));
        }
        var typeDefinitions = ast.FindAll(static node => node is TypeDefinitionAst, searchNestedScriptBlocks: false)
            .OfType<TypeDefinitionAst>()
            .Where(definition => ReferenceEquals(definition.Parent, ast.EndBlock))
            .ToArray();
        if (typeDefinitions.Length > 0)
        {
            fileWideDiagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                "PowerShell class and enum declarations define hosted runtime type identities; functions in this file remain on the PowerShell path until the canonical type-definition contract can lower them together.",
                file,
                typeDefinitions[0].Extent,
                PowerShellCompilationFeatureIds.TypeDefinition));
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

    private PowerShellCompilationUnitPlan AnalyzeUnit(
        string name,
        PowerShellCompilationUnitKind kind,
        ScriptBlockAst root,
        string file,
        IReadOnlyCollection<StatementAst> executableStatements,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames)
    {
        var diagnostics = new List<PowerShellCompilationDiagnostic>();
        var localVariables = CollectLocalVariables(root);
        var runtimeFreeLifecycle = PowerShellRuntimeFreePipelineLifecyclePolicy.TryGetPipelineParameter(root, capabilities, out _, out _);
        var unitCapabilities = runtimeFreeLifecycle
            ? capabilities | PowerShellCompilationCapability.PipelineParameterBinding
            : capabilities;
        var parameters = AnalyzeParameters(
            root.ParamBlock,
            root,
            file,
            diagnostics,
            localVariables,
            targetFramework,
            unitCapabilities,
            localFunctionNames,
            kind == PowerShellCompilationUnitKind.Script);

        AnalyzeUnsupportedNamedBlock(root.DynamicParamBlock, "dynamicparam", root, file, diagnostics, localVariables, targetFramework, unitCapabilities, localFunctionNames, reportLifecycleDiagnostic: true);
        AnalyzeUnsupportedNamedBlock(root.BeginBlock, "begin", root, file, diagnostics, localVariables, targetFramework, unitCapabilities, localFunctionNames, reportLifecycleDiagnostic: !runtimeFreeLifecycle);
        AnalyzeUnsupportedNamedBlock(root.ProcessBlock, "process", root, file, diagnostics, localVariables, targetFramework, unitCapabilities, localFunctionNames, reportLifecycleDiagnostic: !runtimeFreeLifecycle);
        AnalyzeUnsupportedNamedBlock(GetNamedBlock(root, "CleanBlock"), "clean", root, file, diagnostics, localVariables, targetFramework, unitCapabilities, localFunctionNames, reportLifecycleDiagnostic: true);

        foreach (var statement in executableStatements)
        {
            AnalyzeNode(statement, root, file, diagnostics, localVariables, targetFramework, unitCapabilities, localFunctionNames);
        }

        return new PowerShellCompilationUnitPlan(
            name,
            kind,
            root.Extent.StartLineNumber,
            typeof(object).FullName!,
            parameters,
            Deduplicate(diagnostics));
    }

    private void AnalyzeNode(
        Ast node,
        Ast unitRoot,
        string file,
        List<PowerShellCompilationDiagnostic> diagnostics,
        HashSet<string> localVariables,
        string? targetFramework,
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
                        candidate.Extent,
                        PowerShellCompilationFeatureIds.ScriptBlock));
                    break;
                case AttributeAst attribute when IsSupportedMetadataAttribute(attribute, capabilities, targetFramework):
                    break;
                case AttributeAst attribute:
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Syntax node 'AttributeAst' for attribute '{attribute.TypeName.FullName}' is not supported by the typed compiler metadata contract.",
                        file,
                        attribute.Extent,
                        PowerShellCompilationFeatureIds.ParameterMetadata));
                    break;
                case ConvertExpressionAst conversion when conversion.Parent is AssignmentStatementAst assignment && ReferenceEquals(assignment.Left, conversion) && !PowerShellCompilationParameterTypePolicy.CanUseInMethod(conversion.StaticType, targetFramework, capabilities):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Typed local declaration '{conversion.StaticType.FullName}' is not supported by the typed compiler.",
                        file,
                        conversion.Extent,
                        PowerShellCompilationFeatureIds.Conversion));
                    break;
                case ConvertExpressionAst conversion when conversion.Parent is AssignmentStatementAst assignment && ReferenceEquals(assignment.Left, conversion):
                    break;
                case ConvertExpressionAst conversion when IsOrderedHashtableConversion(conversion):
                    break;
                case ConvertExpressionAst conversion when
                    capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects) &&
                    PowerShellObjectConstructionPolicy.IsLiteral(conversion):
                    break;
                case ConvertExpressionAst conversion when PowerShellCompilationConversionPolicy.CanLower(conversion, targetFramework, capabilities):
                    break;
                case ConvertExpressionAst conversion:
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Explicit conversion to '{conversion.StaticType.FullName}' requires PowerShell runtime conversion semantics.",
                        file,
                        conversion.Extent,
                        PowerShellCompilationFeatureIds.Conversion));
                    break;
                case CommandAst command:
                    var commandName = command.GetCommandName();
                    if (command.Parent is PipelineAst mappingPipeline &&
                        PowerShellMappingCommandSemanticBinder.TryGetRuntimeFreeProcess(
                            mappingPipeline,
                            _commandRegistry,
                            out _,
                            out _))
                        break;
                    if (capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding) &&
                        PowerShellCommentHelpSemanticBinder.TryGetTargetName(command, out var helpTarget) &&
                        localFunctionNames?.Contains(helpTarget) == true)
                        break;
                    if (capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls) &&
                        (command.InvocationOperator == TokenKind.Dot ||
                         commandName is not null && localFunctionNames?.Contains(commandName) == true))
                        break;
                    if (_commandRegistry.Resolve(commandName) is
                        {
                            Status: PowerShellCommandResolutionStatus.Resolved,
                            Contract.Family: PowerShellCompilationCommandFamily.CommandDiscovery
                        } && PowerShellCommandDiscoverySemanticBinder.IsSupportedBooleanConsumption(command, capabilities))
                        break;
                    if (PowerShellCommandIslandPolicy.TryGetTargetStreamCommand(command, capabilities, out _, out _, out _, _commandRegistry) ||
                        capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
                        (unitRoot is ScriptBlockAst commandBody &&
                          (PowerShellCommandIslandPolicy.TryGetRuntimeRegion(command, commandBody, localFunctionNames, localVariables, out _) ||
                           PowerShellCommandIslandPolicy.TryGetCapturedRuntimeRegion(command, commandBody, localFunctionNames, localVariables, out _) ||
                           PowerShellCommandIslandPolicy.TryGetRuntimeTailRegion(command, commandBody, localFunctionNames, out _))))
                        break;
                    diagnostics.Add(CreateDiagnostic(
                        commandName is null ? PowerShellCompilationDiagnosticCode.DynamicCommandInvocation : PowerShellCompilationDiagnosticCode.CommandInvocation,
                        commandName is null
                            ? "Dynamic command resolution requires the PowerShell runtime."
                            : $"Command invocation '{commandName}' requires the PowerShell runtime.",
                        file,
                        command.Extent,
                        commandName is null ? PowerShellCompilationFeatureIds.DynamicCommand : PowerShellCompilationFeatureIds.ForCommand(commandName)));
                    break;
                case VariableExpressionAst variable when
                    capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters) &&
                    PowerShellBoundParametersPolicy.IsReference(variable) &&
                    PowerShellBoundParametersPolicy.IsSupportedReference(variable):
                    break;
                case VariableExpressionAst variable when
                    unitRoot is ScriptBlockAst body &&
                    PowerShellRuntimeStateIntrinsicPolicy.IsSupportedReference(variable, body, targetFramework, capabilities):
                    break;
                case VariableExpressionAst variable when IsRuntimeVariable(variable, localVariables):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.RuntimeScope,
                        $"Variable '${variable.VariablePath.UserPath}' depends on PowerShell runtime scope.",
                        file,
                        variable.Extent,
                        PowerShellCompilationFeatureIds.RuntimeScope));
                    break;
                case ExpandableStringExpressionAst expandable when expandable.Extent.Text.Contains("`$", StringComparison.Ordinal):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        "Expandable strings that mix escaped dollar signs with interpolation require PowerShell token-preserving semantics.",
                        file,
                        expandable.Extent,
                        PowerShellCompilationFeatureIds.ExpandableString));
                    break;
                case BinaryExpressionAst binary when
                    !SupportedBinaryOperators.Contains(binary.Operator.ToString()) &&
                    !PowerShellCompilationOperatorPolicy.CanLowerBinary(binary.Operator.ToString(), capabilities):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedOperator,
                        $"Binary operator '{binary.Operator}' is not supported by the typed compiler.",
                        file,
                        binary.Extent,
                        PowerShellCompilationFeatureIds.ForOperator(binary.Operator.ToString())));
                    break;
                case UnaryExpressionAst unary when !SupportedUnaryOperators.Contains(unary.TokenKind.ToString()):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedOperator,
                        $"Unary operator '{unary.TokenKind}' is not supported by the typed compiler.",
                        file,
                        unary.Extent,
                        PowerShellCompilationFeatureIds.ForOperator(unary.TokenKind.ToString())));
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
                            assignment.Left.Extent,
                            PowerShellCompilationFeatureIds.AssignmentTarget));
                    }
                    else if (PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(assignedVariable.VariablePath.UserPath))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                            $"Assignment to read-only automatic variable '${assignedVariable.VariablePath.UserPath}' requires PowerShell runtime semantics.",
                            file,
                            assignment.Left.Extent,
                            PowerShellCompilationFeatureIds.AutomaticVariableAssignment));
                    }
                    else if (!SupportedAssignmentOperators.Contains(assignment.Operator.ToString()))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedOperator,
                            $"Assignment operator '{assignment.Operator}' is not supported by the typed compiler.",
                            file,
                            assignment.Extent,
                            PowerShellCompilationFeatureIds.ForOperator(assignment.Operator.ToString())));
                    }
                    break;
                case SwitchStatementAst switchStatement when HasUnsupportedSwitchFlags(switchStatement.Flags):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Switch flags '{switchStatement.Flags}' require PowerShell runtime matching semantics.",
                        file,
                        switchStatement.Extent,
                        PowerShellCompilationFeatureIds.SwitchFlags));
                    break;
                case SwitchStatementAst:
                    break;
                case CatchClauseAst catchClause when catchClause.CatchTypes.Any(static type => !IsSupportedCatchType(type)):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        "Typed catch filters require statically resolvable CLR exception types on the conservative typed path.",
                        file,
                        catchClause.Extent,
                        PowerShellCompilationFeatureIds.CatchFilter));
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
                            candidate.Extent,
                            PowerShellCompilationFeatureIds.ForSyntax(candidate.GetType().Name)));
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
        => (flags & (SwitchFlags.File | SwitchFlags.Wildcard | SwitchFlags.Parallel)) != 0;

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
                Expression: VariableExpressionAst or TypeExpressionAst,
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

    private static StatementAst[] GetEndStatements(ScriptBlockAst scriptBlock, bool excludeFunctionDefinitions, bool excludeModuleExports)
        => scriptBlock.EndBlock?.Statements
            .Where(statement => !excludeFunctionDefinitions || statement is not FunctionDefinitionAst)
            .Where(statement => !excludeModuleExports || !IsExportModuleMemberStatement(statement))
            .ToArray() ?? Array.Empty<StatementAst>();

    private static bool IsExportModuleMemberStatement(StatementAst statement)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandAst command &&
           PowerShellModuleExportContract.IsExportModuleMember(command);

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

    private void AnalyzeUnsupportedNamedBlock(
        NamedBlockAst? block,
        string blockName,
        Ast unitRoot,
        string file,
        List<PowerShellCompilationDiagnostic> diagnostics,
        HashSet<string> localVariables,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames,
        bool reportLifecycleDiagnostic)
    {
        if (block is null)
            return;

        if (reportLifecycleDiagnostic)
        {
            diagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"The '{blockName}' block requires PowerShell pipeline lifecycle semantics.",
                file,
                block.Extent,
                PowerShellCompilationFeatureIds.PipelineLifecycle));
        }
        foreach (var statement in block.Statements)
            AnalyzeNode(statement, unitRoot, file, diagnostics, localVariables, targetFramework, capabilities, localFunctionNames);
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
        IScriptExtent extent,
        string? featureId = null)
        => new(code, message, file, extent.StartLineNumber, extent.StartColumnNumber, featureId);

    private static PowerShellCompilationDiagnostic[] Deduplicate(IEnumerable<PowerShellCompilationDiagnostic> diagnostics)
        => diagnostics
            .GroupBy(static diagnostic => new { diagnostic.Code, diagnostic.FeatureId, diagnostic.Line, diagnostic.Column, diagnostic.Message })
            .Select(static group => group.First())
            .OrderBy(static diagnostic => diagnostic.Line)
            .ThenBy(static diagnostic => diagnostic.Column)
            .ToArray();
}
