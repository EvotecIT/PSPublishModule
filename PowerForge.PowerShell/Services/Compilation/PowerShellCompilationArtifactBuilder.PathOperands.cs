using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void WritePackagedPathGuardSource(string workspace)
    {
        using var stream = typeof(PowerShellCompilationArtifactBuilder).Assembly.GetManifestResourceStream(
            "PowerForge.PowerShell.Compilation.PowerShellNativeFunctionFileSystemPathGuard.cs")
            ?? throw new InvalidOperationException("Missing packaged provider-path guard runtime source.");
        using var reader = new StreamReader(stream);
        File.WriteAllText(Path.Combine(workspace, "PackagedFileSystemPathGuard.g.cs"),
            "#nullable enable\n" + reader.ReadToEnd(), new UTF8Encoding(false));
    }
}
