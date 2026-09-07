namespace PowerForge;

/// <summary>Defines the complete compiler-owned runtime sources required by generated command methods.</summary>
internal static class PowerShellCommandHostRuntimeSource
{
    internal static IReadOnlyDictionary<string, string> Render(PowerShellTypedCompilationResult typed)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        if (typed.Methods.Any(static method => method.Parameters.Any(static parameter => parameter.TypeName == typeof(string).FullName)))
        {
            using var stream = typeof(PowerShellCommandHostRuntimeSource).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellStringParameterAttribute.cs")
                ?? throw new InvalidOperationException("Missing script string-parameter runtime source.");
            using var reader = new StreamReader(stream);
            sources.Add("StringParameters.g.cs", "#nullable enable\n" + reader.ReadToEnd());
        }
        if (typed.Methods.Any(static method => method.RequiresPowerShellStatementErrors))
            sources.Add("StatementErrors.g.cs", PowerShellStatementErrorRuntimeSource.Render());
        else if (typed.Methods.Any(static method => method.Lifecycle is null && method.Parameters.Any(static parameter => parameter.AcceptsPipelineInput)))
            sources.Add("CommandVariables.g.cs", PowerShellStatementErrorRuntimeSource.RenderVariableScope());
        return sources;
    }
}
