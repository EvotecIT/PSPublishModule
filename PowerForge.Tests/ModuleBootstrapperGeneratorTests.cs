using System.Reflection.PortableExecutable;
using PowerForge;

public partial class ModuleBootstrapperGeneratorTests
{
    [Theory]
    [InlineData("AMD64", "win-x64")]
    [InlineData("X86", "win-x86")]
    [InlineData("ARM64", "win-arm64")]
    [InlineData("ARM", "win-arm")]
    public void WindowsRuntimeArchitectureResolver_UsesEnvironmentFallbackWithoutWarning(
        string environmentArchitecture,
        string expectedRuntimeFolder)
    {
        var resolver = ModuleBootstrapperGenerator.RenderWindowsRuntimeArchitectureResolver("$Arch", "$ArchFolder")
            .Replace(
                "[string][System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture",
                "[string]$null",
                StringComparison.Ordinal)
            .Replace(
                "[string]$env:PROCESSOR_ARCHITECTURE",
                $"[string]'{environmentArchitecture}'",
                StringComparison.Ordinal);

        using var powerShell = System.Management.Automation.PowerShell.Create();
        powerShell.AddScript(resolver + "\r\n$ArchFolder");

        var output = powerShell.Invoke();

        Assert.False(powerShell.HadErrors);
        Assert.Empty(powerShell.Streams.Warning);
        Assert.Equal(expectedRuntimeFolder, Assert.Single(output).BaseObject);
    }

    [Fact]
    public void WindowsRuntimeArchitectureResolver_UsesProcessBitnessForMissingSignalsWithoutWarning()
    {
        var resolver = ModuleBootstrapperGenerator.RenderWindowsRuntimeArchitectureResolver("$Arch", "$ArchFolder")
            .Replace(
                "[string][System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture",
                "[string]$null",
                StringComparison.Ordinal)
            .Replace(
                "[string]$env:PROCESSOR_ARCHITECTURE",
                "[string]$null",
                StringComparison.Ordinal);

        using var powerShell = System.Management.Automation.PowerShell.Create();
        powerShell.AddScript(resolver + "\r\n$ArchFolder");

        var output = powerShell.Invoke();

        Assert.False(powerShell.HadErrors);
        Assert.Empty(powerShell.Streams.Warning);
        Assert.Equal(IntPtr.Size == 4 ? "win-x86" : "win-x64", Assert.Single(output).BaseObject);
    }

    [Fact]
    public void WindowsRuntimeArchitectureResolver_WarnsForUnrecognizedNonEmptyArchitecture()
    {
        var resolver = ModuleBootstrapperGenerator.RenderWindowsRuntimeArchitectureResolver("$Arch", "$ArchFolder")
            .Replace(
                "[string][System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture",
                "[string]'RISCV64'",
                StringComparison.Ordinal);

        using var powerShell = System.Management.Automation.PowerShell.Create();
        powerShell.AddScript(resolver + "\r\n$ArchFolder");

        var output = powerShell.Invoke();

        Assert.False(powerShell.HadErrors);
        Assert.Contains(
            powerShell.Streams.Warning,
            warning => warning.Message.Contains("Unknown Windows architecture 'RISCV64'", StringComparison.Ordinal));
        Assert.Equal(IntPtr.Size == 4 ? "win-x86" : "win-x64", Assert.Single(output).BaseObject);
    }

    [Fact]
    public void Generate_WithLibLayout_WritesLibrariesAndBootstrapperFromTemplates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        Directory.CreateDirectory(Path.Combine(root, "Public"));
        File.Copy(
            typeof(ModuleBootstrapperGeneratorTests).Assembly.Location,
            Path.Combine(root, "Lib", "Core", "DemoModule.dll"),
            overwrite: true);
        File.WriteAllText(Path.Combine(root, "Public", "Get-Demo.ps1"), "function Get-Demo { 'demo' }");

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
            Assert.Contains("$L -split '[\\\\/]'", libraries);
            Assert.Contains("Add-Type -Path $LibraryPath -ErrorAction Stop", libraries);

