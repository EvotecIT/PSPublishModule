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
                command.Redirections.Count != 0 || command.GetCommandName() is not string name ||
                !admittedCommands.Contains(name)))
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
}
