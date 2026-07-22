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
            Assert.Contains("$L -split '[\\\\/]'", libraries);
            Assert.Contains("[System.Reflection.AssemblyName]::GetAssemblyName($LibraryPath)", libraries);

            var bootstrapper = File.ReadAllText(bootstrapperPath);
            Assert.Contains("# DemoModule bootstrapper", bootstrapper);
            Assert.Contains("$LibrariesScript = [IO.Path]::Combine($PSScriptRoot, 'DemoModule.Libraries.ps1')", bootstrapper);
            Assert.Contains("$FunctionsToExport = @('Get-Demo')", bootstrapper);
            Assert.Contains("$AliasesToExport = @('gdemo')", bootstrapper);
            Assert.Contains("[AppDomain]::CurrentDomain.add_AssemblyResolve($PowerForgeDesktopAssemblyResolver)", bootstrapper);
            Assert.Contains("$EventArgs.RequestingAssembly.Location", bootstrapper);
            Assert.Contains("$PowerForgeDesktopAssemblyResolverState = [pscustomobject]@{", bootstrapper);
            Assert.Contains("if (-not $PowerForgeDesktopAssemblyResolverState.BootstrapActive)", bootstrapper);
            Assert.Contains("$PowerForgeDesktopAssemblyResolverState.BootstrapActive = $false", bootstrapper);
            Assert.Contains("StartsWith($PowerForgeDesktopAssemblyRootPrefix, [StringComparison]::OrdinalIgnoreCase)", bootstrapper);
            Assert.Contains("$PowerForgeRequestedAssemblyName -ne [IO.Path]::GetFileName($PowerForgeRequestedAssemblyName)", bootstrapper);
            Assert.Contains("$PowerForgeRequestedAssemblyName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0", bootstrapper);
            Assert.Contains("$PowerForgeAssemblyCandidate = [IO.Path]::GetFullPath(", bootstrapper);
            Assert.Contains("$PowerForgeAssemblyCandidate.StartsWith($PowerForgeDesktopAssemblyRootPrefix, [StringComparison]::OrdinalIgnoreCase)", bootstrapper);
            Assert.Contains("[AppDomain]::CurrentDomain.remove_AssemblyResolve($PowerForgeResolverForRemoval)", bootstrapper);
            Assert.Contains("$ExecutionContext.SessionState.Module.OnRemove", bootstrapper);
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
        File.WriteAllText(Path.Combine(root, "Lib", "Default", "DemoModule.dll"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Lib", "Default", "Dependency.dll"), string.Empty);
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
            Assert.Contains("[AppDomain]::CurrentDomain.add_AssemblyResolve($PowerForgeDesktopAssemblyResolver)", bootstrapper);
            Assert.Contains("[AppDomain]::CurrentDomain.remove_AssemblyResolve($PowerForgeResolverForRemoval)", bootstrapper);
            Assert.Contains("$LibrariesScript = [IO.Path]::Combine($PSScriptRoot, 'DemoModule.Libraries.ps1')", bootstrapper);
            Assert.True(
                bootstrapper.IndexOf(". $LibrariesScript", StringComparison.Ordinal) <
                bootstrapper.IndexOf("& $ImportModule $ModuleAssemblyPath", StringComparison.Ordinal),
                "Desktop dependencies must load before the exported binary module.");

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
    public void BuildAssemblyLoadContextSource_ProbesPackagedRuntimeNativeAssets()
    {
        var identity = new ModuleBootstrapperGenerator.AssemblyLoadContextLoaderIdentity(
            "DemoModule.ModuleLoadContext",
            "DemoModule.ModuleLoadContext",
            "DemoModule.ModuleLoadContext.ModuleAssemblyLoadContext");
        var source = ModuleBootstrapperGenerator.BuildAssemblyLoadContextSource(identity);

        Assert.Contains("LoadPackagedNativeLibrary", source);
        Assert.Contains("TryLoadPackagedNativeLibrary", source);
        Assert.Contains("Path.Combine(_assemblyDirectory, \"runtimes\", rid, \"native\", fileName)", source);
        Assert.Contains("RuntimeInformation.ProcessArchitecture", source);
        Assert.Contains("RuntimeInformation.RuntimeIdentifier", source);
        Assert.Contains("LoadUnmanagedDllFromPath(path)", source);
        Assert.Contains("BadImageFormatException || ex is DllNotFoundException || ex is FileLoadException", source);
        Assert.Contains("yield return \"win-\" + arch", source);
        Assert.Contains("yield return \"linux-\" + arch", source);
        Assert.Contains("yield return \"linux-musl-\" + arch", source);
        Assert.Contains("yield return \"linux-musl\"", source);
        Assert.Contains("yield return \"osx\"", source);
        Assert.Contains("yield return \"unix\"", source);
        Assert.Contains("yield return unmanagedDllName + \".so\";", source);
        Assert.Contains("yield return \"lib\" + unmanagedDllName + \".so\";", source);
    }

    [Fact]
    public void BuildAssemblyLoadContextSource_FallsBackToDirectoryProbingWhenResolverIsUnavailable()
    {
        var identity = new ModuleBootstrapperGenerator.AssemblyLoadContextLoaderIdentity(
            "DemoModule.ModuleLoadContext",
            "DemoModule.ModuleLoadContext",
            "DemoModule.ModuleLoadContext.ModuleAssemblyLoadContext");
        var source = ModuleBootstrapperGenerator.BuildAssemblyLoadContextSource(identity);

        Assert.Contains("private readonly AssemblyDependencyResolver? _resolver;", source);
        Assert.Contains("private readonly DependencyManifestResolver? _manifestResolver;", source);
        Assert.Contains("_resolver = TryCreateResolver(_moduleAssemblyPath);", source);
        Assert.Contains("_manifestResolver = DependencyManifestResolver.TryCreate(_moduleAssemblyPath);", source);
        Assert.Contains("catch (InvalidOperationException)", source);
        Assert.Contains("return null;", source);
        Assert.Contains("_resolver?.ResolveAssemblyToPath(assemblyName)", source);
        Assert.Contains("_resolver?.ResolveUnmanagedDllToPath(unmanagedDllName)", source);
        Assert.Contains("_manifestResolver?.ResolveAssemblyToPath(assemblyName)", source);
        Assert.Contains("_manifestResolver?.ResolveUnmanagedDllToPath(unmanagedDllName)", source);
        Assert.Contains("ResolvePackagedRuntimeAssembly(assemblyName.Name)", source);
        Assert.Contains("Path.Combine(_assemblyDirectory, \"runtimes\", rid, \"lib\")", source);
        Assert.Contains("Path.ChangeExtension(assemblyPath, \".deps.json\")", source);
        Assert.Contains("Path.Combine(_assemblyDirectory, assemblyName.Name + \".dll\")", source);
        Assert.Contains("LoadPackagedNativeLibrary(unmanagedDllName)", source);
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

            var loaderAssembly = System.Reflection.Assembly.LoadFile(Path.Combine(libCore, "DemoModule.ModuleLoadContext.dll"));
            var contextType = loaderAssembly.GetType("DemoModule.ModuleLoadContext.ModuleAssemblyLoadContext", throwOnError: true)!;
            var resolverType = loaderAssembly.GetType("DemoModule.ModuleLoadContext.ModuleAssemblyLoadContext+DependencyManifestResolver", throwOnError: true)!;
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

            var loaderAssembly = System.Reflection.Assembly.LoadFile(Path.Combine(libCore, "DemoModule.ModuleLoadContext.dll"));
            var contextType = loaderAssembly.GetType("DemoModule.ModuleLoadContext.ModuleAssemblyLoadContext", throwOnError: true)!;
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
            Assert.Contains("$ExistingType = $Existing[$Name]", bootstrapper);
            Assert.Contains("$ExistingLoadContext = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($ExistingType.Assembly)", bootstrapper);
            Assert.Contains("$TypeLoadContext = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($Type.Assembly)", bootstrapper);
            Assert.Contains("[object]::ReferenceEquals($ExistingLoadContext, $TypeLoadContext) -and [object]::Equals($ExistingAssemblyName.FullName, $TypeAssemblyName.FullName)", bootstrapper);
            Assert.Contains("Write-Verbose -Message \"Type accelerator '$Name' already exists in the same AssemblyLoadContext from the same assembly identity.", bootstrapper);
            Assert.Contains("Write-Warning -Message \"Type accelerator '$Name' already exists from $($ExistingAssemblyName.FullName).", bootstrapper);
            Assert.Contains("if ([object]::ReferenceEquals($ExistingType, $Type)) {", bootstrapper);
            Assert.Contains("return", bootstrapper);
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
    public void Generate_WithAssemblyLoadContextEnumTypeAccelerators_WritesEnumModeFilter()
    {
        var block = ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(
            AssemblyTypeAcceleratorExportMode.Enums,
            new[] { "Dependency.Widget" },
            new[] { "Dependency" });

        Assert.Contains("$Mode = 'Enums'", block);
        Assert.Contains("$RequestedTypes = @('Dependency.Widget')", block);
        Assert.Contains("$RequestedAssemblies = @('Dependency')", block);
        Assert.Contains("if ($Mode -eq 'Enums' -and -not $Type.IsEnum)", block);
        Assert.Contains("foreach ($TypeName in $RequestedTypes)", block);
    }

    [Fact]
    public void Generate_WithAssemblyLoadContextAssemblyOnlyTypeAccelerators_WritesEmptyAllowList()
    {
        var block = ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(
            AssemblyTypeAcceleratorExportMode.Assembly,
            Array.Empty<string>(),
            new[] { "Dependency" });

        Assert.Contains("$Mode = 'Assembly'", block);
        Assert.Contains("$RequestedTypes = @()", block);
        Assert.Contains("$RequestedAssemblies = @('Dependency')", block);
        Assert.Contains("$PowerForgeAlcLibraryDirectory = [IO.Path]::GetFullPath($LibFolder)", block);
        Assert.Contains("$PowerForgeAlcLibraryDirectory = [IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder)", block);
        Assert.Contains("$ExportedTypes = @($Assembly.GetExportedTypes())", block);
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
            Assert.DoesNotContain("$RegisterPowerForgeAssemblyTypeAccelerators", bootstrapper);
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
            Assert.Contains("Add-Type -TypeDefinition @'", bootstrapper);
            Assert.Contains("DemoModule.DevelopmentModuleLoadContext.ModuleAssemblyLoadContext", bootstrapper);
            Assert.DoesNotContain("DemoModule.ModuleLoadContext.ModuleAssemblyLoadContext", bootstrapper);
            Assert.Contains("DemoModule.Development", bootstrapper);
            Assert.Contains("$ModuleAssembly = $PowerForgeDevelopmentModuleAssembly", bootstrapper);
            Assert.Contains("$LibFolder = [IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)", bootstrapper);
            Assert.Contains("$RequestedTypes = @('Demo.Dependency')", bootstrapper);
            Assert.Contains("& $RegisterPowerForgeAssemblyTypeAccelerators -ModuleAssembly $ModuleAssembly -LibFolder $LibFolder", bootstrapper);
            Assert.Contains("private readonly AssemblyDependencyResolver _resolver;", bootstrapper);
            Assert.DoesNotContain("AssemblyDependencyResolver?", bootstrapper);
            Assert.Contains("_resolver = TryCreateResolver(_moduleAssemblyPath);", bootstrapper);
            Assert.Contains("catch (InvalidOperationException)", bootstrapper);
            Assert.Contains("_resolver?.ResolveAssemblyToPath(assemblyName)", bootstrapper);
            Assert.Contains("_resolver?.ResolveUnmanagedDllToPath(unmanagedDllName)", bootstrapper);
            Assert.Contains("Falling back to direct Import-Module; cmdlets from DemoModule will load from the default context.", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentLoadedType = 'DemoModule.Initialize' -as [type]", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentSelectedBinaryPath = [IO.Path]::GetFullPath($PowerForgeDevelopmentBinaryPath)", bootstrapper);
            Assert.Contains("[string]::Equals($PowerForgeDevelopmentLoadedAssemblyPath, $PowerForgeDevelopmentSelectedBinaryPath, [StringComparison]::OrdinalIgnoreCase)", bootstrapper);
            Assert.Contains("& $ImportModule -Assembly $PowerForgeDevelopmentLoadedType.Assembly -Force -ErrorAction Stop", bootstrapper);
            Assert.Contains("& $ImportModule $PowerForgeDevelopmentBinaryPath -ErrorAction Stop", bootstrapper);
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
            Assert.Contains("$PowerForgeDevelopmentLibFolder = [IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)", bootstrapper);
            Assert.Contains("Join-Path -Path $PowerForgeDevelopmentLibFolder -ChildPath (\"runtimes\\{0}\\native\" -f $PowerForgeDevelopmentArchFolder)", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentPathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH))", bootstrapper);
            Assert.Contains("$env:PATH = \"$PowerForgeDevelopmentNativePath$([IO.Path]::PathSeparator)$env:PATH\"", bootstrapper);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

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
