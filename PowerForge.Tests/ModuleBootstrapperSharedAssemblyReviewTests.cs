using PowerForge;

public sealed partial class ModuleBootstrapperSharedAssemblyTests
{
    [Fact]
    public void SharedPolicy_HelperIdentities_AreStableForNameSetsAndDistinctFromLegacy()
    {
        var legacy = ModuleBootstrapperGenerator.GetAssemblyLoadContextLoaderFileNames("Retained");
        Assert.Equal(new[] { "Retained.ModuleLoadContext.dll", "Retained.DevelopmentModuleLoadContext.dll" }, legacy);
        var current = ModuleBootstrapperGenerator.GetAssemblyLoadContextLoaderFileNames("Retained", new[] { "SharedCore", "SharedDisplay" });
        Assert.Equal(current, ModuleBootstrapperGenerator.GetAssemblyLoadContextLoaderFileNames("Retained", new[] { "shareddisplay", "sharedcore" }));
        var coreOnly = ModuleBootstrapperGenerator.GetAssemblyLoadContextLoaderFileNames("Retained", new[] { "SharedCore" });
        for (var i = 0; i < current.Length; i++)
        {
            Assert.NotEqual(legacy[i], current[i]);
            Assert.NotEqual(coreOnly[i], current[i]);
            Assert.True(ModuleBootstrapperGenerator.IsAssemblyLoadContextLoaderFileName("Retained", current[i]));
            Assert.False(ModuleBootstrapperGenerator.IsAssemblyLoadContextLoaderFileName("OtherProduct", current[i]));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SharedPolicy_RegeneratedPayload_DoesNotPreloadRetainedCoreHelpersOnDesktop()
    {
        if (!OperatingSystem.IsWindows()) return;
        WithReviewFixtures((root, fixtures, probe) =>
        {
            var modules = Path.Combine(root, "regenerated");
            GenerateReviewModule(modules, fixtures, "v2", Array.Empty<string>(), Array.Empty<string>());
            GenerateReviewModule(modules, fixtures, "v2", new[] { "SharedCore", "SharedDisplay" }, Array.Empty<string>());
            var output = Run("powershell.exe", root, "-File", probe, "-Root", modules, "-Preload", "none");
            Assert.Contains("REVIEW_PROOF_OK:", output, StringComparison.Ordinal);
            _output.WriteLine("retained payload helper: " + output.Trim());
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SharedPolicy_RetainedOldGeneratedHelpers_CannotBypassNewPolicy()
    {
        if (!OperatingSystem.IsWindows()) return;
        WithReviewFixtures((root, fixtures, probe) =>
        {
            foreach (var development in new[] { false, true })
            foreach (var oldShared in new[] { Array.Empty<string>(), new[] { "SharedCore" } })
            {
                var modules = Path.Combine(root, (development ? "upgrade-dev" : "upgrade-packaged") + oldShared.Length);
                GenerateReviewModule(modules, fixtures, "v1", oldShared, oldShared);
                GenerateReviewModule(modules, fixtures, "v2", new[] { "SharedCore", "SharedDisplay" }, new[] { "SharedCore" });
                var oldFile = ModuleBootstrapperGenerator.GetAssemblyLoadContextLoaderFileNames("Retained", oldShared)[development ? 1 : 0];
                var arguments = new List<string> { "-File", probe, "-Root", modules, "-Preload", "core", "-Upgrade",
                    "-OldLoaderType", Path.GetFileNameWithoutExtension(oldFile) + ".ModuleAssemblyLoadContext" };
                if (development) arguments.Add("-Development");
                var output = Run("pwsh.exe", root, arguments.ToArray());
                Assert.Contains("REVIEW_PROOF_OK:", output, StringComparison.Ordinal);
                _output.WriteLine(output.Trim());
            }
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SharedPolicy_LazyDependencies_ResolveAfterModuleRemovalWithoutPreloadingAll()
    {
        if (!OperatingSystem.IsWindows()) return;
        WithReviewFixtures((root, fixtures, probe) =>
        {
            foreach (var preload in new[] { "none", "core", "display" })
            {
                var modules = Path.Combine(root, "lazy-" + preload);
                var names = preload == "none" ? Array.Empty<string>() : new[] { preload == "core" ? "SharedCore" : "SharedDisplay" };
                GenerateReviewModule(modules, fixtures, "v2", new[] { "SharedCore", "SharedDisplay" }, names);
                foreach (var development in new[] { false, true })
                    foreach (var host in new[] { "pwsh.exe", "powershell.exe" })
                    {
                        var arguments = new List<string> { "-File", probe, "-Root", modules, "-Preload", preload };
                        if (development) arguments.Add("-Development");
                        var output = Run(host, root, arguments.ToArray());
                        Assert.Contains("REVIEW_PROOF_OK:", output, StringComparison.Ordinal);
                        _output.WriteLine(output.Trim());
                    }
            }
        });
    }

    private static void GenerateReviewModule(string modules, string fixtures, string version, string[] shared, string[] preload)
    {
        var moduleRoot = Path.Combine(modules, version);
        foreach (var payload in new[] { "Core", "Default" })
        {
            var directory = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", payload)).FullName;
            File.Copy(Path.Combine(fixtures, version == "v1" ? "old" : "new", "Retained.Host.dll"), Path.Combine(directory, "Retained.Host.dll"), true);
            if (version != "v1" || shared.Length > 0) CopyRuntime(fixtures, directory, 1);
        }
        var developmentRoot = Path.Combine(moduleRoot, "bin");
        foreach (var framework in new[] { "net8.0", "net472" })
        {
            var directory = Directory.CreateDirectory(Path.Combine(developmentRoot, "Release", framework)).FullName;
            File.Copy(Path.Combine(fixtures, version == "v1" ? "old" : "new", "Retained.Host.dll"), Path.Combine(directory, "Retained.Host.dll"), true);
            if (version != "v1" || shared.Length > 0) CopyRuntime(fixtures, directory, 1);
        }
        ModuleBootstrapperGenerator.Generate(moduleRoot, "Retained",
            new ExportSet(Array.Empty<string>(), new[] { "Get-RetainedData" }, Array.Empty<string>()),
            new[] { "Retained.Host.dll" }, handleRuntimes: false, useAssemblyLoadContext: true,
            targetFrameworks: new[] { "net8.0", "net472" },
            developmentBinaries: new ModuleDevelopmentBinaryBootstrapperOptions(ModuleDevelopmentBinaryMode.Environment,
                developmentRoot, "PF_REVIEW_DEVELOPMENT", "PF_REVIEW_CONFIGURATION", new[] { "net8.0" }, new[] { "net472" }),
            sharedAssemblies: shared, preloadSharedAssemblies: preload);
    }

    private static void WithReviewFixtures(Action<string, string, string> proof)
    {
        var root = NewScratchRoot();
        var scripts = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SharedModuleAssemblies");
        var fixtures = Path.Combine(root, "fixtures");
        try
        {
            Run("powershell.exe", root, "-File", Path.Combine(scripts, "New-ReviewFixtures.ps1"), "-Root", fixtures);
            proof(root, fixtures, Path.Combine(scripts, "Test-ReviewFindings.ps1"));
        }
        finally { Directory.Delete(root, true); }
    }
}
