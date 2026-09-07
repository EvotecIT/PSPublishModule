using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void WriteBinaryHostRuntime(string workspace, PowerShellTypedCompilationResult typed)
    {
        if (typed.Methods.Any(static method => method.RequiresPowerShellStatementErrors))
            File.WriteAllText(Path.Combine(workspace, "StatementErrors.g.cs"), PowerShellStatementErrorRuntimeSource.Render(), new UTF8Encoding(false));
        else if (typed.Methods.Any(static method => method.Lifecycle is null && method.Parameters.Any(static parameter => parameter.AcceptsPipelineInput)))
            File.WriteAllText(Path.Combine(workspace, "CommandVariables.g.cs"), PowerShellStatementErrorRuntimeSource.RenderVariableScope(), new UTF8Encoding(false));
    }
}
