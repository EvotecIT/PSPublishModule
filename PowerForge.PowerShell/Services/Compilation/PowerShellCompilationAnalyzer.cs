using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Uses the PowerShell parser to build conservative whole-unit typed-compilation plans.
/// </summary>
public sealed class PowerShellCompilationAnalyzer
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
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(string)
    };

    /// <summary>Analyzes a PowerShell file or directory.</summary>
    public PowerShellCompilationPlan Analyze(PowerShellCompilationSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var analysisTargetFramework = spec.Mode == PowerShellCompilationMode.Package ? null : spec.TargetFramework;
        if (analysisTargetFramework is not null)
        {
            PowerShellGeneratedTargetFrameworkPolicy.EnsureHostCanAnalyze(analysisTargetFramework);
            PowerShellGeneratedReferenceAssemblyResolver.EnsureAvailable(analysisTargetFramework);
        }
        var files = DiscoverFiles(spec);
        var basePath = Directory.Exists(spec.Path) ? spec.Path : Path.GetDirectoryName(spec.Path) ?? Directory.GetCurrentDirectory();
        return new PowerShellCompilationPlan(
            spec.Mode,
            files.Select(file => AnalyzeFile(file, basePath, analysisTargetFramework)).ToArray(),
            spec.TargetFramework);
    }

    private static string[] DiscoverFiles(PowerShellCompilationSpec spec)
    {
        if (File.Exists(spec.Path))
        {
            var extension = Path.GetExtension(spec.Path);
            if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("PowerShell compilation accepts .ps1 and .psm1 files.", nameof(spec));
            return new[] { spec.Path };
        }

        if (!Directory.Exists(spec.Path))
            throw new DirectoryNotFoundException($"PowerShell compilation input was not found: {spec.Path}");

        return EnumerateSourceFiles(spec.Path, spec.Recurse, spec.ExcludeDirectories)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root, bool recurse, string[] exclusions)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }

            if (!recurse) continue;
            foreach (var directory in Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (IsExcludedDirectory(name, exclusions)) continue;
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(directory);
            }
        }
    }

    private static bool IsExcludedDirectory(string directory, string[] exclusions)
        => exclusions.Any(exclusion =>
            directory.Equals(exclusion, StringComparison.OrdinalIgnoreCase) ||
            ((exclusion.Equals("bin", StringComparison.OrdinalIgnoreCase) || exclusion.Equals("obj", StringComparison.OrdinalIgnoreCase)) &&
             directory.StartsWith(exclusion + "-", StringComparison.OrdinalIgnoreCase)));

    private static PowerShellCompilationFilePlan AnalyzeFile(string file, string basePath, string? targetFramework)
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
            var scriptUnit = AnalyzeUnit("<script>", PowerShellCompilationUnitKind.Script, ast, file, topLevelStatements);
            if (scriptUnit.IsCompilable)
            {
                try
                {
                    var emitted = new PowerShellCSharpMethodEmitter(file, ast, "<script>", "Invoke", topLevelStatements, targetFramework).Emit();
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
                GetEndStatements(function.Body, excludeFunctionDefinitions: false, excludeModuleExports: false));
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
            if (functionUnit.IsCompilable)
            {
                try
                {
                    var emitted = new PowerShellCSharpMethodEmitter(file, function, targetFramework).Emit();
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
        IReadOnlyCollection<StatementAst> executableStatements)
    {
        var diagnostics = new List<PowerShellCompilationDiagnostic>();
        var localVariables = CollectLocalVariables(root);
        var parameters = AnalyzeParameters(root.ParamBlock, root, file, diagnostics, localVariables);

        AnalyzeUnsupportedNamedBlock(root.DynamicParamBlock, "dynamicparam", root, file, diagnostics, localVariables);
        AnalyzeUnsupportedNamedBlock(root.BeginBlock, "begin", root, file, diagnostics, localVariables);
        AnalyzeUnsupportedNamedBlock(root.ProcessBlock, "process", root, file, diagnostics, localVariables);
        AnalyzeUnsupportedNamedBlock(GetNamedBlock(root, "CleanBlock"), "clean", root, file, diagnostics, localVariables);

        foreach (var statement in executableStatements)
        {
            AnalyzeNode(statement, root, file, diagnostics, localVariables);
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
        HashSet<string> localVariables)
    {
        if (paramBlock is null)
            return Array.Empty<PowerShellCompilationParameter>();

        foreach (var attribute in paramBlock.Attributes)
            AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables);

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

            result.Add(new PowerShellCompilationParameter(
                parameter.Name.VariablePath.UserPath,
                type.FullName ?? type.Name,
                parameter.DefaultValue is not null,
                IsMandatoryParameter(parameter)));

            foreach (var attribute in parameter.Attributes.Where(static attribute => attribute is not TypeConstraintAst))
                AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables);
            if (parameter.DefaultValue is not null)
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Parameter '${parameter.Name.VariablePath.UserPath}' declares a PowerShell default value, which is not supported by the typed compiler.",
                    file,
                    parameter.DefaultValue.Extent));
                AnalyzeNode(parameter.DefaultValue, unitRoot, file, diagnostics, localVariables);
            }
        }

        return result.ToArray();
    }

    private static bool IsSupportedParameterType(Type type)
        => SupportedParameterTypes.Contains(type) || (type.IsArray && type.GetArrayRank() == 1 && SupportedParameterTypes.Contains(type.GetElementType()!));

    private static void AnalyzeNode(Ast node, Ast unitRoot, string file, List<PowerShellCompilationDiagnostic> diagnostics, HashSet<string> localVariables)
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
                case ConvertExpressionAst conversion:
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Explicit conversion to '{conversion.StaticType.FullName}' requires PowerShell runtime conversion semantics.",
                        file,
                        conversion.Extent));
                    break;
                case CommandAst command:
                    var commandName = command.GetCommandName();
                    diagnostics.Add(CreateDiagnostic(
                        commandName is null ? PowerShellCompilationDiagnosticCode.DynamicCommandInvocation : PowerShellCompilationDiagnosticCode.CommandInvocation,
                        commandName is null
                            ? "Dynamic command resolution requires the PowerShell runtime."
                            : $"Command invocation '{commandName}' requires the PowerShell runtime.",
                        file,
                        command.Extent));
                    break;
                case VariableExpressionAst variable when IsRuntimeVariable(variable, localVariables):
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.RuntimeScope,
                        $"Variable '${variable.VariablePath.UserPath}' depends on PowerShell runtime scope.",
                        file,
                        variable.Extent));
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
                case AssignmentStatementAst assignment:
                    var assignedVariable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
                    if (assignedVariable is null)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                            "Only direct local-variable assignment is supported; indexed and member assignment require PowerShell runtime semantics.",
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
            ForStatementAst or WhileStatementAst or ForEachStatementAst or ReturnStatementAst or
            BreakStatementAst or ContinueStatementAst or BinaryExpressionAst or UnaryExpressionAst or
            ParenExpressionAst or ConvertExpressionAst or ConstantExpressionAst or StringConstantExpressionAst or
            VariableExpressionAst or ArrayLiteralAst or HashtableAst or TypeExpressionAst or MemberExpressionAst or
            InvokeMemberExpressionAst or IndexExpressionAst;

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
        if (!IsAttributeNamed(attribute, "Parameter") || attribute.PositionalArguments.Count != 0)
            return false;
        return attribute.NamedArguments.All(static argument =>
            argument.ArgumentName.Equals("Mandatory", StringComparison.OrdinalIgnoreCase) &&
            TryGetBooleanAttributeValue(argument, out _));
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
        HashSet<string> localVariables)
    {
        if (block is null)
            return;

        diagnostics.Add(CreateDiagnostic(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            $"The '{blockName}' block requires PowerShell pipeline lifecycle semantics.",
            file,
            block.Extent));
        foreach (var statement in block.Statements)
            AnalyzeNode(statement, unitRoot, file, diagnostics, localVariables);
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
