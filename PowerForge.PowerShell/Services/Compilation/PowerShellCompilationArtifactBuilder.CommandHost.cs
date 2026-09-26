using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void WriteBinaryHostRuntime(
        string workspace,
        PowerShellTypedCompilationResult typed,
        bool requiresExecutableEntryHost = false)
    {
        foreach (var source in PowerShellCommandHostRuntimeSource.Render(typed, requiresExecutableEntryHost))
            File.WriteAllText(Path.Combine(workspace, source.Key), source.Value, new UTF8Encoding(false));
    }
}
