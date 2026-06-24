namespace PowerForge.Tests;

public sealed class ModuleStateFamilyCoherenceAnalyzerTests
{
    [Fact]
    public void Analyze_FlagsMixedVersionsInsideManagedFamily()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.36.0", powerShellEdition: "Core", scope: "AllUsers"),
            new ModuleStateInstalledModule("Microsoft.Graph.Groups", "2.36.0", powerShellEdition: "Core", scope: "AllUsers"),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0", powerShellEdition: "Core", scope: "AllUsers")
        });
        var policy = new ModuleStateFamilyPolicy(
            "Graph",
            new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Groups", "Microsoft.Graph.Users" });

        var findings = new ModuleStateFamilyCoherenceAnalyzer().Analyze(inventory, new[] { policy });

        var finding = Assert.Single(findings);
        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.FamilyVersionMismatch", finding.Code);
        Assert.Equal("Graph", finding.FamilyName);
        Assert.Equal(new[] { "2.36.0", "2.38.0" }, finding.Versions);
        Assert.Contains("Microsoft.Graph.Authentication", finding.ModuleNames);
        Assert.Contains("Microsoft.Graph.Users", finding.ModuleNames);
    }

    [Fact]
    public void Analyze_AllowsSameVersionInsideManagedFamily()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.38"),
            new ModuleStateInstalledModule("Microsoft.Graph.Groups", "2.38.0"),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0")
        });
        var policy = new ModuleStateFamilyPolicy(
            "Graph",
            new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Groups", "Microsoft.Graph.Users" });

        var findings = new ModuleStateFamilyCoherenceAnalyzer().Analyze(inventory, new[] { policy });

        Assert.Empty(findings);
    }

    [Fact]
    public void Analyze_IgnoresModulesOutsideFamilyPolicy()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.38.0"),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.38.0"),
            new ModuleStateInstalledModule("Az.Accounts", "4.1.0")
        });
        var policy = new ModuleStateFamilyPolicy(
            "Graph",
            new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" });

        var findings = new ModuleStateFamilyCoherenceAnalyzer().Analyze(inventory, new[] { policy });

        Assert.Empty(findings);
    }

    [Fact]
    public void Analyze_DeduplicatesCaseInsensitiveModuleNamesAndVersions()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Microsoft.Graph.Authentication", "2.38.0"),
            new ModuleStateInstalledModule("microsoft.graph.authentication", "2.38.0"),
            new ModuleStateInstalledModule("Microsoft.Graph.Users", "2.39.0")
        });
        var policy = new ModuleStateFamilyPolicy(
            "Graph",
            new[] { "Microsoft.Graph.Authentication", "MICROSOFT.GRAPH.AUTHENTICATION", "Microsoft.Graph.Users" });

        var finding = Assert.Single(new ModuleStateFamilyCoherenceAnalyzer().Analyze(inventory, new[] { policy }));

        Assert.Equal(new[] { "Microsoft.Graph.Authentication", "Microsoft.Graph.Users" }, finding.ModuleNames);
        Assert.Equal(new[] { "2.38.0", "2.39.0" }, finding.Versions);
    }

    [Fact]
    public void FamilyCatalog_ResolvesGraphAliasOnce()
    {
        var policies = new ModuleStateFamilyCatalog().Resolve(new[] { "Graph", "MicrosoftGraph" });

        var policy = Assert.Single(policies);
        Assert.Equal("MicrosoftGraph", policy.Name);
        Assert.Contains("Microsoft.Graph.Authentication", policy.Modules);
        Assert.Equal(ModuleStateFamilyCoherenceRule.SameVersion, policy.CoherenceRule);
    }

    [Fact]
    public void FamilyCatalog_UsesObserveOnlyForFamiliesWithoutSameVersionRule()
    {
        var policy = Assert.Single(new ModuleStateFamilyCatalog().Resolve(new[] { "Az" }));

        Assert.Equal("Az", policy.Name);
        Assert.Equal(ModuleStateFamilyCoherenceRule.ObserveOnly, policy.CoherenceRule);
    }

    [Fact]
    public void FamilyCatalog_RejectsUnknownFamily()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ModuleStateFamilyCatalog().Resolve(new[] { "UnknownFamily" }));

        Assert.Contains("Unknown ModuleState family", exception.Message);
    }
}
