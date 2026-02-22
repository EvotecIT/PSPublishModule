using PowerForge;

public class ModuleBootstrapperGeneratorTests
{
    [Fact]
    public void Generate_WithLibLayout_WritesLibrariesAndBootstrapperFromTemplates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "DemoModule.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(new[] { "Get-Demo" }, Array.Empty<string>(), new[] { "gdemo" });
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, new[] { "DemoModule.dll" });

            var librariesPath = Path.Combine(root, "DemoModule.Libraries.ps1");
            var bootstrapperPath = Path.Combine(root, "DemoModule.psm1");
            Assert.True(File.Exists(librariesPath));
            Assert.True(File.Exists(bootstrapperPath));

            var libraries = File.ReadAllText(librariesPath);
            Assert.Contains("# DemoModule.Libraries.ps1", libraries);
            Assert.Contains("Lib\\Core\\DemoModule.dll", libraries);

            var bootstrapper = File.ReadAllText(bootstrapperPath);
            Assert.Contains("# DemoModule bootstrapper", bootstrapper);
            Assert.Contains("$LibrariesScript = [IO.Path]::Combine($PSScriptRoot, 'DemoModule.Libraries.ps1')", bootstrapper);
            Assert.Contains("$FunctionsToExport = @('Get-Demo')", bootstrapper);
            Assert.Contains("$AliasesToExport = @('gdemo')", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithScriptLayoutOnly_WritesScriptLoaderWithoutBinaryLoader()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Public"));
        File.WriteAllText(Path.Combine(root, "Public", "Get-Demo.ps1"), "function Get-Demo {}");

        try
        {
            var exports = new ExportSet(new[] { "Get-Demo" }, Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, exportAssemblies: null);

            var bootstrapperPath = Path.Combine(root, "DemoModule.psm1");
            Assert.True(File.Exists(bootstrapperPath));
            Assert.False(File.Exists(Path.Combine(root, "DemoModule.Libraries.ps1")));

            var bootstrapper = File.ReadAllText(bootstrapperPath);
            Assert.Contains("$Public  = @(", bootstrapper);
            Assert.DoesNotContain("$LibraryName =", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithoutLibOrScriptFolders_DoesNotOverwriteExistingPsm1()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-no-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var psm1Path = Path.Combine(root, "DemoModule.psm1");
        const string existing = "# existing module content";
        File.WriteAllText(psm1Path, existing);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, exportAssemblies: null);

            var after = File.ReadAllText(psm1Path);
            Assert.Equal(existing, after);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
