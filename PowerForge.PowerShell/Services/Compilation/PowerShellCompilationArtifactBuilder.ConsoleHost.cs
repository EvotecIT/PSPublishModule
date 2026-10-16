using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void WritePackagedConsoleHostSources(string workspace)
    {
        foreach (var name in new[] { "PowerShellPackagedConsoleHost", "PowerShellPackagedConsoleUI", "PowerShellPackagedConsoleErrors" })
        {
            using var stream = typeof(PowerShellCompilationArtifactBuilder).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation." + name + ".cs")
                ?? throw new InvalidOperationException("Missing packaged console runtime source: " + name);
            using var reader = new StreamReader(stream);
            File.WriteAllText(Path.Combine(workspace, name + ".g.cs"), "#nullable enable\n" + reader.ReadToEnd(), new UTF8Encoding(false));
        }
    }
}
