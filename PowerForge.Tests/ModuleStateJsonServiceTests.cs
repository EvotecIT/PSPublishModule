namespace PowerForge.Tests;

public sealed class ModuleStateJsonServiceTests
{
    [Fact]
    public void ReadInventory_LoadsInstalledModulesFromJson()
    {
        var inventory = new ModuleStateJsonService().ReadInventory("""
{
  "installedModules": [
    {
      "name": "Microsoft.Graph.Authentication",
      "version": "2.36.0",
      "powerShellEdition": "Core",
      "scope": "AllUsers",
      "path": "C:/Modules/Microsoft.Graph.Authentication/2.36.0",
      "sourceRepository": "CompanyModules",
      "isLoaded": true,
      "isEffectiveImportCandidate": true
    }
  ]
}
""");

        var module = Assert.Single(inventory.InstalledModules);
        Assert.Equal("Microsoft.Graph.Authentication", module.Name);
        Assert.Equal("2.36.0", module.Version);
        Assert.Equal("Core", module.PowerShellEdition);
        Assert.Equal("AllUsers", module.Scope);
        Assert.Equal("CompanyModules", module.SourceRepository);
        Assert.True(module.IsLoaded);
        Assert.True(module.IsEffectiveImportCandidate);
    }

    [Fact]
    public void ReadInventory_AcceptsGetModuleStateResultJson()
    {
        var inventory = new ModuleStateJsonService().ReadInventory("""
{
  "Source": "ModulePath",
  "ModulePaths": [ "C:/Modules" ],
  "InstalledModules": [
    {
      "Name": "Company.Tools",
      "Version": "1.2.3",
      "PowerShellEdition": "Core",
      "Scope": "CurrentUser",
      "Path": "C:/Modules/Company.Tools/1.2.3",
      "SourceRepository": "CompanyModules",
      "IsLoaded": true,
      "IsEffectiveImportCandidate": true
    }
  ]
}
""");

        var module = Assert.Single(inventory.InstalledModules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("1.2.3", module.Version);
        Assert.Equal("Core", module.PowerShellEdition);
        Assert.Equal("CurrentUser", module.Scope);
        Assert.Equal("C:/Modules/Company.Tools/1.2.3", module.Path);
        Assert.Equal("CompanyModules", module.SourceRepository);
        Assert.True(module.IsLoaded);
        Assert.True(module.IsEffectiveImportCandidate);
    }

    [Fact]
    public void ReadDesiredState_LoadsModulesAndFamilyPoliciesFromJson()
    {
        var desiredState = new ModuleStateJsonService().ReadDesiredState("""
{
  "modules": [
    { "name": "Company.Tools", "versionPolicy": ">=1.2.0", "allowedSources": [ "CompanyModules" ], "scope": "AllUsers" }
  ],
  "families": [
    {
      "name": "Graph",
      "modules": [
        "Microsoft.Graph.Authentication",
        "Microsoft.Graph.Users"
      ],
      "coherenceRule": "SameVersion"
    }
  ]
}
""");

        var module = Assert.Single(desiredState.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal(">=1.2.0", module.VersionPolicy);
        Assert.Equal(new[] { "CompanyModules" }, module.AllowedSources);
        Assert.Equal("AllUsers", module.Scope);

        var family = Assert.Single(desiredState.FamilyPolicies);
        Assert.Equal("Graph", family.Name);
        Assert.Equal(new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" }, family.Modules);
        Assert.Equal(ModuleStateFamilyCoherenceRule.SameVersion, family.CoherenceRule);
    }

    [Fact]
    public void ReadDesiredState_PreservesVersionAliases()
    {
        var desiredState = new ModuleStateJsonService().ReadDesiredState("""
{
  "modules": [
    { "name": "Company.Tools", "version": "=1.2.0", "repository": "CompanyModules" },
    { "name": "Company.Runtime", "requiredVersion": "2.0.0" }
  ]
}
""");

        Assert.Equal(2, desiredState.Modules.Length);
        Assert.Equal("=1.2.0", desiredState.Modules[0].VersionPolicy);
        Assert.Equal("CompanyModules", Assert.Single(desiredState.Modules[0].AllowedSources));
        Assert.Equal("2.0.0", desiredState.Modules[1].VersionPolicy);
    }

    [Fact]
    public void ReadMaintenanceReceipt_LoadsMaintainedModulesFromJson()
    {
        var receipt = new ModuleStateJsonService().ReadMaintenanceReceipt("""
{
  "source": "Company baseline",
  "maintainedModules": [
    {
      "name": "Company.Tools",
      "version": "1.2.0",
      "sourceRepository": "CompanyModules",
      "scope": "AllUsers"
    }
  ]
}
""");

        Assert.Equal("Company baseline", receipt.Source);
        var module = Assert.Single(receipt.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("1.2.0", module.Version);
        Assert.Equal("CompanyModules", module.SourceRepository);
        Assert.Equal("AllUsers", module.Scope);
    }
}
