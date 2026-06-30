using System.Collections;
using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ModuleStateObjectAdapterTests
{
    [Fact]
    public void ToDesiredState_AcceptsHashtableWithModuleObjects()
    {
        var desired = new Hashtable
        {
            ["Modules"] = new object[]
            {
                new Hashtable
                {
                    ["Name"] = "Company.Tools",
                    ["Version"] = "=1.2.0",
                    ["Repository"] = "CompanyModules",
                    ["Scope"] = "CurrentUser",
                    ["Path"] = @"C:\OfflineModules",
                    ["ExpectedPackageSha256"] = new string('A', 64)
                }
            },
            ["Families"] = new object[]
            {
                new Hashtable
                {
                    ["Name"] = "CompanySuite",
                    ["Modules"] = new[] { "Company.Tools", "Company.Runtime" },
                    ["CoherenceRule"] = "SameVersion"
                }
            }
        };

        var state = ModuleStateObjectAdapter.ToDesiredState(desired);

        var module = Assert.Single(state.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("=1.2.0", module.VersionPolicy);
        Assert.Equal("CompanyModules", Assert.Single(module.AllowedSources));
        Assert.Equal("CurrentUser", module.Scope);
        Assert.Equal(@"C:\OfflineModules", module.TargetPath);
        Assert.Equal(new string('a', 64), module.ExpectedPackageSha256);

        var family = Assert.Single(state.FamilyPolicies);
        Assert.Equal("CompanySuite", family.Name);
        Assert.Equal(new[] { "Company.Tools", "Company.Runtime" }, family.Modules);
        Assert.Equal(ModuleStateFamilyCoherenceRule.SameVersion, family.CoherenceRule);
    }

    [Fact]
    public void ToDesiredState_AcceptsSingleModuleHashtableUnderModules()
    {
        var desired = new Hashtable
        {
            ["Modules"] = new Hashtable
            {
                ["Name"] = "Company.Tools",
                ["Version"] = "=1.2.0",
                ["Repository"] = "CompanyModules"
            }
        };

        var state = ModuleStateObjectAdapter.ToDesiredState(desired);

        var module = Assert.Single(state.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("=1.2.0", module.VersionPolicy);
        Assert.Equal("CompanyModules", Assert.Single(module.AllowedSources));
    }

    [Fact]
    public void ToDesiredState_AcceptsModuleNameArray()
    {
        var state = ModuleStateObjectAdapter.ToDesiredState(new[] { "Company.Tools", "Company.Runtime" });

        Assert.Equal(new[] { "Company.Tools", "Company.Runtime" }, state.Modules.Select(static module => module.Name));
        Assert.All(state.Modules, static module => Assert.Equal("*", module.VersionPolicy));
    }

    [Fact]
    public void ToDesiredState_AcceptsSingleModuleHashtable()
    {
        var desired = new Hashtable
        {
            ["Name"] = "Company.Tools",
            ["Version"] = "=1.2.0",
            ["Repository"] = "CompanyModules",
            ["Scope"] = "AllUsers"
        };

        var state = ModuleStateObjectAdapter.ToDesiredState(desired);

        var module = Assert.Single(state.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("=1.2.0", module.VersionPolicy);
        Assert.Equal("CompanyModules", Assert.Single(module.AllowedSources));
        Assert.Equal("AllUsers", module.Scope);
    }

    [Fact]
    public void ToDesiredState_AcceptsFamilyOnlyHashtable()
    {
        var desired = new Hashtable
        {
            ["Families"] = new object[]
            {
                new Hashtable
                {
                    ["Name"] = "Graph",
                    ["Modules"] = new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" }
                }
            }
        };

        var state = ModuleStateObjectAdapter.ToDesiredState(desired);

        Assert.Empty(state.Modules);
        var family = Assert.Single(state.FamilyPolicies);
        Assert.Equal("Graph", family.Name);
        Assert.Equal(new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" }, family.Modules);
    }

    [Fact]
    public void ToDesiredState_AcceptsPSCustomObject()
    {
        var desired = new PSObject();
        desired.Properties.Add(new PSNoteProperty("Modules", new object[]
        {
            new { Name = "Company.Tools", RequiredVersion = "1.2.0", Repositories = new[] { "CompanyModules" } }
        }));

        var state = ModuleStateObjectAdapter.ToDesiredState(desired);

        var module = Assert.Single(state.Modules);
        Assert.Equal("Company.Tools", module.Name);
        Assert.Equal("1.2.0", module.VersionPolicy);
        Assert.Equal("CompanyModules", Assert.Single(module.AllowedSources));
    }
}
