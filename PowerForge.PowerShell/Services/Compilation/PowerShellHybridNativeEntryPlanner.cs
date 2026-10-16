using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Selects a source-preserving native root only within its qualified artifact contract.</summary>
internal static class PowerShellHybridNativeEntryPlanner
{
    internal static PowerShellTypedExecutableCompilation? TryPlan(string sourcePath,
        IReadOnlyCollection<string> sourcePaths, PowerShellCompilationPlan plan,
        string targetFramework, string semanticProfileId)
    {
        if (plan.Mode != PowerShellCompilationMode.Hybrid || sourcePaths.Count == 0 ||
            !targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ||
            !semanticProfileId.Equals(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, StringComparison.Ordinal) ||
            plan.TargetContract?.Explicit != true ||
            !string.Equals(plan.TargetContract.RuntimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase))
            return null;
        var path = Path.GetFullPath(sourcePath);
        if (!sourcePaths.Any(source => PowerShellCompilationPathSafety.PathEquals(path, Path.GetFullPath(source)))) return null;
        var root = plan.Files.FirstOrDefault(file => PowerShellCompilationPathSafety.PathEquals(file.FullPath, path))?
            .Units.SingleOrDefault(static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        if (root?.IsCompilable != true) return null;
        var ast = Parser.ParseFile(path, out _, out var errors);
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
        if (PowerShellSourceParser.GetRootFunctionDeclarations(ast).Any(static declaration => declaration.IsConditional) ||
            hostedCommands.Any(command => !IsQualifiedNativeRootCommand(ast, command)))
            return null;
        try
        {
            var compiled = PowerShellTypedExecutableCompiler.CompileHybridNativeEntry(path, sourcePaths, plan, targetFramework, semanticProfileId);
            var method = compiled.EntryPointMethod;
            // Native command regions retain live command lookup and pipeline state. Dependencies
            // are declaration-only and enter this frame through a guarded authored source load.
            return method.NativeFunctionBinding is not null && compiled.LocalMethods.Length == 0 &&
                   !method.RequiresPowerShellModuleState &&
                   !method.RequiresPowerShellRuntimeState && !method.RequiresProviderCancellation
                ? compiled : null;
        }
        catch (InvalidOperationException) { return null; }
    }

    private static bool IsQualifiedNativeRootCommand(ScriptBlockAst root, CommandAst command)
    {
        if (command.Redirections.Count != 0 || command.Parent is PipelineAst pipeline &&
            PowerShellCommandRegionSemanticBinder.HasPipelineOperators(pipeline)) return false;
        if (command.InvocationOperator == TokenKind.Dot)
            return PowerShellCompilationSourcePathPlanner.TryPlan(root, command, allowDerivedPaths: true, out _);
        // Literal commands retain live SDK lookup, arguments, pipelines, streams and callbacks
        // in the existing native command-region owner. This is independent of command names.
        return command.InvocationOperator == TokenKind.Unknown && command.GetCommandName() is not null;
    }

}
