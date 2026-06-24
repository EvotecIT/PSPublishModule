namespace PowerForge.Tests;

public sealed class ModuleStateInventoryServiceTests
{
    [Fact]
    public void Collect_DiscoversVersionedAndFlatModuleManifests()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteManifest(Path.Combine(root.FullName, "Company.Tools", "1.2.3"), "Company.Tools", "1.2.3");
            WriteManifest(Path.Combine(root.FullName, "Company.Legacy"), "Company.Legacy", "0.9.0");

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(root.FullName, "Core", "CurrentUser")
            }));

            Assert.Equal(2, inventory.InstalledModules.Length);
            Assert.Contains(inventory.InstalledModules, module =>
                module.Name == "Company.Tools" &&
                module.Version == "1.2.3" &&
                module.PowerShellEdition == "Core" &&
                module.Scope == "CurrentUser" &&
                module.IsEffectiveImportCandidate &&
                module.Path!.EndsWith(Path.Combine("Company.Tools", "1.2.3"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(inventory.InstalledModules, module =>
                module.Name == "Company.Legacy" &&
                module.Version == "0.9.0" &&
                module.IsEffectiveImportCandidate &&
                module.Path!.EndsWith("Company.Legacy", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Collect_MarksImportWinnerByModulePathOrderThenHighestVersionInThatRoot()
    {
        var firstRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var secondRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteManifest(Path.Combine(firstRoot.FullName, "Company.Tools", "1.0.0"), "Company.Tools", "1.0.0");
            WriteManifest(Path.Combine(firstRoot.FullName, "Company.Tools", "1.1.0"), "Company.Tools", "1.1.0");
            WriteManifest(Path.Combine(secondRoot.FullName, "Company.Tools", "2.0.0"), "Company.Tools", "2.0.0");

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(firstRoot.FullName, "Core", "CurrentUser"),
                new ModuleStateModulePath(secondRoot.FullName, "Core", "AllUsers")
            }));

            var winner = Assert.Single(inventory.InstalledModules, static module => module.IsEffectiveImportCandidate);

            Assert.Equal("Company.Tools", winner.Name);
            Assert.Equal("1.1.0", winner.Version);
            Assert.Equal("CurrentUser", winner.Scope);
            Assert.Contains(inventory.InstalledModules, static module =>
                module.Version == "2.0.0" &&
                module.Scope == "AllUsers" &&
                !module.IsEffectiveImportCandidate);
        }
        finally
        {
            try { firstRoot.Delete(recursive: true); } catch { /* best effort */ }
            try { secondRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Collect_SkipsMissingModuleRoots()
    {
        var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
        {
            new ModuleStateModulePath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
        }));

        Assert.Empty(inventory.InstalledModules);
    }

    private static void WriteManifest(string directoryPath, string moduleName, string version)
    {
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(Path.Combine(directoryPath, moduleName + ".psd1"), $$"""
@{
    RootModule = '{{moduleName}}.psm1'
    ModuleVersion = '{{version}}'
}
""");
    }
}
