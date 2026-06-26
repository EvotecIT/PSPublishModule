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
    public void Collect_MarksEffectiveImportWinnerPerPowerShellEdition()
    {
        var desktopRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var coreRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteManifest(Path.Combine(desktopRoot.FullName, "Company.Tools", "1.0.0"), "Company.Tools", "1.0.0");
            WriteManifest(Path.Combine(coreRoot.FullName, "Company.Tools", "2.0.0"), "Company.Tools", "2.0.0");

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(desktopRoot.FullName, "Desktop", "CurrentUser"),
                new ModuleStateModulePath(coreRoot.FullName, "Core", "CurrentUser")
            }));

            var winners = inventory.InstalledModules
                .Where(static module => module.IsEffectiveImportCandidate)
                .OrderBy(static module => module.PowerShellEdition, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal(2, winners.Length);
            Assert.Contains(winners, static module => module.PowerShellEdition == "Core" && module.Version == "2.0.0");
            Assert.Contains(winners, static module => module.PowerShellEdition == "Desktop" && module.Version == "1.0.0");
        }
        finally
        {
            try { desktopRoot.Delete(recursive: true); } catch { /* best effort */ }
            try { coreRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Collect_DiscoversManifestlessScriptModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var flatModule = Path.Combine(root.FullName, "Company.ScriptOnly");
            Directory.CreateDirectory(flatModule);
            File.WriteAllText(Path.Combine(flatModule, "Company.ScriptOnly.psm1"), string.Empty);
            var versionedModule = Path.Combine(root.FullName, "Company.VersionedScript", "1.2.3");
            Directory.CreateDirectory(versionedModule);
            File.WriteAllText(Path.Combine(versionedModule, "Company.VersionedScript.psm1"), string.Empty);

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(root.FullName, "Core", "CurrentUser")
            }));

            Assert.Contains(inventory.InstalledModules, static module =>
                module.Name == "Company.ScriptOnly" &&
                module.Version == "0.0" &&
                module.Path!.EndsWith("Company.ScriptOnly", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(inventory.InstalledModules, static module =>
                module.Name == "Company.VersionedScript" &&
                module.Version == "1.2.3" &&
                module.Path!.EndsWith(Path.Combine("Company.VersionedScript", "1.2.3"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Collect_DiscoversVersionedBinaryOnlyModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleDirectory = Path.Combine(root.FullName, "Company.BinaryOnly", "1.2.3");
            Directory.CreateDirectory(moduleDirectory);
            File.WriteAllBytes(Path.Combine(moduleDirectory, "Company.BinaryOnly.dll"), Array.Empty<byte>());

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(root.FullName, "Core", "CurrentUser")
            }));

            var module = Assert.Single(inventory.InstalledModules);
            Assert.Equal("Company.BinaryOnly", module.Name);
            Assert.Equal("1.2.3", module.Version);
            Assert.Equal("Core", module.PowerShellEdition);
            Assert.Equal("CurrentUser", module.Scope);
            Assert.True(module.IsEffectiveImportCandidate);
            Assert.EndsWith(Path.Combine("Company.BinaryOnly", "1.2.3"), module.Path!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
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

    [Fact]
    public void Collect_ReadsRepositoryMetadataFromManifest()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteManifest(Path.Combine(root.FullName, "Company.Tools", "1.2.3"), "Company.Tools", "1.2.3", "CompanyModules");

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(root.FullName, "Core", "CurrentUser")
            }));

            var module = Assert.Single(inventory.InstalledModules);
            Assert.Equal("CompanyModules", module.SourceRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Collect_PreservesPrereleaseVersionFolderWhenManifestHasStableVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteManifest(Path.Combine(root.FullName, "Company.Tools", "1.2.0-preview1"), "Company.Tools", "1.2.0");

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(root.FullName, "Core", "CurrentUser")
            }));

            var module = Assert.Single(inventory.InstalledModules);
            Assert.Equal("1.2.0-preview1", module.Version);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Collect_AppendsManifestPrereleaseWhenFolderVersionIsStable()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteManifest(Path.Combine(root.FullName, "Company.Tools", "1.2.0"), "Company.Tools", "1.2.0", prerelease: "preview1");

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(root.FullName, "Core", "CurrentUser")
            }));

            var module = Assert.Single(inventory.InstalledModules);
            Assert.Equal("1.2.0-preview1", module.Version);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Collect_ReadsRepositoryMetadataFromPSGetModuleInfo()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleDirectory = Path.Combine(root.FullName, "Company.Tools", "1.2.3");
            WriteManifest(moduleDirectory, "Company.Tools", "1.2.3");
            File.WriteAllText(Path.Combine(moduleDirectory, "PSGetModuleInfo.xml"), """
<Obj>
  <MS>
    <S N="Repository">CompanyModules</S>
  </MS>
</Obj>
""");

            var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(new[]
            {
                new ModuleStateModulePath(root.FullName, "Core", "CurrentUser")
            }));

            var module = Assert.Single(inventory.InstalledModules);
            Assert.Equal("CompanyModules", module.SourceRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteManifest(string directoryPath, string moduleName, string version, string? repository = null, string? prerelease = null)
    {
        Directory.CreateDirectory(directoryPath);
        var repositoryLine = string.IsNullOrWhiteSpace(repository)
            ? string.Empty
            : $"    Repository = '{repository}'{Environment.NewLine}";
        var prereleaseBlock = string.IsNullOrWhiteSpace(prerelease)
            ? string.Empty
            : $$"""
    PrivateData = @{
        PSData = @{
            Prerelease = '{{prerelease}}'
        }
    }
""";
        File.WriteAllText(Path.Combine(directoryPath, moduleName + ".psd1"), $$"""
@{
    RootModule = '{{moduleName}}.psm1'
    ModuleVersion = '{{version}}'
{{repositoryLine}}{{prereleaseBlock}}}
""");
    }
}
