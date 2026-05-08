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
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, new[] { "DemoModule.dll" }, handleRuntimes: false);

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
            Assert.DoesNotContain("ProcessArchitecture", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithHandleRuntimes_EmitsRuntimeBootstrapperBlock()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "DemoModule.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, new[] { "DemoModule.dll" }, handleRuntimes: true);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("ProcessArchitecture", bootstrapper);
            Assert.Contains("IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)", bootstrapper);
            Assert.Contains("Lib\\{0}\\runtimes\\{1}\\native", bootstrapper);
            Assert.Contains("$PathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH)) { @() } else { @($env:PATH -split [IO.Path]::PathSeparator) }", bootstrapper);
            Assert.Contains("($PathEntries -notcontains $NativePath)", bootstrapper);
            Assert.Contains("Unknown Windows architecture", bootstrapper);
            Assert.DoesNotContain("\r\n\r\ntry {", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithAssemblyLoadContext_WritesAlcBootstrapperAndSkipsLibrariesScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Default"));
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "DemoModule.dll"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "Dependency.dll"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Lib", "Default", "DemoModule.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-Demo" }, Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("DemoModule.ModuleLoadContext.ModuleAssemblyLoadContext", bootstrapper);
            Assert.Contains("DemoModule.ModuleLoadContext.dll", bootstrapper);
            Assert.Contains("LoadModule($ModuleAssemblyPath, 'DemoModule')", bootstrapper);
            Assert.Contains("AddExportedCmdlet", bootstrapper);
            Assert.DoesNotContain("$LibrariesScript =", bootstrapper);

            Assert.True(File.Exists(Path.Combine(root, "Lib", "Core", "DemoModule.ModuleLoadContext.dll")));
            Assert.False(File.Exists(Path.Combine(root, "Lib", "Default", "DemoModule.ModuleLoadContext.dll")));
            Assert.False(File.Exists(Path.Combine(root, "DemoModule.Libraries.ps1")));
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
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, exportAssemblies: null, handleRuntimes: false);

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
    public void Generate_WithScriptLayoutOnlyAndHandleRuntimes_DoesNotEmitBinaryRuntimeBlock()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-script-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Public"));
        File.WriteAllText(Path.Combine(root, "Public", "Get-Demo.ps1"), "function Get-Demo {}");

        try
        {
            var exports = new ExportSet(new[] { "Get-Demo" }, Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, exportAssemblies: null, handleRuntimes: true);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.DoesNotContain("ProcessArchitecture", bootstrapper);
            Assert.DoesNotContain("IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)", bootstrapper);
            Assert.DoesNotContain("Lib\\{0}\\runtimes\\{1}\\native", bootstrapper);
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
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, exportAssemblies: null, handleRuntimes: false);

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
