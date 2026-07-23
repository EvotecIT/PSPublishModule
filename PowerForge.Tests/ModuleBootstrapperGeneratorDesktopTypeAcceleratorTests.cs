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
                assemblyTypeAccelerators: new[] { "Dependency.Widget" },
                ignoreLibrariesOnLoad: new[] { "Ignored.Dependency.dll" });

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("$RegisterPowerForgeDesktopAssemblyTypeAccelerators = {", bootstrapper);
            Assert.Contains("[AppDomain]::CurrentDomain.GetAssemblies()", bootstrapper);
            Assert.Contains("$RegisteredPowerForgeTypeAccelerators = $script:PowerForgeRegisteredAssemblyTypeAccelerators", bootstrapper);
            Assert.Contains("$IgnoredLibraryFileNames = @('Ignored.Dependency.dll')", bootstrapper);
            Assert.Contains("if ($IgnoredLibraryFileNames -contains $File.Name)", bootstrapper);
            Assert.Contains("$TestPowerForgeDesktopIgnoredAssembly = {", bootstrapper);
            Assert.Contains("$ResolvedPowerForgeDesktopAssemblies = @{}", bootstrapper);
            Assert.Contains("$FailedPowerForgeDesktopAssemblies = @{}", bootstrapper);
            Assert.Contains("$AssemblyName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)", bootstrapper);
            Assert.Contains("$TestPowerForgeDesktopAssemblyContentMatch = {", bootstrapper);
            Assert.Contains("[Reflection.AssemblyName]::GetAssemblyName($ExpectedPath)", bootstrapper);
            Assert.Contains("[Security.Cryptography.SHA256]::Create()", bootstrapper);
            Assert.Contains("return $LoadedHash -eq $ExpectedHash", bootstrapper);
            Assert.DoesNotContain("[Diagnostics.FileVersionInfo]", bootstrapper);
            Assert.Contains("& $TestPowerForgeDesktopAssemblyContentMatch -Assembly $Assembly -ExpectedPath $AssemblyPath", bootstrapper);
            Assert.Contains("A module-owned assembly with this identity exists but could not be selected above", bootstrapper);
            Assert.Contains("if ($PSEdition -ne 'Core' -and $PowerForgeDesktopBinaryLoaded)", bootstrapper);
            Assert.Contains("$PowerForgeModuleRoot = $PSScriptRoot", bootstrapper);
            Assert.Contains("$LibrariesScript = [IO.Path]::Combine($PowerForgeModuleRoot, 'DemoModule.Libraries.ps1')", bootstrapper);
            Assert.Contains("$ModuleAssemblyPath = [IO.Path]::Combine($LibRoot, $LibFolder, $Library)", bootstrapper);
            Assert.Contains("& $RegisterPowerForgeDesktopAssemblyTypeAccelerators -LibraryDirectory ([IO.Path]::Combine($LibRoot, $LibFolder))", bootstrapper);
            Assert.True(
                bootstrapper.IndexOf("$PowerForgeModuleRoot = $PSScriptRoot", StringComparison.Ordinal) <
                bootstrapper.IndexOf(". $LibrariesScript", StringComparison.Ordinal));
            Assert.True(
                bootstrapper.IndexOf(". $LibrariesScript", StringComparison.Ordinal) <
                bootstrapper.IndexOf("& $RegisterPowerForgeDesktopAssemblyTypeAccelerators", StringComparison.Ordinal));
            Assert.Contains("    if ($PSEdition -ne 'Core') {\r\n        # Desktop loads module dependencies", bootstrapper);
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
