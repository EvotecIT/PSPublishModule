namespace PowerForge;

/// <summary>Defines the complete compiler-owned runtime sources required by generated command methods.</summary>
internal static class PowerShellCommandHostRuntimeSource
{
    internal static IReadOnlyDictionary<string, string> Render(
        PowerShellTypedCompilationResult typed,
        bool requiresExecutableEntryHost = false)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var requiresRegionHost = typed.PromotedRegions.Any(static region => region.RequiresLocalOwnershipGuard);
        var requiresRegionControlFlow = typed.PromotedRegions.Any(static region => region.ControlFlowContract is not null);
        var requiresRegionValueAlternative = typed.PromotedRegions.Any(static region =>
            region.ContinuationLocals.Any(static local => local.Alternatives.Count > 0));
        if (requiresRegionControlFlow)
        {
            using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellRegionControlFlowEnvelope.cs")
                ?? throw new InvalidOperationException("Missing retained region control-flow runtime source.");
            using var reader = new StreamReader(stream);
            sources.Add("RegionControlFlow.g.cs", "#nullable enable\n" + reader.ReadToEnd());
        }
        if (requiresRegionValueAlternative)
        {
            using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellRegionValueAlternative.cs")
                ?? throw new InvalidOperationException("Missing retained region value-alternative runtime source.");
            using var reader = new StreamReader(stream);
            sources.Add("RegionValueAlternative.g.cs", "#nullable enable\n" + reader.ReadToEnd());
        }
        if (requiresRegionHost)
        {
            foreach (var name in new[] { "PowerShellRegionLocalOwnership", "PowerShellHybridRegionHost" })
            {
                using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                    "PowerForge.PowerShell.Compilation." + name + ".cs")
                    ?? throw new InvalidOperationException("Missing retained region runtime source: " + name);
                using var reader = new StreamReader(stream);
                sources.Add(name + ".g.cs", "#nullable enable\n" + reader.ReadToEnd());
            }
        }
        if (requiresRegionHost || typed.Methods.Any(static method => method.NativeFunctionBinding is not null))
        {
            foreach (var name in new[] { "PowerShellNativeFunctionHost", "PowerShellNativeFunctionContext", "PowerShellNativeFunctionContext.Operations",
                "PowerShellNativeFunctionContext.Output", "PowerShellNativeFunctionContext.Members", "PowerShellNativeFunctionContext.Indexing",
                "PowerShellNativeFunctionContext.Conversions", "PowerShellNativeFunctionContext.Invocations",
                "PowerShellNativeFunctionContext.CommandRegions", "PowerShellNativeFunctionContext.Compilation",
                "PowerShellNativeFunctionContext.Declarations", "PowerShellNativeFunctionContext.Enumeration",
                "PowerShellNativeFunctionContext.Switch",
                "PowerShellNativeFunctionContext.StringJoin", "PowerShellNativeFunctionContext.ScriptBlocks", "PowerShellNativeFunctionContext.Patterns",
                "PowerShellNativeFunctionContext.Membership" })
            {
                using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                    "PowerForge.PowerShell.Compilation." + name + ".cs")
                    ?? throw new InvalidOperationException("Missing native function runtime source: " + name);
                using var reader = new StreamReader(stream);
                sources.Add(name + ".g.cs", "#nullable enable\n" + reader.ReadToEnd());
            }
        }
        var requiresModuleState = requiresExecutableEntryHost || requiresRegionHost || typed.Methods.Any(PowerShellModuleSessionStatePolicy.RequiresState) ||
            typed.PromotedRegions.Any(static region => region.RequiresPowerShellStopping) ||
            typed.Methods.Any(static method => method.NativeFunctionBinding is not null && method.RequiresPowerShellStatementErrors);
        if (requiresModuleState)
        {
            using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellModuleSessionState.cs")
                ?? throw new InvalidOperationException("Missing module session-state runtime source.");
            using var reader = new StreamReader(stream);
            sources.Add("ModuleSessionState.g.cs", "#nullable enable\n" + reader.ReadToEnd());
        }
        if (typed.Methods.Any(static method => method.NativeFunctionBinding is null && method.Parameters.Any(static parameter => parameter.TypeName == typeof(string).FullName)))
        {
            using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellStringParameterAttribute.cs")
                ?? throw new InvalidOperationException("Missing script string-parameter runtime source.");
            using var reader = new StreamReader(stream);
            sources.Add("StringParameters.g.cs", "#nullable enable\n" + reader.ReadToEnd());
        }
        if (requiresModuleState || typed.Methods.Any(static method => method.NativeFunctionBinding is not null))
        {
            using var outputStream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellNativeOutput.cs")
                ?? throw new InvalidOperationException("Missing native output runtime source.");
            using var outputReader = new StreamReader(outputStream);
            sources.Add("NativeOutput.g.cs", "#nullable enable\n" + outputReader.ReadToEnd());
            using var operationStream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellNativeLanguageOperations.cs")
                ?? throw new InvalidOperationException("Missing native language-operation runtime source.");
            using var operationReader = new StreamReader(operationStream);
            sources.Add("NativeLanguageOperations.g.cs", "#nullable enable\n" + operationReader.ReadToEnd());
            using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellSourceExtent.cs")
                ?? throw new InvalidOperationException("Missing native source-extent runtime source.");
            using var reader = new StreamReader(stream);
            sources.Add("SourceExtent.g.cs", "#nullable enable\n" + reader.ReadToEnd());
        }
        if (requiresModuleState)
            sources.Add("StatementErrors.g.cs", PowerShellStatementErrorRuntimeSource.Render());
        else if (typed.Methods.Any(static method => method.Lifecycle?.Execution != PowerShellCompilationLifecycleExecution.HostedSteppablePipeline && method.Parameters.Any(static parameter => parameter.AcceptsPipelineInput)))
            sources.Add("CommandVariables.g.cs", PowerShellStatementErrorRuntimeSource.RenderVariableScope());
        return sources;
    }
}
