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
