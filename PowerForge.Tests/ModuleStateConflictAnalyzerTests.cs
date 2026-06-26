namespace PowerForge.Tests;

public sealed class ModuleStateConflictAnalyzerTests
{
    [Fact]
    public void Analyze_FlagsSourcePreferenceMismatch()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", sourceRepository: "PublicGallery", isEffectiveImportCandidate: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0", new[] { "CompanyModules" })
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.SourcePreferenceMismatch", finding.Code);
        Assert.Equal(new[] { "Company.Tools" }, finding.ModuleNames);
        Assert.Equal(new[] { "1.2.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_UsesEffectiveImportCandidateForSourcePreference()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", sourceRepository: "PublicGallery", isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", sourceRepository: "CompanyModules")
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0", new[] { "CompanyModules" })
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.SourcePreferenceMismatch", finding.Code);
        Assert.Equal(new[] { "1.0.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_PrefersLoadedModuleForSourcePreference()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", sourceRepository: "PublicGallery", isLoaded: true, isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", sourceRepository: "CompanyModules", isEffectiveImportCandidate: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0", new[] { "CompanyModules" })
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.SourcePreferenceMismatch", finding.Code);
        Assert.Equal(new[] { "1.0.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_UsesDesiredScopeForSourcePreference()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser", sourceRepository: "PublicGallery", isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "AllUsers", sourceRepository: "CompanyModules")
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0", new[] { "CompanyModules" }, scope: "AllUsers")
        };

        Assert.Empty(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));
    }

    [Fact]
    public void Analyze_FlagsSourceMismatchWithinDesiredScope()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser", sourceRepository: "CompanyModules", isEffectiveImportCandidate: true),
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "AllUsers", sourceRepository: "PublicGallery")
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0", new[] { "CompanyModules" }, scope: "AllUsers")
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.SourcePreferenceMismatch", finding.Code);
        Assert.Equal(new[] { "1.3.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_WarnsWhenDesiredSourceIsKnownButInstalledSourceIsUnknown()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0")
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0", new[] { "CompanyModules" })
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
        Assert.Equal("ModuleState.SourceUnknown", finding.Code);
    }

    [Fact]
    public void Analyze_FlagsScopeAmbiguityWhenMultipleScopesExposeDifferentVersions()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "AllUsers"),
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "CurrentUser")
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0")
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
        Assert.Equal("ModuleState.ScopeAmbiguity", finding.Code);
        Assert.Equal(new[] { "1.0.0", "1.3.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_FlagsDesiredScopeMismatch()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "CurrentUser")
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0", scope: "AllUsers")
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
        Assert.Equal("ModuleState.ScopeMismatch", finding.Code);
        Assert.Equal(new[] { "Company.Tools" }, finding.ModuleNames);
        Assert.Equal(new[] { "1.3.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_DoesNotBlockPlannedVersionRepairForDifferentSource()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", sourceRepository: "PublicGallery", isEffectiveImportCandidate: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=2.0.0", new[] { "CompanyModules" })
        };

        Assert.Empty(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));
    }

    [Fact]
    public void Analyze_BlocksDowngradePolicyUntilHigherVersionIsCleaned()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "2.0.0", sourceRepository: "CompanyModules", isEffectiveImportCandidate: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", "<2.0.0", new[] { "CompanyModules" })
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.DowngradeRequiresCleanup", finding.Code);
        Assert.Equal(new[] { "2.0.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_FlagsLoadedModuleThatDoesNotSatisfyDesiredPolicy()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", isLoaded: true),
            new ModuleStateInstalledModule("Company.Tools", "1.3.0")
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.2.0")
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.LoadedVersionMismatch", finding.Code);
        Assert.Equal(new[] { "1.0.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_AllowsLoadedModuleThatSatisfiesDesiredPolicy()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", isLoaded: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.2.0")
        };

        Assert.Empty(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));
    }
}
