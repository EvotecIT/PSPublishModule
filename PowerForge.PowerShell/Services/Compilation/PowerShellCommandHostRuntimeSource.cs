namespace PowerForge;

/// <summary>Defines the complete compiler-owned runtime sources required by generated command methods.</summary>
internal static class PowerShellCommandHostRuntimeSource
{
    internal static IReadOnlyDictionary<string, string> Render(PowerShellTypedCompilationResult typed)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        if (typed.Methods.Any(static method => method.NativeFunctionBinding is not null))
        {
            foreach (var name in new[] { "PowerShellNativeFunctionHost", "PowerShellNativeFunctionContext", "PowerShellNativeFunctionContext.Operations",
                "PowerShellNativeFunctionContext.Output", "PowerShellNativeFunctionContext.Members", "PowerShellNativeFunctionContext.Indexing",
                "PowerShellNativeFunctionContext.Conversions", "PowerShellNativeFunctionContext.Invocations",
                "PowerShellNativeFunctionContext.CommandRegions", "PowerShellNativeFunctionContext.Compilation",
                "PowerShellNativeFunctionContext.Declarations", "PowerShellNativeFunctionContext.Enumeration" })
            {
                using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                    "PowerForge.PowerShell.Compilation." + name + ".cs")
                    ?? throw new InvalidOperationException("Missing native function runtime source: " + name);
                using var reader = new StreamReader(stream);
                sources.Add(name + ".g.cs", "#nullable enable\n" + reader.ReadToEnd());
            }
        }
        var requiresModuleState = typed.Methods.Any(PowerShellModuleSessionStatePolicy.RequiresState) ||
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
            using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellSourceExtent.cs")
                ?? throw new InvalidOperationException("Missing native source-extent runtime source.");
            using var reader = new StreamReader(stream);
            sources.Add("SourceExtent.g.cs", "#nullable enable\n" + reader.ReadToEnd());
        }
        if (requiresModuleState)
            sources.Add("StatementErrors.g.cs", PowerShellStatementErrorRuntimeSource.Render());
        else if (typed.Methods.Any(static method => method.Lifecycle is null && method.Parameters.Any(static parameter => parameter.AcceptsPipelineInput)))
            sources.Add("CommandVariables.g.cs", PowerShellStatementErrorRuntimeSource.RenderVariableScope());
        return sources;
    }
}
