using PowerForge;

public sealed class ModuleBootstrapperGeneratorDesktopTypeAcceleratorTests
{
    [Fact]
    public void Generate_WithTypeAccelerators_WritesDesktopRegistrationAfterLibrariesLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-desktop-types-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "DemoModule.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-Demo" }, Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                assemblyTypeAcceleratorMode: AssemblyTypeAcceleratorExportMode.AllowList,
                assemblyTypeAccelerators: new[] { "Dependency.Widget" });

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("$RegisterPowerForgeDesktopAssemblyTypeAccelerators = {", bootstrapper);
            Assert.Contains("[AppDomain]::CurrentDomain.GetAssemblies()", bootstrapper);
            Assert.Contains("$RegisteredPowerForgeTypeAccelerators = $script:PowerForgeRegisteredAssemblyTypeAccelerators", bootstrapper);
            Assert.Contains("& $RegisterPowerForgeDesktopAssemblyTypeAccelerators -LibraryDirectory ([IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder))", bootstrapper);
            Assert.True(
                bootstrapper.IndexOf(". $LibrariesScript", StringComparison.Ordinal) <
                bootstrapper.IndexOf("& $RegisterPowerForgeDesktopAssemblyTypeAccelerators", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithDevelopmentBinaries_UsesSelectedDesktopBinaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-desktop-development-types-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        Directory.CreateDirectory(moduleRoot);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-Demo" }, Array.Empty<string>());
            var developmentOptions = new ModuleDevelopmentBinaryBootstrapperOptions(
                ModuleDevelopmentBinaryMode.Environment,
                Path.Combine(root, "Sources", "Demo", "bin"),
                "DEMO_USE_DEVELOPMENT_BINARIES",
                "DEMO_DEVELOPMENT_CONFIGURATION",
                new[] { "net8.0", "net472" },
                new[] { "net472", "net8.0" });

            ModuleBootstrapperGenerator.Generate(
                moduleRoot,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                assemblyTypeAcceleratorMode: AssemblyTypeAcceleratorExportMode.AllowList,
                assemblyTypeAccelerators: new[] { "Demo.Dependency" },
                developmentBinaries: developmentOptions);

            var bootstrapper = File.ReadAllText(Path.Combine(moduleRoot, "DemoModule.psm1"));
            Assert.Contains("& $RegisterPowerForgeDesktopAssemblyTypeAccelerators -LibraryDirectory ([IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath))", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
