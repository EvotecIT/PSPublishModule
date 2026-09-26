using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Admits a small, source-checked script-root body to the private Hybrid entry ABI.</summary>
internal static class PowerShellHybridExecutableEntryPlanner
{
    private static readonly HashSet<string> QualifiedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-Date",
        "ConvertTo-Json"
    };

    internal static PowerShellTypedExecutableCompilation? TryPlan(
        string sourcePath,
        IReadOnlyCollection<string> sourcePaths,
        PowerShellCompilationPlan plan,
        string targetFramework,
        string semanticProfileId,
        IEnumerable<PowerShellCompilationCommandProviderContract> commandProviders)
    {
        if (plan.Mode != PowerShellCompilationMode.Hybrid || sourcePaths.Count != 1)
            return null;
        var fullPath = Path.GetFullPath(sourcePath);
        if (!PowerShellCompilationPathSafety.PathEquals(Path.GetFullPath(sourcePaths.Single()), fullPath))
            return null;
        var root = plan.Files.FirstOrDefault(file => PowerShellCompilationPathSafety.PathEquals(file.FullPath, fullPath))?
            .Units.SingleOrDefault(static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        if (root?.IsCompilable != true)
            return null;

        var ast = Parser.ParseFile(fullPath, out _, out var errors);
        if (errors.Length != 0 || ast.UsingStatements.Count != 0 ||
            ast.EndBlock?.Statements.Count != 1 ||
            ast.EndBlock.Statements[0] is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandAst command ||
            ast.ParamBlock?.Attributes.Count > 0)
            return null;
        var parameterNames = ast.ParamBlock?.Parameters.Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!HasPreboundCommandShape(command, parameterNames)) return null;
        if (ast.ParamBlock is not null && ast.ParamBlock.Parameters.Any(parameter =>
                parameter.Attributes.Count != 1 ||
                parameter.Attributes[0] is not TypeConstraintAst constraint ||
                !constraint.TypeName.FullName.Equals("string", StringComparison.OrdinalIgnoreCase) &&
                !constraint.TypeName.FullName.Equals("System.String", StringComparison.OrdinalIgnoreCase) ||
                !IsSimpleParameterName(parameter.Name.VariablePath.UserPath)))
            return null;

        PowerShellTypedExecutableCompilation compiled;
        try
        {
            compiled = PowerShellTypedExecutableCompiler.CompileHybridPreboundEntry(
                fullPath, sourcePaths, plan, targetFramework, semanticProfileId, commandProviders);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        var method = compiled.EntryPointMethod;
        var admitted = compiled.LocalMethods.Length == 0 && method.ReturnType == typeof(void) &&
               method.RequiresPowerShellStatementErrors && method.RequiresPowerShellCommandRegions &&
               !method.RequiresPowerShellStreams && !method.RequiresPowerShellStopping &&
               !method.RequiresPowerShellBoundParameters && !method.RequiresPowerShellRuntimeState &&
               !method.RequiresPowerShellModuleState && !method.RequiresProviderCancellation &&
               compiled.EntryPoint.Parameters.All(static parameter => parameter.ClrType == typeof(string));
        if (!admitted) return null;
        try
        {
            // Explain and Build must agree that the packaged source still contains the exact body selected above.
            PowerShellHybridExecutableEntryEmitter.ComposeScript(
                PowerShellPackagedScriptRewriter.Rewrite(fullPath), fullPath, compiled);
            return compiled;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsSimpleParameterName(string name)
        => name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') &&
           name.Skip(1).All(static character => char.IsLetterOrDigit(character) || character == '_');

    private static bool HasPreboundCommandShape(CommandAst command, ISet<string> parameterNames)
    {
        if (command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0 ||
            command.CommandElements.Count == 0 ||
            command.CommandElements[0] is not StringConstantExpressionAst name ||
            !QualifiedCommands.Contains(name.Value))
            return false;
        return command.CommandElements.Skip(1).All(element => element switch
        {
            CommandParameterAst parameter => parameter.Argument is null || IsSimpleArgument(parameter.Argument, parameterNames),
            _ => IsSimpleArgument(element, parameterNames)
        });
    }

    private static bool IsSimpleArgument(CommandElementAst element, ISet<string> parameterNames)
        => element is StringConstantExpressionAst or ConstantExpressionAst ||
           element is VariableExpressionAst variable &&
           !variable.VariablePath.IsDriveQualified &&
           parameterNames.Contains(variable.VariablePath.UserPath);
}
