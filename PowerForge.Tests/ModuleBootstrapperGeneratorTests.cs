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
    public void ResolveAssemblyLoadContextTargetFramework_UsesLowestModernModuleFramework()
    {
        var framework = ModuleBootstrapperGenerator.ResolveAssemblyLoadContextTargetFramework(new[] { "net472", "net8.0", "net6.0-windows" });

        Assert.Equal("net6.0", framework);
    }

    [Fact]
    public void ResolveAssemblyLoadContextTargetFramework_DefaultsToNet8WhenNoModernFrameworkIsKnown()
    {
        var framework = ModuleBootstrapperGenerator.ResolveAssemblyLoadContextTargetFramework(new[] { "net472", "netstandard2.0" });

        Assert.Equal("net8.0", framework);
    }

    [Fact]
    public void ResolveAssemblyLoadContextTargetDirectories_PrefersStandardWhenAllLibLayoutsExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-layout-" + Guid.NewGuid().ToString("N"));
        var libRoot = Path.Combine(root, "Lib");
        Directory.CreateDirectory(Path.Combine(libRoot, "Standard"));
        Directory.CreateDirectory(Path.Combine(libRoot, "Core"));
        Directory.CreateDirectory(Path.Combine(libRoot, "Default"));

        try
        {
            var directories = ModuleBootstrapperGenerator.ResolveAssemblyLoadContextTargetDirectories(libRoot);

            Assert.Equal(new[] { Path.Combine(libRoot, "Standard") }, directories);
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
            Assert.Contains("-PassThru -ErrorAction Stop", bootstrapper);
            Assert.Contains("AddExportedCmdlet", bootstrapper);
            Assert.Contains("AddExportedAlias", bootstrapper);
            Assert.Contains("ExportedAliases.Values", bootstrapper);
            Assert.Contains("Aliases from $LibraryName will not be re-exported to the module scope.", bootstrapper);
            Assert.Contains("before the private export table can reference it", bootstrapper);
            Assert.Contains("if ([string]::IsNullOrWhiteSpace($Alias.Definition)) { $Alias.ResolvedCommandName } else { $Alias.Definition }", bootstrapper);
            Assert.Contains("Set-Alias -Name $Alias.Name -Value $AliasTarget -Scope Local -Force -ErrorAction Stop", bootstrapper);
            Assert.Contains("GetCommand($Alias.Name, [System.Management.Automation.CommandTypes]::Alias)", bootstrapper);
            Assert.Contains("could not be re-exported", bootstrapper);
            Assert.Contains("Falling back to direct Import-Module", bootstrapper);
            Assert.Contains("will load from the default context", bootstrapper);
            Assert.Contains("$PSEdition -ne 'Core'", bootstrapper);
            Assert.Contains("$LibrariesScript = [IO.Path]::Combine($PSScriptRoot, 'DemoModule.Libraries.ps1')", bootstrapper);

            Assert.True(File.Exists(Path.Combine(root, "Lib", "Core", "DemoModule.ModuleLoadContext.dll")));
            Assert.False(File.Exists(Path.Combine(root, "Lib", "Default", "DemoModule.ModuleLoadContext.dll")));
            Assert.True(File.Exists(Path.Combine(root, "DemoModule.Libraries.ps1")));

            var libraries = File.ReadAllText(Path.Combine(root, "DemoModule.Libraries.ps1"));
            Assert.DoesNotContain("DemoModule.ModuleLoadContext.dll", libraries);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
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
            Assert.Contains("$Framework = 'Default'", bootstrapper);
            Assert.True(File.Exists(Path.Combine(root, "Lib", "Default", "DemoModule.ModuleLoadContext.dll")));

            var libraries = File.ReadAllText(Path.Combine(root, "DemoModule.Libraries.ps1"));
            Assert.DoesNotContain("DemoModule.ModuleLoadContext.dll", libraries);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Generate_WithAssemblyLoadContextTypeAccelerators_WritesAllowListedRegistrationBlock()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-types-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        File.WriteAllText(Path.Combine(root, "Lib", "Core", "DemoModule.dll"), string.Empty);
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

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            Assert.Contains("$RegisterPowerForgeAssemblyTypeAccelerators = {", bootstrapper);
            Assert.Contains("$Mode = 'AllowList'", bootstrapper);
            Assert.Contains("$RequestedTypes = @('Dependency.Widget')", bootstrapper);
            Assert.Contains("System.Management.Automation.TypeAccelerators", bootstrapper);
            Assert.Contains("$TypeAccelerators.GetProperty('Get', [System.Reflection.BindingFlags] 'Static,Public,NonPublic')", bootstrapper);
            Assert.Contains("AssemblyLoadContext]::GetLoadContext($ModuleAssembly)", bootstrapper);
            Assert.Contains("$AddPowerForgeTypeAccelerator = {", bootstrapper);
            Assert.Contains("Type accelerator '$Name' already exists", bootstrapper);
            Assert.Contains("if ([object]::ReferenceEquals($Existing[$Name], $Type)) {\r\n                return", bootstrapper);
            Assert.Contains("$PreviousPowerForgeOnRemove = $ExecutionContext.SessionState.Module.OnRemove", bootstrapper);
            Assert.Contains("& $PreviousPowerForgeOnRemove @args", bootstrapper);
            Assert.Contains("OnRemove", bootstrapper);
            Assert.Contains("& $RegisterPowerForgeAssemblyTypeAccelerators -ModuleAssembly $ModuleAssembly -LibFolder $LibFolder", bootstrapper);
            Assert.DoesNotContain("function Register-PowerForgeAssemblyTypeAccelerators", bootstrapper);
            Assert.DoesNotContain("$Mode -eq 'None'", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WithAssemblyLoadContextAssemblyTypeAccelerators_WritesAssemblyModeWithEnumerationGuard()
    {
        var block = ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(
            AssemblyTypeAcceleratorExportMode.Assembly,
            new[] { "Dependency.Widget" },
            new[] { "Dependency" });

        Assert.Contains("$Mode = 'Assembly'", block);
        Assert.Contains("$RequestedTypes = @('Dependency.Widget')", block);
        Assert.Contains("$RequestedAssemblies = @('Dependency')", block);
        Assert.Contains("$ExportedTypes = @($Assembly.GetExportedTypes())", block);
        Assert.Contains("Could not enumerate exported types from assembly '$AssemblyName'", block);
        Assert.Contains("& $AddPowerForgeTypeAccelerator -Type $Type", block);
        Assert.Contains("foreach ($TypeName in $RequestedTypes)", block);
    }

    [Fact]
    public void Generate_WithTypeAcceleratorModeNone_DoesNotInferFromConfiguredLists()
    {
        var block = ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(
            AssemblyTypeAcceleratorExportMode.None,
            new[] { "Dependency.Widget" },
            Array.Empty<string>());

        Assert.Equal(string.Empty, block);
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
