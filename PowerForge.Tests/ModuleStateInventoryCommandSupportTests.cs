using PSPublishModule;
using System.Reflection;
using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ModuleStateInventoryCommandSupportTests
{
    [Fact]
    public void InferScope_RecognizesUnixAllUsersRoots()
    {
        var scope = InvokeInferScope("/usr/local/share/powershell/Modules/Company.Tools");

        Assert.Equal("AllUsers", scope);
    }

    [Fact]
    public void InferScope_DoesNotTreatArbitraryProfilePathsAsCurrentUser()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
            return;

        var scope = InvokeInferScopeNullable(Path.Combine(profile, "src", "Modules", "Company.Tools"));

        Assert.Null(scope);
    }

    [Fact]
    public void InferScope_DoesNotTreatArbitraryProgramFilesPathsAsAllUsers()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
            return;

        var scope = InvokeInferScopeNullable(Path.Combine(programFiles, "Company", "StagingModules", "Company.Tools"));

        Assert.Null(scope);
    }

    [Fact]
    public void CreateInventoryResultFromFile_MarksMatchingLoadedModule()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var inventoryPath = Path.Combine(root.FullName, "inventory.json");
            var modulePath = Path.Combine(root.FullName, "Company.Tools", "1.2.0");
            Directory.CreateDirectory(modulePath);
            File.WriteAllText(inventoryPath, $$"""
{
  "installedModules": [
    {
      "name": "Company.Tools",
      "version": "1.2.0",
      "path": "{{modulePath.Replace("\\", "\\\\")}}"
    }
  ]
}
""");

            var result = ModuleStateInventoryCommandSupport.CreateInventoryResultFromFile(
                inventoryPath,
                new[] { new ModuleStateLoadedModuleEvidence("Company.Tools", "1.2", Path.Combine(modulePath, "Company.Tools.psm1")) });

            var module = Assert.Single(result.InstalledModules);
            Assert.True(module.IsLoaded);
            Assert.True(module.IsEffectiveImportCandidate);
            Assert.Equal(modulePath, module.Path);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void NormalizeModulePaths_UsesFilesystemAwarePathComparer()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var upper = Path.Combine(root, "Modules");
        var lower = Path.Combine(root, "modules");

        var paths = ModuleStateInventoryCommandSupport.NormalizeModulePaths(new[] { upper, lower });

        var expectedCount = OperatingSystem.IsWindows() ? 1 : 2;
        Assert.Equal(expectedCount, paths.Length);
    }

    [Fact]
    public void CreateInventoryResultFromModulePaths_AppliesNameFilterAfterLoadedMerge()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var result = ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePaths(
                new[] { root.FullName },
                new[]
                {
                    new ModuleStateLoadedModuleEvidence("Microsoft.Graph.Users", "2.38.0", Path.Combine(root.FullName, "Microsoft.Graph.Users.psm1")),
                    new ModuleStateLoadedModuleEvidence("Company.Runtime", "1.0.0", Path.Combine(root.FullName, "Company.Runtime.psm1"))
                },
                new[] { "Microsoft.Graph.*" });

            var module = Assert.Single(result.InstalledModules);
            Assert.Equal("Microsoft.Graph.Users", module.Name);
            Assert.True(module.IsLoaded);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void CreateInventoryResultFromFile_AddsLoadedOnlyModule()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var inventoryPath = Path.Combine(root.FullName, "inventory.json");
            File.WriteAllText(inventoryPath, """{ "installedModules": [] }""");

            var result = ModuleStateInventoryCommandSupport.CreateInventoryResultFromFile(
                inventoryPath,
                new[] { new ModuleStateLoadedModuleEvidence("Company.Runtime", "2.0.0", @"C:\Temp\Company.Runtime.psm1") });

            var module = Assert.Single(result.InstalledModules);
            Assert.Equal("Company.Runtime", module.Name);
            Assert.Equal("2.0.0", module.Version);
            Assert.True(module.IsLoaded);
            Assert.False(module.IsEffectiveImportCandidate);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void IncludeLoadedModules_MergesLoadedEvidenceIntoPipedInventory()
    {
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Pipeline",
            ModulePaths = new[] { @"C:\Modules" },
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.2.0",
                    Path = @"C:\Modules\Company.Tools\1.2.0"
                }
            }
        };

        var result = ModuleStateInventoryCommandSupport.IncludeLoadedModules(
            inventory,
            new[] { new ModuleStateLoadedModuleEvidence("Company.Tools", "1.2", @"C:\Modules\Company.Tools\1.2.0\Company.Tools.psm1") });

        Assert.Equal("Pipeline", result.Source);
        Assert.Equal(new[] { @"C:\Modules" }, result.ModulePaths);
        var module = Assert.Single(result.InstalledModules);
        Assert.True(module.IsLoaded);
        Assert.True(module.IsEffectiveImportCandidate);
        Assert.Equal(@"C:\Modules\Company.Tools\1.2.0", module.Path);
    }

    [Fact]
    public void ToCoreInventory_TreatsLegacyModulePathsAsAvailableUnlessDiagnosticsProveOtherwise()
    {
        const string availableRoot = @"C:\AvailableModules";
        const string missingRoot = @"C:\MissingModules";
        var result = new ModuleStateInventoryResult
        {
            Source = "LegacyArtifact",
            ModulePaths = new[] { availableRoot, missingRoot },
            Diagnostics = new[]
            {
                new ModuleStateInventoryDiagnosticResult
                {
                    Severity = "Error",
                    Code = "ModuleState.InventoryPathMissing",
                    Message = "The path was unavailable during collection.",
                    Path = missingRoot
                }
            }
        };

        var inventory = ModuleStateInventoryResultMapper.ToCoreInventory(result);

        Assert.True(Assert.Single(inventory.ModulePaths, path => ModuleStatePathIdentity.Equals(path.Path, availableRoot)).WasAvailable);
        Assert.False(Assert.Single(inventory.ModulePaths, path => ModuleStatePathIdentity.Equals(path.Path, missingRoot)).WasAvailable);
    }

    [Fact]
    public void InventoryMapping_PreservesDependencyVisibilityGroup()
    {
        const string moduleRoot = @"C:\VisibleModules";
        var coreInventory = new ModuleStateInventory(
            Array.Empty<ModuleStateInstalledModule>(),
            new[]
            {
                new ModuleStateModulePath(
                    moduleRoot,
                    wasAvailable: true,
                    dependencyVisibilityGroup: ModuleStateInventoryCommandSupport.CurrentProcessModulePathVisibilityGroup)
            },
            Array.Empty<ModuleStateInventoryDiagnostic>());

        var result = ModuleStateInventoryResultMapper.ToCmdletResult(coreInventory, "ModulePath", new[] { moduleRoot });
        var roundTripped = ModuleStateInventoryResultMapper.ToCoreInventory(result);

        Assert.Equal(
            ModuleStateInventoryCommandSupport.CurrentProcessModulePathVisibilityGroup,
            Assert.Single(result.ScannedPaths).DependencyVisibilityGroup);
        Assert.Equal(
            ModuleStateInventoryCommandSupport.CurrentProcessModulePathVisibilityGroup,
            Assert.Single(roundTripped.ModulePaths).DependencyVisibilityGroup);
    }

    [Fact]
    public void ResolveLoadedModuleVersion_AppendsPrereleaseLabel()
    {
        var item = new PSObject();
        item.Properties.Add(new PSNoteProperty("Version", new Version(1, 2, 0)));
        item.Properties.Add(new PSNoteProperty("Prerelease", "preview1"));

        var version = ModuleStateInventoryCommandSupport.ResolveLoadedModuleVersion(item);

        Assert.Equal("1.2.0-preview1", version);
    }

    [Fact]
    public void MergeWithModulePathEntries_recomputes_effective_candidate_from_merged_root_order()
    {
        using var workspace = new TemporaryDirectory();
        var preferredRoot = Path.Combine(workspace.Path, "preferred");
        var supplementalRoot = Path.Combine(workspace.Path, "supplemental");
        var preferredPath = CreateInstalledModule(preferredRoot, "Company.Tools", "1.0.0");
        CreateInstalledModule(supplementalRoot, "Company.Tools", "2.0.0");
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Artifact",
            ModulePaths = new[] { preferredRoot },
            ScannedPaths = new[]
            {
                new ModuleStateInventoryPathResult
                {
                    Path = preferredRoot,
                    PowerShellEdition = "Core",
                    Scope = "CurrentUser",
                    IsRequired = true
                }
            },
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    Path = preferredPath,
                    ModuleRoot = preferredRoot,
                    PowerShellEdition = "Core",
                    Scope = "CurrentUser",
                    IsEffectiveImportCandidate = true
                }
            }
        };

        var merged = ModuleStateInventoryCommandSupport.MergeWithModulePathEntries(
            inventory,
            new[] { new ModuleStateModulePath(supplementalRoot, "Core", "CurrentUser") });

        var winner = Assert.Single(merged.InstalledModules, static module => module.IsEffectiveImportCandidate);
        Assert.Equal("1.0.0", winner.Version);
        Assert.Equal(preferredRoot, winner.ModuleRoot);
        Assert.Equal(2, merged.InstalledModules.Length);
    }

    [Fact]
    public void MergeWithModulePathEntries_ReplacesStaleArtifactEvidenceForAvailableRoot()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "modules");
        Directory.CreateDirectory(moduleRoot);
        var stalePath = Path.Combine(moduleRoot, "Company.Stale", "1.0.0");
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Artifact",
            ModulePaths = new[] { moduleRoot },
            ScannedPaths = new[] { new ModuleStateInventoryPathResult { Path = moduleRoot, IsRequired = true, WasAvailable = true } },
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Stale",
                    Version = "1.0.0",
                    Path = stalePath,
                    ModuleRoot = moduleRoot
                }
            },
            Diagnostics = new[]
            {
                new ModuleStateInventoryDiagnosticResult
                {
                    Severity = "Error",
                    Code = "ModuleState.StaleArtifact",
                    Message = "Stale artifact evidence.",
                    Path = stalePath
                }
            }
        };

        var merged = ModuleStateInventoryCommandSupport.MergeWithModulePathEntries(
            inventory,
            new[] { new ModuleStateModulePath(moduleRoot, isRequired: true) });

        Assert.Empty(merged.InstalledModules);
        Assert.DoesNotContain(merged.Diagnostics, static diagnostic => diagnostic.Code == "ModuleState.StaleArtifact");
        var scannedRoot = Assert.Single(merged.ScannedPaths);
        Assert.True(scannedRoot.WasAvailable);
        Assert.True(scannedRoot.IsRequired);
    }

    [Fact]
    public void MergeWithModulePathEntries_UsesCurrentUnavailableStateForSupplementalRoot()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "missing-modules");
        var stalePath = Path.Combine(moduleRoot, "Company.Stale", "1.0.0");
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Artifact",
            ModulePaths = new[] { moduleRoot },
            ScannedPaths = new[]
            {
                new ModuleStateInventoryPathResult
                {
                    Path = moduleRoot,
                    IsRequired = true,
                    WasAvailable = true
                }
            },
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Stale",
                    Version = "1.0.0",
                    Path = stalePath,
                    ModuleRoot = moduleRoot
                }
            }
        };

        var merged = ModuleStateInventoryCommandSupport.MergeWithModulePathEntries(
            inventory,
            new[] { new ModuleStateModulePath(moduleRoot, isRequired: true) });

        var scannedRoot = Assert.Single(merged.ScannedPaths);
        Assert.False(scannedRoot.WasAvailable);
        Assert.True(scannedRoot.IsRequired);
        Assert.Single(merged.InstalledModules);
        Assert.Contains(merged.Diagnostics, static diagnostic => diagnostic.Code == "ModuleState.InventoryPathMissing");
    }

    private static string CreateInstalledModule(string root, string name, string version)
    {
        var path = Path.Combine(root, name, version);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, name + ".psd1"), "@{ ModuleVersion = '" + version + "' }");
        return path;
    }

    private static string? InvokeInferScope(string path)
    {
        var result = InvokeInferScopeNullable(path);
        return Assert.IsType<string>(result);
    }

    private static string? InvokeInferScopeNullable(string path)
    {
        var method = typeof(ModuleStateInventoryCommandSupport).GetMethod(
            "InferScope",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return method!.Invoke(null, new object[] { path }) as string;
    }
}