            var bootstrapper = File.ReadAllText(bootstrapperPath);
            Assert.Contains("# DemoModule bootstrapper", bootstrapper);
            Assert.Contains("$LibrariesScript = [IO.Path]::Combine($PowerForgeModuleRoot, 'DemoModule.Libraries.ps1')", bootstrapper);
            Assert.Contains("[IO.Path]::Combine($PowerForgeModuleRoot, 'Public', '*.ps1')", bootstrapper);
            Assert.Contains("[IO.Path]::Combine($PowerForgeModuleRoot, '*.psd1')", bootstrapper);
            Assert.True(
                bootstrapper.IndexOf("$PowerForgeModuleRoot = $PSScriptRoot", StringComparison.Ordinal) <
                bootstrapper.IndexOf("[IO.Path]::Combine($PowerForgeModuleRoot, 'Public', '*.ps1')", StringComparison.Ordinal),
                "The module root must be captured before script folders are discovered.");
            Assert.Contains("$FunctionsToExport = @('Get-Demo')", bootstrapper);
            Assert.Contains("$AliasesToExport = @('gdemo')", bootstrapper);
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Core/DemoModule.dll' }", bootstrapper);
            Assert.Contains("$PowerForgeDesktopAssemblyResolver.BootstrapActive = $false", bootstrapper);
            Assert.Contains("$ExecutionContext.SessionState.Module.OnRemove", bootstrapper);
            Assert.Contains("$PowerForgeDesktopAssemblyResolver.Register()", bootstrapper);
            Assert.Contains("$DesktopAssemblyResolver.Unregister()", bootstrapper);
            Assert.DoesNotContain("ProcessArchitecture", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithIgnoredLibrariesOnLoad_OmitsNativeLibrariesFromLibrariesScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-ignore-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Default"));
        File.Copy(
            typeof(ModuleBootstrapperGeneratorTests).Assembly.Location,
            Path.Combine(root, "Lib", "Default", "DemoModule.dll"),
            overwrite: true);
        File.Copy(
            typeof(ModuleBootstrapperGeneratorTests).Assembly.Location,
            Path.Combine(root, "Lib", "Default", "Dependency.dll"),
            overwrite: true);
        File.WriteAllText(Path.Combine(root, "Lib", "Default", "libgcc_s_seh-1.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                ignoreLibrariesOnLoad: new[] { "libgcc_s_seh-1.dll" });

            var libraries = File.ReadAllText(Path.Combine(root, "DemoModule.Libraries.ps1"));
            Assert.Contains("Lib\\Default\\DemoModule.dll", libraries);
            Assert.Contains("Lib\\Default\\Dependency.dll", libraries);
            Assert.True(
                libraries.IndexOf("Lib\\Default\\Dependency.dll", StringComparison.Ordinal) <
                libraries.IndexOf("Lib\\Default\\DemoModule.dll", StringComparison.Ordinal),
                "Private dependencies must be preloaded before the exported module assembly on Desktop PowerShell.");
            Assert.DoesNotContain("libgcc_s_seh-1.dll", libraries);
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
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core", "runtimes", "win-x64", "native"));
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "DemoModule.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, new[] { "DemoModule.dll" }, handleRuntimes: true);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("ProcessArchitecture", bootstrapper);
            Assert.Contains("IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)", bootstrapper);
            Assert.Contains("$NativeLibraryDirectories = @(", bootstrapper);
            Assert.Contains("foreach ($NativeRuntimeRoot in @('Lib/Core'))", bootstrapper);
            Assert.Contains("foreach ($NativeLibraryDirectory in $NativeLibraryDirectories)", bootstrapper);
            Assert.Contains("Join-Path -Path $NativeLibraryDirectory -ChildPath (\"runtimes\\{0}\\native\" -f $ArchFolder)", bootstrapper);
            Assert.Contains("[array] $NativePaths = foreach", bootstrapper);
            Assert.Contains("$PathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH)) { @() } else { @($env:PATH -split [IO.Path]::PathSeparator) }", bootstrapper);
            Assert.Contains("[array] $RemainingPathEntries = foreach ($PathEntry in $PathEntries)", bootstrapper);
            Assert.Contains("if ($NativePaths -notcontains $PathEntry)", bootstrapper);
            Assert.Contains("[array] $OrderedPathEntries = @($NativePaths) + @($RemainingPathEntries)", bootstrapper);
            Assert.Contains("$env:PATH = [string]::Join([IO.Path]::PathSeparator, $OrderedPathEntries)", bootstrapper);
            Assert.Contains("$Arch = [string][System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture", bootstrapper);
            Assert.Contains("$Arch = [string]$env:PROCESSOR_ARCHITECTURE", bootstrapper);
            Assert.Contains("'AMD64' { 'win-x64' }", bootstrapper);
            Assert.Contains("if ([IntPtr]::Size -eq 4) { 'win-x86' } else { 'win-x64' }", bootstrapper);
            Assert.Contains("Unknown Windows architecture", bootstrapper);
            Assert.True(
                bootstrapper.IndexOf("$BinaryModuleDirectories = ", StringComparison.Ordinal) <
                bootstrapper.IndexOf("$env:PATH =", StringComparison.Ordinal),
                "The managed assembly directory must be resolved before native PATH probing.");
            Assert.True(
                bootstrapper.IndexOf("$env:PATH =", StringComparison.Ordinal) <
                bootstrapper.IndexOf("foreach ($BinaryModule in $BinaryModules) {\r\n            try {", StringComparison.Ordinal),
                "Native PATH probing must complete before the binary module import loop.");
            Assert.DoesNotContain("\r\n\r\ntry {", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }





    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithBinaryPayload_ShipsPrecompiledLoaderForBothEditionsInsteadOfPerModuleHelpers()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-ps70-alc-" + Guid.NewGuid().ToString("N"));
        var coreRoot = Path.Combine(root, "Lib", "Core");
        Directory.CreateDirectory(coreRoot);
        File.WriteAllText(Path.Combine(coreRoot, "DemoModule.dll"), string.Empty);

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
                targetFrameworks: new[] { "net8.0", "netstandard2.1", "net472" });

            // PowerShell 7.0 runs on .NET Core 3.1 and Windows PowerShell 5.1 on .NET Framework 4.7.2+.
            AssertLoaderTargetFramework(Path.Combine(root, "Loader", "Core", "PowerForge.ModuleLoader.V1.dll"), ".NETCoreApp,Version=v3.1");
            AssertLoaderTargetFramework(Path.Combine(root, "Loader", "Desktop", "PowerForge.ModuleLoader.V1.dll"), ".NETFramework,Version=v4.7.2");
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Lib"), "*ModuleLoad*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }


    [Fact]
    public void ResolveAssemblyLoadContextTargetDirectories_CoversEverySelectableModernLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-layout-" + Guid.NewGuid().ToString("N"));
        var libRoot = Path.Combine(root, "Lib");
        Directory.CreateDirectory(Path.Combine(libRoot, "Standard"));
        Directory.CreateDirectory(Path.Combine(libRoot, "Core"));
        Directory.CreateDirectory(Path.Combine(libRoot, "Default"));

        try
        {
            var directories = ModuleBootstrapperGenerator.ResolveAssemblyLoadContextTargetDirectories(libRoot);

            Assert.Equal(
                new[] { Path.Combine(libRoot, "Core"), Path.Combine(libRoot, "Standard") },
                directories);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }




    [Fact]
    public void Generate_WithPrebuiltMultipleModernPayloads_DiscoversFoldersAndWritesLibraryMaps()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-multitfm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core-net10.0"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Default"));
        File.Copy(
            typeof(ModuleBootstrapperGeneratorTests).Assembly.Location,
            Path.Combine(root, "Lib", "Core", "DemoModule.dll"),
            overwrite: true);
        File.Copy(
            typeof(ModuleBootstrapperGeneratorTests).Assembly.Location,
            Path.Combine(root, "Lib", "Core-net10.0", "DemoModule.dll"),
            overwrite: true);
        File.Copy(
            typeof(ModuleBootstrapperGeneratorTests).Assembly.Location,
            Path.Combine(root, "Lib", "Default", "DemoModule.dll"),
            overwrite: true);

        try
        {
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                targetFrameworks: Array.Empty<string>());

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            var libraries = File.ReadAllText(Path.Combine(root, "DemoModule.Libraries.ps1"));

            // The build resolves each host's payload; only the .NET version band is left for import time.
            Assert.Contains("if ([Environment]::Version -ge [version]'10.0') {", bootstrapper);
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Core-net10.0/DemoModule.dll' }", bootstrapper);
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Core/DemoModule.dll' }", bootstrapper);
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Default/DemoModule.dll' }", bootstrapper);
            Assert.DoesNotContain("Get-ChildItem", bootstrapper);
            Assert.Contains("if ([Environment]::Version -ge [version]'10.0') {", libraries);
            Assert.Contains("Lib\\Core-net10.0\\DemoModule.dll", libraries);
            Assert.Contains("Lib\\Default\\DemoModule.dll", libraries);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithOnlyNamedCorePayload_AllowsRuntimeSelectionBeforeEmptyLayoutGuard()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-named-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core-net10.0"));
        File.WriteAllText(Path.Combine(root, "Lib", "Core-net10.0", "DemoModule.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, new[] { "DemoModule.dll" }, handleRuntimes: false);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("if ([Environment]::Version -ge [version]'10.0') {", bootstrapper);
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Core-net10.0/DemoModule.dll' }", bootstrapper);
            Assert.Contains("@{ Name = 'DemoModule.dll'; Error = 'No compatible PowerShell Core assemblies found' }", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_WithDefaultAndNamedCorePayload_FallsBackToDefaultBelowNamedCoreRuntime(bool useAssemblyLoadContext)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-default-named-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Default"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core-net10.0"));
        File.WriteAllText(Path.Combine(root, "Lib", "Default", "DemoModule.dll"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Lib", "Core-net10.0", "DemoModule.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: useAssemblyLoadContext);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            var belowNamedCore = bootstrapper.Substring(bootstrapper.IndexOf("[Environment]::Version -ge [version]'10.0'", StringComparison.Ordinal));
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Core-net10.0/DemoModule.dll' }", belowNamedCore);
            belowNamedCore = belowNamedCore.Substring(belowNamedCore.IndexOf("} else {", StringComparison.Ordinal));
            Assert.StartsWith("} else {\r\n                @(\r\n                    @{ Name = 'DemoModule.dll'; Path = 'Lib/Default/DemoModule.dll' }", belowNamedCore);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveAssemblyLoadContextTargetDirectories_UsesRootLibForRootLevelBinaryLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-root-layout-" + Guid.NewGuid().ToString("N"));
        var libRoot = Directory.CreateDirectory(Path.Combine(root, "Lib")).FullName;
        File.WriteAllText(Path.Combine(libRoot, "DemoModule.dll"), string.Empty);

        try
        {
            var directories = ModuleBootstrapperGenerator.ResolveAssemblyLoadContextTargetDirectories(libRoot);

            Assert.Equal(new[] { libRoot }, directories);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveAssemblyLoadContextTargetDirectories_CoversEveryRuntimeFallbackForConfiguredAssemblies()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-configured-layout-" + Guid.NewGuid().ToString("N"));
        var libRoot = Directory.CreateDirectory(Path.Combine(root, "Lib")).FullName;
        var coreRoot = Directory.CreateDirectory(Path.Combine(libRoot, "Core")).FullName;
        var defaultRoot = Directory.CreateDirectory(Path.Combine(libRoot, "Default")).FullName;
        File.WriteAllText(Path.Combine(coreRoot, "DemoModule.dll"), string.Empty);
        File.WriteAllText(Path.Combine(defaultRoot, "DemoModule.dll"), string.Empty);
        File.WriteAllText(Path.Combine(libRoot, "ExtraModule.dll"), string.Empty);

        try
        {
            var directories = ModuleBootstrapperGenerator.ResolveAssemblyLoadContextTargetDirectories(
                libRoot,
                new[] { "DemoModule.dll", "ExtraModule.dll" });

            Assert.Equal(new[] { coreRoot, defaultRoot, libRoot }, directories);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithAssemblyLoadContext_WritesAlcBootstrapperAndKeepsDesktopLibrariesScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Default"));
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "DemoModule.dll"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "Dependency.dll"), string.Empty);
        File.WriteAllText(
            Path.Combine(root, "Lib", "Core", ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName),
            "net8.0");
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
            Assert.Contains("[PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext]::LoadModuleFromGroup(", bootstrapper);
            Assert.Contains("'PowerForge.ModuleLoader.V1.dll'", bootstrapper);
            Assert.Contains("[string[]]$BinaryModulePaths,", bootstrapper);
            Assert.Contains("$ModuleAssemblyPath,", bootstrapper);
            Assert.Contains("-PassThru -ErrorAction Stop", bootstrapper);
            Assert.Contains("Export-ModuleMember -Cmdlet ([string[]]@($InnerModule.ExportedCmdlets.Keys)) -Alias ([string[]]@($InnerModule.ExportedAliases.Keys))", bootstrapper);
            foreach (var edrSignal in new[] { "NonPublic", "AddExportedCmdlet", "Add-Type -TypeDefinition", "GetType('System.Management", "LoadFrom(", "Threading.Monitor" })
                Assert.DoesNotContain(edrSignal, bootstrapper);
            Assert.Contains("$PSEdition -ne 'Core'", bootstrapper);
            Assert.Contains("$PowerForgeDesktopAssemblyResolver.Register()", bootstrapper);
            Assert.Contains("$DesktopAssemblyResolver.Unregister()", bootstrapper);
            Assert.Contains("$LibrariesScript = [IO.Path]::Combine($PowerForgeModuleRoot, 'DemoModule.Libraries.ps1')", bootstrapper);
            Assert.True(
                bootstrapper.IndexOf(". $LibrariesScript", StringComparison.Ordinal) <
                bootstrapper.IndexOf("& $ImportModule $ModuleAssemblyPath", StringComparison.Ordinal),
                "Desktop dependencies must load before the exported binary module.");

            Assert.True(File.Exists(Path.Combine(root, "Loader", "Core", "PowerForge.ModuleLoader.V1.dll")));
            Assert.True(File.Exists(Path.Combine(root, "Loader", "Desktop", "PowerForge.ModuleLoader.V1.dll")));
            Assert.True(File.Exists(Path.Combine(root, "DemoModule.Libraries.ps1")));

            var libraries = File.ReadAllText(Path.Combine(root, "DemoModule.Libraries.ps1"));
            Assert.DoesNotContain("PowerForge.ModuleLoader", libraries);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithAssemblyLoadContext_LoadsInterdependentExportsIntoOneContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-export-group-" + Guid.NewGuid().ToString("N"));
        var libCore = Path.Combine(root, "Lib", "Core");
        Directory.CreateDirectory(libCore);

        try
        {
            var sharedPath = BuildFixtureProject(
                root,
                "SharedExportProject",
                "SharedExport",
                """
                namespace SharedExport;

                public sealed class SharedValue
                {
                }
                """);
            var primaryPath = BuildFixtureProject(
                root,
                "PrimaryExportProject",
                "PrimaryExport",
                """
                namespace PrimaryExport;

                public static class Entry
                {
                    public static void Accept(SharedExport.SharedValue value) { }
                }
                """,
                new[] { sharedPath });

            var packagedPrimary = Path.Combine(libCore, "PrimaryExport.dll");
            var packagedShared = Path.Combine(root, "Lib", "SharedExport.dll");
            File.Copy(primaryPath, packagedPrimary, overwrite: true);
            File.Copy(sharedPath, packagedShared, overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "PrimaryExport.dll", "SharedExport.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            var contextType = LoadCompiledLoaderContextType(root);
            var loadFromGroup = contextType.GetMethod("LoadModuleFromGroup", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var group = new[] { packagedPrimary, packagedShared };
            var loaded = group
                .Select(path => (System.Reflection.Assembly)loadFromGroup.Invoke(null, new object?[] { group, path, "DemoModule", null })!)
                .ToArray();

            Assert.Equal(2, loaded.Length);
            var parameterType = loaded[0]
                .GetType("PrimaryExport.Entry", throwOnError: true)!
                .GetMethod("Accept", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .GetParameters()[0]
                .ParameterType;
            var sharedType = loaded[1].GetType("SharedExport.SharedValue", throwOnError: true)!;

            Assert.Same(sharedType, parameterType);
            Assert.Same(
                System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(loaded[0]),
                System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(loaded[1]));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); } catch { /* generated loader assembly remains locked after reflection load */ }
            }
        }
    }




    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithAssemblyLoadContext_ResolvesNestedRuntimeAssetFromDepsJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-deps-" + Guid.NewGuid().ToString("N"));
        var libCore = Path.Combine(root, "Lib", "Core");
        Directory.CreateDirectory(libCore);

        try
        {
            var dependencyPath = BuildFixtureProject(
                root,
                "NestedDependency",
                "NestedDependency",
                """
                namespace NestedDependency;

                public static class Marker
                {
                    public static string Value => "deps";
                }
                """);

            var competingRuntimeDependencyPath = BuildFixtureProject(
                root,
                "NestedDependencyCompetingRuntime",
                "NestedDependency",
                """
                namespace NestedDependency;

                public static class Marker
                {
                    public static string Value => "wrong-runtime";
                }
                """);

            var modulePath = BuildFixtureProject(
                root,
                "DemoModule",
                "DemoModule",
                """
                namespace DemoModule;

                public static class Entry
                {
                    public static string Read() => NestedDependency.Marker.Value;
                }
                """,
                new[] { dependencyPath });

            File.Copy(modulePath, Path.Combine(libCore, "DemoModule.dll"), overwrite: true);
            var nestedDependencyPath = Path.Combine(libCore, "lib", "net8.0", "NestedDependency.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(nestedDependencyPath)!);
            File.Copy(dependencyPath, nestedDependencyPath, overwrite: true);
            var competingRuntimePath = Path.Combine(libCore, "runtimes", GetCurrentRuntimeAssetRid(), "lib", "net9.0", "NestedDependency.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(competingRuntimePath)!);
            File.Copy(competingRuntimeDependencyPath, competingRuntimePath, overwrite: true);
            WriteDepsJson(Path.Combine(libCore, "DemoModule.deps.json"));

            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-Demo" }, Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            var contextType = LoadCompiledLoaderContextType(root);
            var resolverType = contextType.Assembly.GetType("PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext+DependencyManifestResolver", throwOnError: true)!;
            var tryCreate = resolverType.GetMethod("TryCreate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var resolver = tryCreate.Invoke(null, new object[] { Path.Combine(libCore, "DemoModule.dll") });
            Assert.NotNull(resolver);

            var resolveAssembly = resolverType.GetMethod("ResolveAssemblyToPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
            var resolved = (string?)resolveAssembly.Invoke(resolver, new object[] { new System.Reflection.AssemblyName("NestedDependency") });

            Assert.Equal(nestedDependencyPath, resolved);

            var loadModule = contextType.GetMethod("LoadModule", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var moduleAssembly = (System.Reflection.Assembly)loadModule.Invoke(null, new object?[] { Path.Combine(libCore, "DemoModule.dll"), "DemoModule" })!;
            var value = moduleAssembly.GetType("DemoModule.Entry", throwOnError: true)!
                .GetMethod("Read", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null);

            Assert.Equal("deps", value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); } catch { /* generated loader assembly remains locked after reflection load */ }
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithAssemblyLoadContext_ResolvesPackagedRuntimeAssemblyWithoutDepsJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-runtime-" + Guid.NewGuid().ToString("N"));
        var libCore = Path.Combine(root, "Lib", "Core");
        Directory.CreateDirectory(libCore);

        try
        {
            var runtimeDependencyPath = BuildFixtureProject(
                root,
                "NestedDependencyRuntime",
                "NestedDependency",
                """
                namespace NestedDependency;

                public static class Marker
                {
                    public static string Value => "runtime-probe";
                }
                """);

            var facadeDependencyPath = BuildFixtureProject(
                root,
                "NestedDependencyFacade",
                "NestedDependency",
                """
                namespace NestedDependency;

                public static class Marker
                {
                    public static string Value => "facade";
                }
                """);

            var modulePath = BuildFixtureProject(
                root,
                "DemoModule",
                "DemoModule",
                """
                namespace DemoModule;

                public static class Entry
                {
                    public static string Read() => NestedDependency.Marker.Value;
                }
                """,
                new[] { facadeDependencyPath });

            File.Copy(modulePath, Path.Combine(libCore, "DemoModule.dll"), overwrite: true);
            File.Copy(facadeDependencyPath, Path.Combine(libCore, "NestedDependency.dll"), overwrite: true);
            var nestedDependencyPath = Path.Combine(libCore, "runtimes", GetCurrentRuntimeAssetRid(), "lib", "net8.0", "NestedDependency.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(nestedDependencyPath)!);
            File.Copy(runtimeDependencyPath, nestedDependencyPath, overwrite: true);

            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-Demo" }, Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            var contextType = LoadCompiledLoaderContextType(root);
            var loadModule = contextType.GetMethod("LoadModule", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var moduleAssembly = (System.Reflection.Assembly)loadModule.Invoke(null, new object?[] { Path.Combine(libCore, "DemoModule.dll"), "DemoModule" })!;
            var value = moduleAssembly.GetType("DemoModule.Entry", throwOnError: true)!
                .GetMethod("Read", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null);

            Assert.Equal("runtime-probe", value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); } catch { /* generated loader assembly remains locked after reflection load */ }
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithAssemblyLoadContextAndDefaultOnlyLib_WritesLoaderBesideDefaultAssembly()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Default"));
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
            Assert.Contains("$BinaryModules = @(\r\n            @{ Name = 'DemoModule.dll'; Path = 'Lib/Default/DemoModule.dll' }\r\n        )", bootstrapper);
            Assert.True(File.Exists(Path.Combine(root, "Loader", "Core", "PowerForge.ModuleLoader.V1.dll")));

            var libraries = File.ReadAllText(Path.Combine(root, "DemoModule.Libraries.ps1"));
            Assert.DoesNotContain("PowerForge.ModuleLoader", libraries);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithRootPrimaryAndNestedAuxiliary_WritesAlcLoaderBesidePrimaryAssembly()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-root-primary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        File.WriteAllText(Path.Combine(root, "Lib", "DemoModule.dll"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "Dependency.dll"), string.Empty);

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

            Assert.True(File.Exists(Path.Combine(root, "Loader", "Core", "PowerForge.ModuleLoader.V1.dll")));
            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/DemoModule.dll' }", bootstrapper);
            Assert.Contains("RegisterFromLoadContext(\r\n                            $ModuleAssembly,\r\n                            $LibraryDirectory,", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithMultipleConfiguredAssemblies_ImportsEveryAssemblyAndPrefersModuleName()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-multiple-exports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib"));
        File.WriteAllText(Path.Combine(root, "Lib", "DemoModule.DLL"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Lib", "Auxiliary.dll"), string.Empty);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-Demo" }, Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                exports,
                new[] { "Auxiliary.dll", "DemoModule.dll" },
                handleRuntimes: false);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains(
                "@{ Name = 'DemoModule.dll'; Path = 'Lib/DemoModule.DLL' }\r\n            @{ Name = 'Auxiliary.dll'; Path = 'Lib/Auxiliary.dll' }",
                bootstrapper);
            Assert.Contains("foreach ($BinaryModule in $BinaryModules) {", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("./Lib/Plugins/DemoModule.dll")]
    [InlineData(@".\Lib\Plugins\DemoModule.dll")]
    [Trait("Category", "Integration")]
    public void Generate_WithPathQualifiedExportAssembly_PreservesRelativeLocation(string configuredReference)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-qualified-export-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Directory.CreateDirectory(Path.Combine(root, "Lib", "Plugins")).FullName;
        File.WriteAllText(Path.Combine(pluginRoot, "DemoModule.DLL"), string.Empty);

        try
        {
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { configuredReference },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("@{ Name = 'Plugins/DemoModule.dll'; Path = 'Lib/Plugins/DemoModule.DLL' }", bootstrapper);
            Assert.True(File.Exists(Path.Combine(root, "Loader", "Core", "PowerForge.ModuleLoader.V1.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithUnqualifiedNestedExportAssembly_ResolvesUniqueRecursiveMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-unqualified-nested-export-" + Guid.NewGuid().ToString("N"));
        var fixtureRoot = Path.Combine(root, "Fixture");
        var moduleRoot = Path.Combine(root, "Module");
        var pluginRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Plugins")).FullName;
        Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Core-net99.0"));
        File.WriteAllText(Path.Combine(moduleRoot, "Lib", "Core-net99.0", "Auxiliary.dll"), string.Empty);

        try
        {
            var fixtureAssembly = BuildFixtureProject(
                fixtureRoot,
                "NestedBinaryFixture",
                "DemoModule",
                "namespace NestedBinaryFixture; public static class Marker { public static string Value => \"nested\"; }");
            File.Copy(fixtureAssembly, Path.Combine(pluginRoot, "DemoModule.dll"), overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                moduleRoot,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            var bootstrapperPath = Path.Combine(moduleRoot, "DemoModule.psm1");
            var bootstrapper = File.ReadAllText(bootstrapperPath);
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Plugins/DemoModule.dll' }", bootstrapper);
            Assert.DoesNotContain("-Recurse", bootstrapper);
            Assert.True(File.Exists(Path.Combine(moduleRoot, "Loader", "Core", "PowerForge.ModuleLoader.V1.dll")));

            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add("-NoLogo");
            processStartInfo.ArgumentList.Add("-NoProfile");
            processStartInfo.ArgumentList.Add("-NonInteractive");
            processStartInfo.ArgumentList.Add("-ExecutionPolicy");
            processStartInfo.ArgumentList.Add("Bypass");
            processStartInfo.ArgumentList.Add("-Command");
            processStartInfo.ArgumentList.Add(
                "Import-Module -Name '" + bootstrapperPath.Replace("'", "''", StringComparison.Ordinal) + "' -Force -ErrorAction Stop");

            using var process = System.Diagnostics.Process.Start(processStartInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"Generated module import failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithAmbiguousNestedExportAssembly_FailsTheBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-ambiguous-nested-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "PluginsA"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "PluginsB"));
        File.WriteAllText(Path.Combine(root, "Lib", "PluginsA", "DemoModule.dll"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Lib", "PluginsB", "DemoModule.dll"), string.Empty);

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false));

            Assert.Contains("matched multiple nested Lib payloads", error.Message, StringComparison.Ordinal);
            Assert.Contains("Lib/PluginsA/DemoModule.dll, Lib/PluginsB/DemoModule.dll", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "DemoModule.psm1")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void InlineMergedScriptPayload_ExecutesFunctionsPrependedBeforeFirstSourceMarker()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-prepended-source-" + Guid.NewGuid().ToString("N"));
        var fixtureRoot = Path.Combine(root, "Fixture");
        var moduleRoot = Path.Combine(root, "Module");
        var libRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Core")).FullName;
        var publicRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Public")).FullName;

        try
        {
            var fixtureAssembly = BuildFixtureProject(
                fixtureRoot,
                "PrependedSourceFixture",
                "DemoModule",
                "namespace PrependedSourceFixture; public static class Marker { public static string Value => \"binary\"; }");
            File.Copy(fixtureAssembly, Path.Combine(libRoot, "DemoModule.dll"), overwrite: true);
            File.WriteAllText(
                Path.Combine(publicRoot, "Get-Original.ps1"),
                "function Get-Original { 'original' }");

            var exports = new ExportSet(
                new[] { "Get-Recovered", "Get-Original" },
                Array.Empty<string>(),
                Array.Empty<string>());
            var sources = ModuleMergeComposer.BuildSources(
                moduleRoot,
                "DemoModule",
                information: null,
                exports,
                fixRelativePaths: false,
                exportAssemblies: new[] { "DemoModule.dll" });
            var merged = ModuleMergeComposer.PrependFunctions(
                new[] { "function Get-Recovered { 'recovered' }" },
                sources.MergedScriptContent);

            ModuleBootstrapperGenerator.Generate(
                moduleRoot,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: false);
            var bootstrapperPath = Path.Combine(moduleRoot, "DemoModule.psm1");
            ModuleBootstrapperGenerator.InlineMergedScriptPayload(bootstrapperPath, merged);

            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add("-NoLogo");
            processStartInfo.ArgumentList.Add("-NoProfile");
            processStartInfo.ArgumentList.Add("-NonInteractive");
            processStartInfo.ArgumentList.Add("-ExecutionPolicy");
            processStartInfo.ArgumentList.Add("Bypass");
            processStartInfo.ArgumentList.Add("-Command");
            processStartInfo.ArgumentList.Add(
                "Import-Module -Name '" + bootstrapperPath.Replace("'", "''", StringComparison.Ordinal) +
                "' -Force -ErrorAction Stop; '{0},{1}' -f (Get-Recovered), (Get-Original)");

            using var process = System.Diagnostics.Process.Start(processStartInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"Generated module import failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
            Assert.Contains("recovered,original", standardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithAbsoluteExportAssembly_UsesPackagedFileName()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-absolute-export-" + Guid.NewGuid().ToString("N"));
        var libRoot = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core")).FullName;
        File.WriteAllText(Path.Combine(libRoot, "DemoModule.dll"), string.Empty);
        var configuredReference = Path.Combine(root, "build-output", "DemoModule.dll");

        try
        {
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { configuredReference },
                handleRuntimes: false);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Core/DemoModule.dll' }", bootstrapper, StringComparison.Ordinal);
            Assert.DoesNotContain(configuredReference, bootstrapper, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("Artifacts/DemoModule.dll")]
    [InlineData(@"bin\Release\DemoModule.dll")]
    public void Generate_WithProjectRelativeExportAssembly_UsesPackagedFileName(string configuredReference)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-relative-export-" + Guid.NewGuid().ToString("N"));
        var libRoot = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core")).FullName;
        File.WriteAllText(Path.Combine(libRoot, "DemoModule.dll"), string.Empty);

        try
        {
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { configuredReference },
                handleRuntimes: false);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("@{ Name = 'DemoModule.dll'; Path = 'Lib/Core/DemoModule.dll' }", bootstrapper, StringComparison.Ordinal);
            Assert.DoesNotContain(configuredReference, bootstrapper, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(AssemblyTypeAcceleratorExportMode.Assembly)]
    [InlineData(AssemblyTypeAcceleratorExportMode.Enums)]
    [InlineData(AssemblyTypeAcceleratorExportMode.AllowList)]
    public void BuildTypeAcceleratorBlock_DelegatesConfiguredSurfaceToCompiledLoader(AssemblyTypeAcceleratorExportMode mode)
    {
        var block = ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(
            mode,
            new[] { "Dependency.Widget" },
            new[] { "Dependency" },
            "$LibraryDirectory");

        Assert.Contains("[PowerForge.ModuleLoader.V1.TypeAcceleratorRegistration]::RegisterFromLoadContext(", block);
        Assert.Contains("'" + mode + "'", block);
        Assert.Contains("[string[]]@('Dependency.Widget')", block);
        Assert.Contains("[string[]]@('Dependency')", block);
        Assert.Contains("$PowerForgeTypeAcceleratorOwner.Unregister()", block);
        Assert.DoesNotContain("NonPublic", block);
        Assert.DoesNotContain("GetType('System.Management.Automation", block);
    }

    [Fact]
    public void Generate_WithTypeAcceleratorModeNone_DoesNotInferFromConfiguredLists()
    {
        var block = ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(
            AssemblyTypeAcceleratorExportMode.None,
            new[] { "Dependency.Widget" },
            Array.Empty<string>(),
            "$LibraryDirectory");

        Assert.Equal(string.Empty, block);
    }

    [Fact]
    public void Generate_WithAssemblyLoadContextTypeAcceleratorModeNone_DoesNotWriteTypeAcceleratorComment()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-none-" + Guid.NewGuid().ToString("N"));
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
                assemblyTypeAcceleratorMode: AssemblyTypeAcceleratorExportMode.None);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.DoesNotContain("Type accelerator registration relies on", bootstrapper);
            Assert.DoesNotContain("TypeAcceleratorRegistration", bootstrapper);
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
            Assert.Contains("$Public  = [string[]]@(", bootstrapper);
            Assert.Contains("[IO.Path]::Combine($PSScriptRoot, 'Public', '*.ps1')", bootstrapper);
            Assert.Contains("[Array]::Sort($Public, [StringComparer]::Ordinal)", bootstrapper);
            Assert.Contains("[Array]::Sort($Private, [StringComparer]::Ordinal)", bootstrapper);
            Assert.Contains("[Array]::Sort($Classes, [StringComparer]::Ordinal)", bootstrapper);
            Assert.Contains("[Array]::Sort($Enums, [StringComparer]::Ordinal)", bootstrapper);
            Assert.True(
                bootstrapper.IndexOf("$Enums + $Classes", StringComparison.Ordinal) >= 0,
                "Enums must be dot-sourced before classes so class declarations can reference enum types.");
            Assert.DoesNotContain("$PowerForgeModuleRoot", bootstrapper);
            Assert.DoesNotContain("$LibraryName =", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithAuxiliaryLibraryOnly_KeepsScriptModuleBootstrapper()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-auxiliary-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Public"));
        Directory.CreateDirectory(Path.Combine(root, "Lib"));
        File.WriteAllText(Path.Combine(root, "Public", "Get-Demo.ps1"), "function Get-Demo {}");
        File.WriteAllText(Path.Combine(root, "Lib", "Auxiliary.DLL"), string.Empty);

        try
        {
            var exports = new ExportSet(new[] { "Get-Demo" }, Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, exportAssemblies: null, handleRuntimes: false);

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("$Public  = [string[]]@(", bootstrapper, StringComparison.Ordinal);
            Assert.DoesNotContain("$LibraryName =", bootstrapper, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "DemoModule.Libraries.ps1")));
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

    [Fact]
    public void Generate_WithUnconfiguredAssemblyInNonPayloadFolder_DoesNotOverwriteExistingPsm1()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-unconfigured-nested-" + Guid.NewGuid().ToString("N"));
        var unrelatedDirectory = Path.Combine(root, "Lib", "Artifacts");
        Directory.CreateDirectory(unrelatedDirectory);
        File.WriteAllText(Path.Combine(unrelatedDirectory, "DemoModule.dll"), string.Empty);

        var psm1Path = Path.Combine(root, "DemoModule.psm1");
        const string existing = "# existing module content";
        File.WriteAllText(psm1Path, existing);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(root, "DemoModule", exports, exportAssemblies: null, handleRuntimes: false);

            Assert.Equal(existing, File.ReadAllText(psm1Path));
            Assert.False(File.Exists(Path.Combine(root, "DemoModule.Libraries.ps1")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithDevelopmentBinariesAndScriptFolders_CapturesModuleRootBeforeDevelopmentBranch()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "pf-bootstrapper-dev-script-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Core"));
        Directory.CreateDirectory(Path.Combine(moduleRoot, "Public"));
        File.WriteAllText(
            Path.Combine(moduleRoot, "Lib", "Core", "DemoModule.dll"),
            string.Empty);
        File.WriteAllText(
            Path.Combine(moduleRoot, "Public", "Get-Demo.ps1"),
            "function Get-Demo { 'demo' }");

        try
        {
            var exports = new ExportSet(
                new[] { "Get-Demo" },
                Array.Empty<string>(),
                Array.Empty<string>());
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
                useAssemblyLoadContext: false,
                developmentBinaries: developmentOptions);

            var bootstrapper = File.ReadAllText(
                Path.Combine(moduleRoot, "DemoModule.psm1"));
            var rootCapture = bootstrapper.IndexOf(
                "$PowerForgeModuleRoot = $PSScriptRoot",
                StringComparison.Ordinal);
            var developmentBranch = bootstrapper.IndexOf(
                "$PowerForgeDevelopmentBinaryLoaded = $false",
                StringComparison.Ordinal);
            var scriptDiscovery = bootstrapper.IndexOf(
                "[IO.Path]::Combine($PowerForgeModuleRoot, 'Public', '*.ps1')",
                StringComparison.Ordinal);

            Assert.True(rootCapture >= 0);
            Assert.True(rootCapture < developmentBranch);
            Assert.True(developmentBranch < scriptDiscovery);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithDevelopmentBinaries_WritesSourceBootstrapperWithoutPackagedLib()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-dev-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        Directory.CreateDirectory(moduleRoot);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-Demo" }, new[] { "demo" });
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
                conditionalFunctionDependencies: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Required.Demo"] = new[] { "Get-Demo" }
                },
                developmentBinaries: developmentOptions);

            var bootstrapperPath = Path.Combine(moduleRoot, "DemoModule.psm1");
            Assert.True(File.Exists(bootstrapperPath));

            var bootstrapper = File.ReadAllText(bootstrapperPath);
            Assert.Contains("# Auto-generated by PowerForge. Do not edit.", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentBinaryMode = 'Environment'", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentBinaryEnvironmentVariable = 'DEMO_USE_DEVELOPMENT_BINARIES'", bootstrapper);
            Assert.Contains("[IO.Path]::Combine($PSScriptRoot, '..', 'Sources', 'Demo', 'bin')", bootstrapper);
            Assert.DoesNotContain("Add-Type -TypeDefinition", bootstrapper);
            Assert.Contains("[IO.Path]::Combine($PowerForgeDevelopmentBinaryRoot, 'PowerForge.ModuleLoader', $PowerForgeLoaderEdition, 'PowerForge.ModuleLoader.V1.dll')", bootstrapper);
            Assert.Contains("[PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext]::LoadModuleFromGroup(", bootstrapper);
            Assert.Contains("'DemoModule.Development'", bootstrapper);
            Assert.Contains("[IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath),", bootstrapper);
            Assert.Contains("[string[]]@('Demo.Dependency')", bootstrapper);
            Assert.True(File.Exists(Path.Combine(developmentOptions.BinaryRootPath, "PowerForge.ModuleLoader", "Core", "PowerForge.ModuleLoader.V1.dll")));
            Assert.False(Directory.Exists(Path.Combine(moduleRoot, "Loader")), "A source development bootstrapper must not add binaries to the source tree.");
            Assert.Contains("$PowerForgeDevelopmentLoadedType = 'DemoModule.Initialize' -as [type]", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentSelectedBinaryPath = [IO.Path]::GetFullPath($PowerForgeDevelopmentBinaryPath)", bootstrapper);
            Assert.Contains("[string]::Equals($PowerForgeDevelopmentLoadedAssemblyPath, $PowerForgeDevelopmentSelectedBinaryPath, [StringComparison]::OrdinalIgnoreCase)", bootstrapper);
            Assert.Contains("& $ImportModule -Assembly $PowerForgeDevelopmentLoadedType.Assembly -Force -PassThru -ErrorAction Stop", bootstrapper);
            Assert.Contains("& $ImportModule $PowerForgeDevelopmentBinaryPath -PassThru -ErrorAction Stop", bootstrapper);
            Assert.Contains("$PowerForgeCommandModuleDependencies = @", bootstrapper);
            Assert.Contains("'Required.Demo' = @('Get-Demo')", bootstrapper);
            Assert.DoesNotContain("No assemblies found", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithDevelopmentBinariesAndHandleRuntimes_ProbesSelectedBinaryRuntimePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-dev-runtime-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        Directory.CreateDirectory(moduleRoot);

        try
        {
            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-Demo" }, Array.Empty<string>());
            var developmentOptions = new ModuleDevelopmentBinaryBootstrapperOptions(
                ModuleDevelopmentBinaryMode.Auto,
                Path.Combine(root, "Sources", "Demo", "bin"),
                "DEMO_USE_DEVELOPMENT_BINARIES",
                "DEMO_DEVELOPMENT_CONFIGURATION",
                new[] { "net9.0" },
                new[] { "net472" });

            ModuleBootstrapperGenerator.Generate(
                moduleRoot,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: true,
                developmentBinaries: developmentOptions);

            var bootstrapper = File.ReadAllText(Path.Combine(moduleRoot, "DemoModule.psm1"));
            Assert.Contains("$PowerForgeDevelopmentArch = [string][System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentArch = [string]$env:PROCESSOR_ARCHITECTURE", bootstrapper);
            Assert.Contains("'AMD64' { 'win-x64' }", bootstrapper);
            Assert.Contains("if ([IntPtr]::Size -eq 4) { 'win-x86' } else { 'win-x64' }", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentLibFolder = [IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)", bootstrapper);
            Assert.Contains("Join-Path -Path $PowerForgeDevelopmentLibFolder -ChildPath (\"runtimes\\{0}\\native\" -f $PowerForgeDevelopmentArchFolder)", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentPathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH))", bootstrapper);
            Assert.Contains("[array] $PowerForgeDevelopmentRemainingPathEntries = foreach", bootstrapper);
            Assert.Contains("if ($PowerForgeDevelopmentPathEntry -ne $PowerForgeDevelopmentNativePath)", bootstrapper);
            Assert.Contains("[array] $PowerForgeDevelopmentOrderedPathEntries = @($PowerForgeDevelopmentNativePath) +", bootstrapper);
            Assert.Contains("$env:PATH = [string]::Join([IO.Path]::PathSeparator, $PowerForgeDevelopmentOrderedPathEntries)", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void AssertLoaderTargetFramework(string loaderPath, string expectedFramework)
    {
        Assert.True(File.Exists(loaderPath), loaderPath);
        using var stream = File.OpenRead(loaderPath);
        using var reader = new PEReader(stream);
        var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(reader);
        var framework = metadata.GetAssemblyDefinition().GetCustomAttributes()
            .Select(handle => metadata.GetCustomAttribute(handle))
            .Select(attribute => System.Text.Encoding.UTF8.GetString(metadata.GetBlobBytes(attribute.Value)))
            .FirstOrDefault(value => value.Contains(expectedFramework, StringComparison.Ordinal));
        Assert.NotNull(framework);
        // No debug directory: the shipped loader carries no build-machine paths.
        Assert.DoesNotContain(reader.ReadDebugDirectory(), entry => entry.Type == DebugDirectoryEntryType.CodeView);
    }

    private static Type LoadCompiledLoaderContextType(string moduleRoot)
        => System.Reflection.Assembly.LoadFile(Path.Combine(moduleRoot, "Loader", "Core", "PowerForge.ModuleLoader.V1.dll"))
            .GetType("PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext", throwOnError: true)!;

    private static string BuildFixtureProject(
        string root,
        string projectName,
        string assemblyName,
        string source,
        IReadOnlyList<string>? references = null)
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(root, projectName));
        var projectPath = Path.Combine(projectRoot.FullName, projectName + ".csproj");
        var sourcePath = Path.Combine(projectRoot.FullName, "Class1.cs");

        var referenceItems = references is { Count: > 0 }
            ? string.Join(
                Environment.NewLine,
                references.Select((reference, index) => $"""
                  <Reference Include="Reference{index}">
                    <HintPath>{reference}</HintPath>
                  </Reference>
                """))
            : string.Empty;

        File.WriteAllText(projectPath, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{{assemblyName}}</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
{{referenceItems}}
  </ItemGroup>
</Project>
""");

        File.WriteAllText(sourcePath, source);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectRoot.FullName
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"dotnet build failed for test fixture.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        var assemblyPath = Path.Combine(projectRoot.FullName, "bin", "Release", "net8.0", assemblyName + ".dll");
        Assert.True(File.Exists(assemblyPath), $"Built assembly not found: {assemblyPath}");
        return assemblyPath;
    }

    private static string GetCurrentRuntimeAssetRid()
    {
        if (OperatingSystem.IsWindows())
            return "win";
        if (OperatingSystem.IsMacOS())
            return "osx";

        return "linux";
    }

    private static void WriteDepsJson(string path)
    {
        File.WriteAllText(path, """
{
  "runtimeTarget": {
    "name": ".NETCoreApp,Version=v8.0",
    "signature": ""
  },
  "targets": {
    ".NETCoreApp,Version=v8.0": {
      "DemoModule/1.0.0": {
        "runtime": {
          "DemoModule.dll": {}
        }
      },
      "NestedDependency/1.0.0": {
        "runtime": {
          "lib/net8.0/NestedDependency.dll": {}
        }
      }
    }
  },
  "libraries": {
    "DemoModule/1.0.0": {
      "type": "project",
      "serviceable": false,
      "sha512": ""
    },
    "NestedDependency/1.0.0": {
      "type": "project",
      "serviceable": false,
      "sha512": ""
    }
  }
}
""");
    }
}
