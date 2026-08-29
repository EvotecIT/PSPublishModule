namespace PowerForge;

/// <summary>Shapes an explain request through the same final emitter-routing stages used by artifact builds.</summary>
public static class PowerShellCompilationExplainShaper
{
    /// <summary>Creates a final, artifact-aware explanation without running restore, compilation, or publication.</summary>
    public static PowerShellCompilationExplanation CreateFinalExplanation(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationPlan plan,
        string targetFramework)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(targetFramework)) throw new ArgumentException("A target framework is required.", nameof(targetFramework));
        var shaped = Shape(input, plan, targetFramework);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(
            plan,
            input.Kind,
            shaped,
            input.SourcePath);
        return PowerShellCompilationExplanationService.CreateFinal(plan, ledger);
    }

    private static PowerShellTypedCompilationResult? Shape(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationPlan plan,
        string targetFramework)
    {
        if (plan.Mode is PowerShellCompilationMode.Analyze or PowerShellCompilationMode.Package)
            return null;
        if (plan.Mode == PowerShellCompilationMode.Strict && !plan.CanProceed)
            return null;
        if (input.Kind == PowerShellCompilationArtifactKind.Executable && plan.Mode == PowerShellCompilationMode.Strict)
        {
            var executable = PowerShellTypedExecutableEmitter.Emit(input.SourcePath, input.CompilationSourceFiles, plan, targetFramework);
            return new PowerShellTypedCompilationResult(
                input.SourcePath,
                "PowerForge.Compiled",
                "CompiledPowerShellScript",
                executable.CompiledSource,
                executable.Methods,
                Array.Empty<PowerShellCompilationDiagnostic>(),
                input.CompilationSourceFiles,
                lifecycleSources: null,
                optimization: executable.Optimization,
                irSnapshots: executable.IrSnapshots);
        }

        var transpiler = new PowerShellTypedCompilationTranspiler();
        var typeName = PowerShellCSharpSymbolRenderer.Identifier(input.ArtifactName) + "Methods";
        var typed = input.Kind is PowerShellCompilationArtifactKind.BinaryModule or PowerShellCompilationArtifactKind.Executable
            ? transpiler.TranspileForBinaryModule(input.CompilationSourceFiles, "PowerForge.Compiled", typeName, targetFramework)
            : transpiler.Transpile(input.CompilationSourceFiles, "PowerForge.Compiled", typeName, targetFramework);
        if (plan.Mode == PowerShellCompilationMode.Hybrid &&
            input.Kind is PowerShellCompilationArtifactKind.BinaryModule or PowerShellCompilationArtifactKind.Executable)
        {
            typed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, targetFramework);
        }
        if (input.Kind == PowerShellCompilationArtifactKind.BinaryModule)
        {
            if (plan.Mode == PowerShellCompilationMode.Hybrid)
                typed = PowerShellAdvancedFunctionLifecyclePlanner.AddHostedLifecycleMethods(typed, targetFramework);
            var exportContract = PowerShellModuleExportContract.TryRead(input.SourcePath);
            var exportedFunctions = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName));
            typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions, targetFramework);
        }
        else if (input.Kind == PowerShellCompilationArtifactKind.Executable)
        {
            typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions: null, targetFramework);
        }
        return typed;
    }
}
