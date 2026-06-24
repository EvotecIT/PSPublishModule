namespace PowerForge.Tests;

public sealed class ModuleStateReceiptDriftAnalyzerTests
{
    [Fact]
    public void Analyze_FlagsReceiptModuleMissing()
    {
        var inventory = new ModuleStateInventory(Array.Empty<ModuleStateInstalledModule>());
        var receipt = new ModuleStateMaintenanceReceipt(
            "Company baseline",
            new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0") });

        var finding = Assert.Single(new ModuleStateReceiptDriftAnalyzer().Analyze(inventory, new[] { receipt }));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.ReceiptModuleMissing", finding.Code);
        Assert.Equal(new[] { "Company.Tools" }, finding.ModuleNames);
        Assert.Equal(new[] { "1.2.0" }, finding.Versions);
        Assert.Contains("Company baseline", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_FlagsReceiptVersionDrift()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.3.0")
        });
        var receipt = new ModuleStateMaintenanceReceipt(
            null,
            new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0") });

        var finding = Assert.Single(new ModuleStateReceiptDriftAnalyzer().Analyze(inventory, new[] { receipt }));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.ReceiptVersionDrift", finding.Code);
        Assert.Equal(new[] { "1.2.0", "1.3.0" }, finding.Versions);
    }

    [Fact]
    public void Analyze_FlagsReceiptSourceDrift()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", sourceRepository: "PublicGallery")
        });
        var receipt = new ModuleStateMaintenanceReceipt(
            null,
            new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules") });

        var finding = Assert.Single(new ModuleStateReceiptDriftAnalyzer().Analyze(inventory, new[] { receipt }));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.ReceiptSourceDrift", finding.Code);
    }

    [Fact]
    public void Analyze_FlagsReceiptScopeDriftAsError()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", scope: "CurrentUser", sourceRepository: "CompanyModules")
        });
        var receipt = new ModuleStateMaintenanceReceipt(
            null,
            new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules", scope: "AllUsers") });

        var finding = Assert.Single(new ModuleStateReceiptDriftAnalyzer().Analyze(inventory, new[] { receipt }));

        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.ReceiptScopeDrift", finding.Code);
    }

    [Fact]
    public void Analyze_AllowsMatchingReceiptState()
    {
        var inventory = new ModuleStateInventory(new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2", scope: "AllUsers", sourceRepository: "CompanyModules")
        });
        var receipt = new ModuleStateMaintenanceReceipt(
            null,
            new[] { new ModuleStateMaintenanceReceiptModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules", scope: "AllUsers") });

        Assert.Empty(new ModuleStateReceiptDriftAnalyzer().Analyze(inventory, new[] { receipt }));
    }
}
