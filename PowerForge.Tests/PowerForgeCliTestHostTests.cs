namespace PowerForge.Tests;

public sealed class PowerForgeCliTestHostTests
{
    [Fact]
    public void ExplicitPrebuiltCliSelectionCannotFallBackToAnotherBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeCliTestHost-" + Guid.NewGuid().ToString("N"));
        var sourceCli = Path.Combine(root, "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll");
        var selectedCli = Path.Combine(root, "candidate", "PowerForge.Cli.dll");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceCli)!);
            Directory.CreateDirectory(Path.GetDirectoryName(selectedCli)!);
            File.WriteAllText(sourceCli, "different source build");
            File.WriteAllText(selectedCli, "selected artifacts build");

            Assert.Equal(sourceCli, PowerForgeCliTestHost.ResolveAssemblyPath(root, null));
            Assert.Equal(selectedCli, PowerForgeCliTestHost.ResolveAssemblyPath(root, selectedCli));
            File.Delete(selectedCli);
            var failure = Assert.Throws<FileNotFoundException>(() => PowerForgeCliTestHost.ResolveAssemblyPath(root, selectedCli));
            Assert.Equal(selectedCli, failure.FileName);
            Assert.True(File.Exists(sourceCli));
            Assert.Throws<InvalidOperationException>(() => PowerForgeCliTestHost.ResolveAssemblyPath(root, "PowerForge.Cli.dll"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
