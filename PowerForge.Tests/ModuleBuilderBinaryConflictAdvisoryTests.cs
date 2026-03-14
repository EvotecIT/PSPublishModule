using System;

namespace PowerForge.Tests;

public sealed class ModuleBuilderBinaryConflictAdvisoryTests
{
    [Fact]
    public void BuildBinaryConflictAdvisorySummary_GroupsModulesAndAssemblies()
    {
        var result = new BinaryConflictDetectionResult(
            powerShellEdition: "Desktop",
            moduleRoot: @"C:\Repo\TestModule",
            assemblyRootPath: @"C:\Repo\TestModule\Lib\Default",
            assemblyRootRelativePath: @"Lib\Default",
            issues: new[]
            {
                CreateIssue("System.Memory", "4.0.5.0", "AzureADHybridAuthenticationManagement", "2.4.71.0", "4.0.1.2", 1),
                CreateIssue("System.Runtime.CompilerServices.Unsafe", "6.0.3.0", "AzureADHybridAuthenticationManagement", "2.4.71.0", "4.0.4.1", 1),
                CreateIssue("Microsoft.Bcl.AsyncInterfaces", "9.0.0.0", "AzureADHybridAuthenticationManagement", "2.4.71.0", "9.0.0.9", -1),
                CreateIssue("System.Memory", "4.0.5.0", "DomainDetective", "0.2.0.1", "4.0.1.2", 1),
                CreateIssue("System.Memory", "4.0.5.0", "DomainDetective", "0.2.0.2", "4.0.6.0", -1),
                CreateIssue("System.Memory", "4.0.5.0", "OtherModule", "1.0.0", "4.0.6.0", -1)
            },
            summary: "6 conflicts across 2 module sources");

        var advisory = ModuleBuilder.BuildBinaryConflictAdvisorySummary(result);

        Assert.Equal(3, advisory.DistinctPayloadAssemblies);
        Assert.Equal(3, advisory.DistinctInstalledModules);
        Assert.Equal(3, advisory.PayloadNewerConflicts);
        Assert.Equal(3, advisory.PayloadOlderConflicts);

        var topModule = Assert.Single(advisory.TopModules, static module => module.ModuleLabel == "AzureADHybridAuthenticationManagement 2.4.71.0");
        Assert.Equal(3, topModule.ConflictCount);
        Assert.Equal(3, topModule.DistinctAssemblies);
        Assert.Equal(2, topModule.PayloadNewerCount);
        Assert.Equal(1, topModule.PayloadOlderCount);
        Assert.DoesNotContain(advisory.AllModules, static module => module.ModuleLabel == "DomainDetective 0.2.0.1");
        Assert.Contains(advisory.AllModules, static module => module.ModuleLabel == "DomainDetective 0.2.0.2");

        var topAssembly = Assert.Single(advisory.TopAssemblies, static assembly => assembly.AssemblyLabel == "System.Memory 4.0.5.0");
        Assert.Equal(3, topAssembly.ConflictCount);
        Assert.Equal(3, topAssembly.DistinctModules);
        Assert.Equal(0, advisory.RemainingModuleCount);
        Assert.Equal(0, advisory.RemainingAssemblyCount);
    }

    [Fact]
    public void BuildBinaryConflictAdvisorySummary_ExplainsWhenTheWarningIsActionable()
    {
        var result = new BinaryConflictDetectionResult(
            powerShellEdition: "Core",
            moduleRoot: @"C:\Repo\TestModule",
            assemblyRootPath: @"C:\Repo\TestModule\Lib\Core",
            assemblyRootRelativePath: @"Lib\Core",
            issues: new[]
            {
                CreateIssue("System.Memory", "4.0.5.0", "LegacyModule", "1.0.0", "4.0.1.2", 1)
            },
            summary: "1 conflict across 1 module source");

        var advisory = ModuleBuilder.BuildBinaryConflictAdvisorySummary(result);

        Assert.Contains("Ignore unless you use this module together", advisory.Actionability, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LegacyModule 1.0.0", advisory.Actionability, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteBinaryConflictReport_WritesDedupedModulesAndExactVersionPairs()
    {
        var result = new BinaryConflictDetectionResult(
            powerShellEdition: "Desktop",
            moduleRoot: @"C:\Repo\TestModule",
            assemblyRootPath: @"C:\Repo\TestModule\Lib\Default",
            assemblyRootRelativePath: @"Lib\Default",
            issues: new[]
            {
                CreateIssue("System.Memory", "4.0.5.0", "LegacyModule", "1.0.0", "4.0.1.2", 1),
                CreateIssue("System.Text.Json", "9.0.0.0", "LegacyModule", "2.0.0", "8.0.0.6", 1),
                CreateIssue("System.Memory", "4.0.5.0", "OtherModule", "3.0.0", "4.0.6.0", -1)
            },
            summary: "3 conflicts across 1 module source");

        var advisory = ModuleBuilder.BuildBinaryConflictAdvisorySummary(result);
        var reportsRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-binary-conflict-report-" + Guid.NewGuid().ToString("N")));

        try
        {
            var builder = new ModuleBuilder(new NullLogger());
            var reportPath = builder.WriteBinaryConflictReport(reportsRoot.FullName, advisory, result);

            Assert.False(string.IsNullOrWhiteSpace(reportPath));
            Assert.True(File.Exists(reportPath), "Expected binary conflict report file to exist.");

            var text = File.ReadAllText(reportPath!);
            Assert.Contains("Binary conflict report for Desktop", text, StringComparison.Ordinal);
            Assert.Contains("Installed modules below already keep only the newest installed version per module name.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LegacyModule 1.0.0", text, StringComparison.Ordinal);
            Assert.Contains("LegacyModule 2.0.0", text, StringComparison.Ordinal);
            Assert.Contains("System.Text.Json: ours 9.0.0.0, theirs 8.0.0.6 (ours newer)", text, StringComparison.Ordinal);
            Assert.Contains("OtherModule 3.0.0", text, StringComparison.Ordinal);
        }
        finally
        {
            try { reportsRoot.Delete(recursive: true); } catch { }
        }
    }

    private static BinaryConflictDetectionIssue CreateIssue(
        string assemblyName,
        string payloadAssemblyVersion,
        string installedModuleName,
        string installedModuleVersion,
        string installedAssemblyVersion,
        int versionComparison)
    {
        return new BinaryConflictDetectionIssue(
            powerShellEdition: "Core",
            assemblyName: assemblyName,
            payloadAssemblyFileName: assemblyName + ".dll",
            payloadAssemblyVersion: payloadAssemblyVersion,
            installedModuleName: installedModuleName,
            installedModuleVersion: installedModuleVersion,
            installedAssemblyVersion: installedAssemblyVersion,
            installedAssemblyRelativePath: installedModuleName + "/" + installedModuleVersion + "/bin/" + assemblyName + ".dll",
            installedAssemblyPath: @"C:\Modules\" + installedModuleName + @"\" + installedModuleVersion + @"\bin\" + assemblyName + ".dll",
            versionComparison: versionComparison);
    }
}
