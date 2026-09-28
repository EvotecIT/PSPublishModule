using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Selects a source-preserving native root only within its qualified artifact contract.</summary>
internal static class PowerShellHybridNativeEntryPlanner
{
    private static readonly HashSet<string> QualifiedHostedRootCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Write-Output", "Write-Host", "Write-Warning", "Write-Verbose", "Write-Debug", "Write-Information",
            "Write-Error"
        };
    private static readonly HashSet<string> QualifiedPipelineUtilityCommands =
        new(StringComparer.OrdinalIgnoreCase) { "Group-Object", "Sort-Object", "Select-Object" };
    private static readonly HashSet<string> QualifiedWhereComparisons =
        new(StringComparer.OrdinalIgnoreCase) { "EQ", "NE", "GT", "GE", "LT", "LE" };

    internal static PowerShellTypedExecutableCompilation? TryPlan(string sourcePath,
        IReadOnlyCollection<string> sourcePaths, PowerShellCompilationPlan plan,
        string targetFramework, string semanticProfileId)
    {
        if (plan.Mode != PowerShellCompilationMode.Hybrid || sourcePaths.Count != 1 ||
            !targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ||
            !semanticProfileId.Equals(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, StringComparison.Ordinal) ||
            plan.TargetContract?.Explicit != true ||
            !string.Equals(plan.TargetContract.RuntimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase))
            return null;
        var path = Path.GetFullPath(sourcePath);
        if (!PowerShellCompilationPathSafety.PathEquals(path, Path.GetFullPath(sourcePaths.Single()))) return null;
        var root = plan.Files.FirstOrDefault(file => PowerShellCompilationPathSafety.PathEquals(file.FullPath, path))?
            .Units.SingleOrDefault(static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        if (root?.IsCompilable != true) return null;
        var ast = Parser.ParseFile(path, out _, out var errors);
        var declarations = ast.EndBlock?.Statements.OfType<FunctionDefinitionAst>().ToArray()
            ?? Array.Empty<FunctionDefinitionAst>();
        // A conditional declaration can shadow a later pipeline stage in the same script frame.
        // Keep command admission based on top-level declarations only.
        var authoredFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in ast.FindAll(static node => node is FunctionDefinitionAst, true)
                     .OfType<FunctionDefinitionAst>())
        {
            var name = declaration.Name;
            var separator = name.LastIndexOf(':');
            authoredFunctionNames.Add(separator < 0 ? name : name.Substring(separator + 1));
        }
        var admittedCommands = new HashSet<string>(QualifiedHostedRootCommands, StringComparer.OrdinalIgnoreCase);
        admittedCommands.UnionWith(declarations.Select(static declaration => declaration.Name));
        // Caller-frame observations need their own launcher contract. This first route
        // owns the native script frame, not the wrapper's caller identity or input pipe.
        if (errors.Length != 0 || ast.FindAll(static node => node is ParamBlockAst, true)
                .OfType<ParamBlockAst>().Any(static block => block.FindAll(static node => node is CommandAst, true).Any()) ||
            ast.FindAll(static node => node is VariableExpressionAst variable &&
                (variable.VariablePath.UserPath.Equals("MyInvocation", StringComparison.OrdinalIgnoreCase) ||
                 variable.VariablePath.UserPath.Equals("PSCmdlet", StringComparison.OrdinalIgnoreCase) ||
                 variable.VariablePath.UserPath.Equals("input", StringComparison.OrdinalIgnoreCase) ||
                 variable.VariablePath.IsDriveQualified &&
                 (variable.VariablePath.DriveName.Equals("function", StringComparison.OrdinalIgnoreCase) ||
                  variable.VariablePath.DriveName.Equals("alias", StringComparison.OrdinalIgnoreCase))), true).Any())
            return null;
        var hostedCommands = ast.EndBlock?.FindAll(static node => node is CommandAst, true)
            .OfType<CommandAst>().ToArray() ?? Array.Empty<CommandAst>();
        if (hostedCommands.Any(command => command.InvocationOperator != TokenKind.Unknown ||
                command.Redirections.Count != 0 ||
                !IsQualifiedHostedRootCommand(command, admittedCommands, authoredFunctionNames)))
            return null;
        try
        {
            var compiled = PowerShellTypedExecutableCompiler.CompileHybridNativeEntry(path, plan, targetFramework, semanticProfileId);
            var method = compiled.EntryPointMethod;
            // Reuse canonical semantics rather than widening the scalar route's command list.
            // Qualified stream sites retain PowerShell command lookup and routing;
            // parent module/runtime state and provider effects remain outside this route.
            // Native lowering also rejects declaration/dependency owners.
            return method.NativeFunctionBinding is not null && compiled.LocalMethods.Length == 0 &&
                   !method.RequiresPowerShellModuleState &&
                   !method.RequiresPowerShellRuntimeState && !method.RequiresProviderCancellation
                ? compiled : null;
        }
        catch (InvalidOperationException) { return null; }
    }

    private static bool IsQualifiedHostedRootCommand(CommandAst command, ISet<string> admittedCommands,
        ISet<string> authoredFunctionNames)
    {
        if (command.GetCommandName() is not string name) return false;
        var isForEach = name.Equals("ForEach-Object", StringComparison.OrdinalIgnoreCase);
        var isWhere = name.Equals("Where-Object", StringComparison.OrdinalIgnoreCase);
        if (QualifiedPipelineUtilityCommands.Contains(name))
            return IsQualifiedPipelineUtility(command, name, authoredFunctionNames);
        if (!isForEach && !isWhere)
            return admittedCommands.Contains(name);
        // The whole pipeline remains PowerShell-owned. Admit only ordinary direct script blocks;
        // other parameter sets and nonlocal transfers need separate entry proof.
        if (authoredFunctionNames.Contains(name) ||
            command.Parent is not PipelineAst pipeline ||
            pipeline.PipelineElements.Count < 2 ||
            PowerShellCommandRegionSemanticBinder.HasPipelineOperators(pipeline))
            return false;
        var terminal = pipeline.PipelineElements[pipeline.PipelineElements.Count - 1];
        if (!ReferenceEquals(terminal, command) &&
            !(isWhere && pipeline.PipelineElements.Count >= 3 &&
              ReferenceEquals(pipeline.PipelineElements[pipeline.PipelineElements.Count - 2], command) &&
              terminal is CommandAst following &&
              (following.GetCommandName()?.Equals("ForEach-Object", StringComparison.OrdinalIgnoreCase) == true ||
               following.GetCommandName()?.Equals("Select-Object", StringComparison.OrdinalIgnoreCase) == true)))
            return false;
        if (isWhere && IsQualifiedWherePropertyComparison(command)) return true;
        if (command.CommandElements.Count != 2 ||
            command.CommandElements[1] is not ScriptBlockExpressionAst process ||
            process.ScriptBlock is not { DynamicParamBlock: null, BeginBlock: null, ProcessBlock: null,
                ParamBlock: null, EndBlock: { Unnamed: true } } block ||
            block.GetType().GetProperty("CleanBlock")?.GetValue(block) is not null)
            return false;
        return !HasNonlocalTransfer(block);
    }

    private static bool IsQualifiedWherePropertyComparison(CommandAst command)
    {
        var arguments = command.CommandElements.Skip(1).ToArray();
        var offset = arguments.Length == 4 && arguments[0] is CommandParameterAst propertyParameter &&
                     propertyParameter.Argument is null &&
                     propertyParameter.ParameterName.Equals("Property", StringComparison.OrdinalIgnoreCase)
            ? 1 : 0;
        return arguments.Length == offset + 3 &&
               arguments[offset] is StringConstantExpressionAst { Value.Length: > 0 } &&
               arguments[offset + 1] is CommandParameterAst comparison && comparison.Argument is null &&
               QualifiedWhereComparisons.Contains(comparison.ParameterName) &&
               (arguments[offset + 2] is StringConstantExpressionAst or ConstantExpressionAst ||
                arguments[offset + 2] is VariableExpressionAst value && !value.Splatted &&
                (value.VariablePath.IsUnqualified || value.VariablePath.IsLocal));
    }

    private static bool IsQualifiedPipelineUtility(CommandAst command, string name,
        ISet<string> authoredFunctionNames)
    {
        if (authoredFunctionNames.Contains(name) || command.Parent is not PipelineAst pipeline ||
            pipeline.PipelineElements.Count < 2 ||
            PowerShellCommandRegionSemanticBinder.HasPipelineOperators(pipeline))
            return false;
        if (command.CommandElements.Skip(1).OfType<CommandParameterAst>().Any(parameter =>
                !IsQualifiedUtilityParameter(name, parameter.ParameterName)) ||
            command.FindAll(static node => node is CommandAst or AssignmentStatementAst or
                FunctionDefinitionAst or InvokeMemberExpressionAst or
                VariableExpressionAst { Splatted: true } or
                UnaryExpressionAst { TokenKind: TokenKind.PlusPlus or TokenKind.MinusMinus or
                    TokenKind.PostfixPlusPlus or TokenKind.PostfixMinusMinus }, true).Any(node =>
                !ReferenceEquals(node, command)))
            return false;
        var index = pipeline.PipelineElements.IndexOf(command);
        if (index < 1 || pipeline.PipelineElements.Skip(index + 1).Any(element =>
                element is not CommandAst following ||
                following.GetCommandName() is not { } followingName ||
                !QualifiedPipelineUtilityCommands.Contains(followingName)))
            return false;
        return command.FindAll(static node => node is ScriptBlockExpressionAst, true)
            .OfType<ScriptBlockExpressionAst>()
            .All(static expression => IsBoundedUtilityExpression(expression.ScriptBlock));
    }

    private static bool IsQualifiedUtilityParameter(string commandName, string parameterName)
        => commandName.ToLowerInvariant() switch
        {
            "group-object" => parameterName.Equals("Property", StringComparison.OrdinalIgnoreCase),
            "sort-object" => parameterName.Equals("Property", StringComparison.OrdinalIgnoreCase) ||
                             parameterName.Equals("Descending", StringComparison.OrdinalIgnoreCase),
            "select-object" => parameterName.Equals("Property", StringComparison.OrdinalIgnoreCase) ||
                               parameterName.Equals("First", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static bool IsBoundedUtilityExpression(ScriptBlockAst block)
        => block is { DynamicParamBlock: null, BeginBlock: null, ProcessBlock: null, ParamBlock: null,
               EndBlock: { Unnamed: true, Statements.Count: 1 } } &&
           block.GetType().GetProperty("CleanBlock")?.GetValue(block) is null &&
           block.EndBlock.Statements[0] is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandExpressionAst &&
           !PowerShellCommandRegionSemanticBinder.HasPipelineOperators(pipeline) &&
           !block.FindAll(static node => node is CommandAst or AssignmentStatementAst or FunctionDefinitionAst,
               searchNestedScriptBlocks: true).Any() &&
           block.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
               .OfType<VariableExpressionAst>().All(static variable =>
                   variable.VariablePath.UserPath.Equals("_", StringComparison.OrdinalIgnoreCase) ||
                   variable.VariablePath.UserPath.Equals("PSItem", StringComparison.OrdinalIgnoreCase)) &&
           !HasNonlocalTransfer(block);

    private static bool HasNonlocalTransfer(Ast ast)
        => ast.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or
            ContinueStatementAst or ExitStatementAst or ThrowStatementAst or TrapStatementAst,
            searchNestedScriptBlocks: true).Any();
}
