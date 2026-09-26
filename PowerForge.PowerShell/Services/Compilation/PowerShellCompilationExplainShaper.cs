namespace PowerForge;

/// <summary>Shapes an explain request through the same final emitter-routing stages used by artifact builds.</summary>
public static class PowerShellCompilationExplainShaper
{
    /// <summary>Creates a final, artifact-aware explanation without running restore, compilation, or publication.</summary>
    public static PowerShellCompilationExplanation CreateFinalExplanation(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationPlan plan,
        string targetFramework)
        => CreateFinalExplanation(input, plan, targetFramework, Array.Empty<PowerShellCompilationCommandProviderContract>());

    /// <summary>Shapes final decisions with the same resolved provider contracts used by project analysis.</summary>
    public static PowerShellCompilationExplanation CreateFinalExplanation(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationPlan plan,
        string targetFramework,
        IEnumerable<PowerShellCompilationCommandProviderContract> commandProviders)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(targetFramework)) throw new ArgumentException("A target framework is required.", nameof(targetFramework));
        if (commandProviders is null) throw new ArgumentNullException(nameof(commandProviders));
        var shaped = Shape(input, plan, targetFramework, commandProviders: commandProviders);
        var profile = plan.TargetContract?.SemanticProfileId ??
                      PowerShellCompilationTargetContractService.GetDefaultSemanticProfileId(targetFramework);
        var emittedMethods = GetEmittedMethods(input, plan, shaped, targetFramework, profile, commandProviders);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(
            plan,
            input.Kind,
            shaped,
            input.SourcePath,
            emittedMethods: emittedMethods);
        return PowerShellCompilationExplanationService.CreateFinal(plan, ledger);
    }

    internal static PowerShellCompiledMethod[] GetEmittedMethods(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationPlan plan,
        PowerShellTypedCompilationResult? shaped,
        string targetFramework,
        string semanticProfileId,
        IEnumerable<PowerShellCompilationCommandProviderContract> commandProviders)
    {
        var methods = shaped?.Methods.AsEnumerable() ?? Enumerable.Empty<PowerShellCompiledMethod>();
        if (input.Kind == PowerShellCompilationArtifactKind.Executable && plan.Mode == PowerShellCompilationMode.Hybrid)
        {
            var entry = PowerShellHybridExecutableEntryPlanner.TryPlan(
                input.SourcePath, input.CompilationSourceFiles, plan, targetFramework, semanticProfileId, commandProviders);
            if (entry is not null) methods = methods.Append(entry.EntryPoint.Method);
        }
        return methods.ToArray();
    }

    internal static PowerShellTypedCompilationResult? Shape(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationPlan plan,
        string targetFramework,
        string? semanticProfileId = null,
        IEnumerable<PowerShellCompilationCommandProviderContract>? commandProviders = null)
    {
        var profile = semanticProfileId ?? plan.TargetContract?.SemanticProfileId ??
            PowerShellCompilationTargetContractService.GetDefaultSemanticProfileId(targetFramework);
        if (plan.Mode is PowerShellCompilationMode.Analyze or PowerShellCompilationMode.Package)
            return null;
        if (plan.Mode == PowerShellCompilationMode.Strict && !plan.CanProceed)
            return null;
        if (input.Kind == PowerShellCompilationArtifactKind.Executable && plan.Mode == PowerShellCompilationMode.Strict)
        {
            var executable = PowerShellTypedExecutableEmitter.Emit(input.SourcePath, input.CompilationSourceFiles, plan, targetFramework, profile, commandProviders);
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

        var transpiler = new PowerShellTypedCompilationTranspiler(commandProviders ?? Array.Empty<PowerShellCompilationCommandProviderContract>(), profile);
        var typeName = PowerShellCSharpSymbolRenderer.Identifier(input.ArtifactName) + "Methods";
        var capabilities = PowerShellCompilationBuildSpec.GetCapabilities(input.Kind, plan.Mode);
        var typed = input.Kind is PowerShellCompilationArtifactKind.BinaryModule or PowerShellCompilationArtifactKind.Executable
            ? transpiler.TranspileForBinaryModule(input.CompilationSourceFiles, "PowerForge.Compiled", typeName, targetFramework, capabilities)
            : transpiler.Transpile(input.CompilationSourceFiles, "PowerForge.Compiled", typeName, targetFramework);
        if (plan.Mode == PowerShellCompilationMode.Hybrid &&
            input.Kind is PowerShellCompilationArtifactKind.BinaryModule or PowerShellCompilationArtifactKind.Executable)
        {
            typed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, targetFramework, profile, capabilities);
        }
        if (input.Kind == PowerShellCompilationArtifactKind.BinaryModule)
        {
            if (plan.Mode == PowerShellCompilationMode.Hybrid)
                typed = PowerShellAdvancedFunctionLifecyclePlanner.AddHostedLifecycleMethods(typed, targetFramework);
            var exportContract = PowerShellModuleExportContract.TryRead(input.SourcePath);
            var exportedFunctions = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName));
            typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions, targetFramework, profile, capabilities);
        }
        else if (input.Kind == PowerShellCompilationArtifactKind.Executable)
        {
            typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions: null, targetFramework, profile, capabilities);
        }
        return typed;
    }
}
