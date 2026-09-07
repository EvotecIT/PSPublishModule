using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void WriteBinaryHostRuntime(string workspace, PowerShellTypedCompilationResult typed)
    {
        if (typed.Methods.Any(static method => method.Parameters.Any(static parameter => parameter.TypeName == typeof(string).FullName)))
        {
            using var stream = typeof(PowerShellCompilationArtifactBuilder).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation.PowerShellStringParameterAttribute.cs")
                ?? throw new InvalidOperationException("Missing script string-parameter runtime source.");
            using var reader = new StreamReader(stream);
            File.WriteAllText(Path.Combine(workspace, "StringParameters.g.cs"), "#nullable enable\n" + reader.ReadToEnd(), new UTF8Encoding(false));
        }
        if (typed.Methods.Any(static method => method.RequiresPowerShellStatementErrors))
            File.WriteAllText(Path.Combine(workspace, "StatementErrors.g.cs"), PowerShellStatementErrorRuntimeSource.Render(), new UTF8Encoding(false));
        else if (typed.Methods.Any(static method => method.Lifecycle is null && method.Parameters.Any(static parameter => parameter.AcceptsPipelineInput)))
            File.WriteAllText(Path.Combine(workspace, "CommandVariables.g.cs"), PowerShellStatementErrorRuntimeSource.RenderVariableScope(), new UTF8Encoding(false));
    }
}
