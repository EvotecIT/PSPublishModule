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

        var finding = Assert.Single(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired),
            static finding => finding.Code == "ModuleState.SourcePreferenceMismatch");

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

        var finding = Assert.Single(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired),
            static finding => finding.Code == "ModuleState.SourcePreferenceMismatch");

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

        var finding = Assert.Single(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired),
            static finding => finding.Code == "ModuleState.SourcePreferenceMismatch");

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.SourcePreferenceMismatch", finding.Code);
        Assert.Equal(new[] { "1.0.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_WarnsWhenDesiredScopeIsShadowedByPolicySatisfyingEffectiveCopy()
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

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
        Assert.Equal("ModuleState.ScopeShadowing", finding.Code);
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

        var finding = Assert.Single(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired),
            static finding => finding.Code == "ModuleState.SourcePreferenceMismatch");

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
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0"),
            new ModuleStateDesiredModule("Company.Legacy", ">=1.0.0")
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
    public void Analyze_FlagsSideBySideVersionsWithinSameScope()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser"),
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "CurrentUser")
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0")
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
        Assert.Equal("ModuleState.SideBySideVersions", finding.Code);
        Assert.Equal("CurrentUser", finding.Scope);
        Assert.Equal(new[] { "1.0.0", "1.3.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_FlagsDesiredScopeShadowedByEffectiveImportCandidate()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "AllUsers"),
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", scope: "CurrentUser", isEffectiveImportCandidate: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.2.0", scope: "AllUsers")
        };

        var finding = Assert.Single(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.ScopeShadowing", finding.Code);
        Assert.Equal("CurrentUser", finding.Scope);
        Assert.Equal(new[] { "1.0.0", "1.3.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_AllowsShadowingCopyWhenVersionMatchesDesiredScope()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "AllUsers"),
            new ModuleStateInstalledModule("Company.Tools", "1.3.0", scope: "CurrentUser", isEffectiveImportCandidate: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.2.0", scope: "AllUsers")
        };

        Assert.Empty(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));
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

        var finding = Assert.Single(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired),
            static finding => finding.Code == "ModuleState.DowngradeRequiresCleanup");

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.DowngradeRequiresCleanup", finding.Code);
        Assert.Equal(new[] { "2.0.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_BlocksDowngradePolicyWhenLoadedVersionSatisfiesButEffectiveDiskCopyDoesNot()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.0.0", isLoaded: true),
            new ModuleStateInstalledModule("Company.Tools", "3.0.0", isEffectiveImportCandidate: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", "<2.0.0")
        };

        var finding = Assert.Single(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired),
            static finding => finding.Code == "ModuleState.DowngradeRequiresCleanup");

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.DowngradeRequiresCleanup", finding.Code);
        Assert.Equal(new[] { "3.0.0" }, finding.Versions);
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

        var finding = Assert.Single(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired),
            static finding => finding.Code == "ModuleState.LoadedVersionMismatch");

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

    [Fact]
    public void Analyze_AllowsLoadedPrereleaseWhenDesiredPolicyIncludesPrerelease()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0-preview1", isLoaded: true)
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.2.0-preview1", includePrerelease: true)
        };

        Assert.Empty(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));
    }

    [Fact]
    public void Analyze_ReportsCrossScopeCommandConflictsOnlyWhenRepairScopeIsRequested()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule(
                "Company.Tools",
                "1.3.0",
                scope: "AllUsers",
                exportedCommands: new[] { "Get-CompanyThing" }),
            new ModuleStateInstalledModule(
                "Company.Legacy",
                "1.0.0",
                scope: "CurrentUser",
                exportedCommands: new[] { "Get-CompanyThing" })
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0")
        };

        Assert.Empty(new ModuleStateConflictAnalyzer().Analyze(inventory, desired));

        var finding = Assert.Single(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired, includeCrossScopeCommandConflicts: true),
            static finding => finding.Code == "ModuleState.CrossScopeCommandConflict");

        Assert.Equal(ModuleStateConflictSeverity.Warning, finding.Severity);
        Assert.Equal(new[] { "Company.Legacy", "Company.Tools" }, finding.ModuleNames);
        Assert.Equal(new[] { "1.0.0", "1.3.0" }, finding.Versions);
        Assert.Contains("Get-CompanyThing", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_DoesNotReportSameScopeCommandConflictsAsRepairCrossScopeConflicts()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule(
                "Company.Tools",
                "1.3.0",
                scope: "CurrentUser",
                exportedCommands: new[] { "Get-CompanyThing" }),
            new ModuleStateInstalledModule(
                "Company.Legacy",
                "1.0.0",
                scope: "CurrentUser",
                exportedCommands: new[] { "Get-CompanyThing" })
        });
        var desired = new[]
        {
            new ModuleStateDesiredModule("Company.Tools", ">=1.0.0")
        };

        Assert.DoesNotContain(
            new ModuleStateConflictAnalyzer().Analyze(inventory, desired, includeCrossScopeCommandConflicts: true),
            static finding => finding.Code == "ModuleState.CrossScopeCommandConflict");
    }
}
