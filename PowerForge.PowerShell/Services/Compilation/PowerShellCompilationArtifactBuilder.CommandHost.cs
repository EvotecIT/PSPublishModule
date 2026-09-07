using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void WriteBinaryHostRuntime(string workspace, PowerShellTypedCompilationResult typed)
    {
        foreach (var source in PowerShellCommandHostRuntimeSource.Render(typed))
            File.WriteAllText(Path.Combine(workspace, source.Key), source.Value, new UTF8Encoding(false));
    }
}
