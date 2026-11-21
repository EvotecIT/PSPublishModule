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
        var shaped = Shape(input, plan, targetFramework, out var entry, commandProviders: commandProviders);
        var emittedMethods = GetEmittedMethods(shaped, entry);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(
            plan,
            input.Kind,
            shaped,
            input.SourcePath,
            emittedMethods: emittedMethods,
            snapshot: input.GetSourceSnapshot(GetProfile(input, plan)));
        return PowerShellCompilationExplanationService.CreateFinal(plan, ledger);
    }

    internal static PowerShellCompiledMethod[] GetEmittedMethods(PowerShellTypedCompilationResult? shaped,
        PowerShellTypedExecutableCompilation? entry)
        => entry is null ? shaped?.Methods ?? Array.Empty<PowerShellCompiledMethod>() :
            (shaped?.Methods.AsEnumerable() ?? Enumerable.Empty<PowerShellCompiledMethod>()).Append(entry.EntryPoint.Method).ToArray();

    internal static PowerShellTypedCompilationResult? Shape(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationPlan plan,
        string targetFramework,
        string? semanticProfileId = null,
        IEnumerable<PowerShellCompilationCommandProviderContract>? commandProviders = null)
        => Shape(input, plan, targetFramework, out _, semanticProfileId, commandProviders);

    internal static PowerShellTypedCompilationResult? Shape(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationPlan plan,
        string targetFramework,
        out PowerShellTypedExecutableCompilation? entry,
        string? semanticProfileId = null,
        IEnumerable<PowerShellCompilationCommandProviderContract>? commandProviders = null)
    {
        entry = null;
        var profile = semanticProfileId ?? GetProfile(input, plan);
        if (plan.Mode is PowerShellCompilationMode.Analyze or PowerShellCompilationMode.Package)
            return null;
        if (plan.Mode == PowerShellCompilationMode.Strict && !plan.CanProceed)
            return null;
        var snapshot = input.GetSourceSnapshot(profile);
        var documents = input.CompilationSourceFiles.Select(snapshot.Get).ToArray();
        if (input.Kind == PowerShellCompilationArtifactKind.Executable && plan.Mode == PowerShellCompilationMode.Strict)
        {
            var executable = PowerShellTypedExecutableEmitter.Emit(input.SourcePath, input.CompilationSourceFiles, plan, targetFramework, profile, commandProviders, documents);
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

        var nativeDependencyTypes = plan.DependencyGraph is null ? PowerShellNativeDependencyTypes.Empty :
            PowerShellNativeDependencyTypes.Create(input.ModuleManifestPath, plan.Dependencies, plan.DependencyGraph, input.ModuleRoot, snapshot);
        var transpiler = new PowerShellTypedCompilationTranspiler(commandProviders ?? Array.Empty<PowerShellCompilationCommandProviderContract>(), profile, nativeDependencyTypes);
        var typeName = PowerShellCSharpSymbolRenderer.Identifier(input.ArtifactName) + "Methods";
        var capabilities = PowerShellCompilationBuildSpec.GetCapabilities(input.Kind, plan.Mode);
        if (input.Kind == PowerShellCompilationArtifactKind.Executable && plan.Mode == PowerShellCompilationMode.Hybrid)
            entry = PowerShellHybridExecutableEntryPlanner.TryPlan(input.SourcePath, input.CompilationSourceFiles, plan,
                targetFramework, profile, commandProviders ?? Array.Empty<PowerShellCompilationCommandProviderContract>(), documents);
        var typed = transpiler.TranspileAnalyzed(documents, plan, "PowerForge.Compiled", typeName, targetFramework, capabilities,
            guardedExecutableEntryPath: input.Kind == PowerShellCompilationArtifactKind.Executable ? input.SourcePath : null,
            nativeScriptEntry: entry?.EntryPointMethod.NativeFunctionBinding is not null);
        if (plan.Mode == PowerShellCompilationMode.Hybrid &&
            input.Kind is PowerShellCompilationArtifactKind.BinaryModule or PowerShellCompilationArtifactKind.Executable)
        {
            typed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, targetFramework, profile, capabilities, nativeDependencyTypes, documents, plan, commandProviders);
        }
        if (input.Kind == PowerShellCompilationArtifactKind.BinaryModule)
        {
            if (plan.Mode == PowerShellCompilationMode.Hybrid)
                typed = PowerShellAdvancedFunctionLifecyclePlanner.AddHostedLifecycleMethods(typed, targetFramework, documents);
            var exportContract = PowerShellModuleExportContract.TryRead(snapshot.Get(input.SourcePath).SyntaxRoot);
            var exportedFunctions = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName));
            typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions, targetFramework, profile, capabilities, documents, plan, transpiler);
        }
        else if (input.Kind == PowerShellCompilationArtifactKind.Executable)
        {
            typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions: null, targetFramework, profile, capabilities, documents, plan, transpiler);
        }
        return typed;
    }

    private static string GetProfile(PowerShellCompilationResolvedInput input, PowerShellCompilationPlan plan)
        => plan.TargetContract?.SemanticProfileId ?? plan.AnalysisSemanticProfileId ?? input.SourceSnapshot.SemanticProfileId;
}
