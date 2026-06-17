using System;
using System.IO;

namespace PowerForge.Tests;

public sealed class BinaryConflictMitigationClassifierTests
{
    [Fact]
    public void SuppressCurrentModuleConflictsMitigatedByAlc_SuppressesCoreAutoMode()
    {
        var result = CreateResult("Core");

        var filtered = BinaryConflictMitigationClassifier.SuppressCurrentModuleConflictsMitigatedByAlc(
            result,
            useAssemblyLoadContext: true,
            strictAnalysis: false);

        Assert.False(filtered.HasConflicts);
        Assert.Contains("mitigated", filtered.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuppressCurrentModuleConflictsMitigatedByAlc_KeepsDesktopAndStrictFindings()
    {
        var desktop = BinaryConflictMitigationClassifier.SuppressCurrentModuleConflictsMitigatedByAlc(
            CreateResult("Desktop"),
            useAssemblyLoadContext: true,
            strictAnalysis: false);
        var strict = BinaryConflictMitigationClassifier.SuppressCurrentModuleConflictsMitigatedByAlc(
            CreateResult("Core"),
            useAssemblyLoadContext: true,
            strictAnalysis: true);

        Assert.True(desktop.HasConflicts);
        Assert.True(strict.HasConflicts);
    }

    [Fact]
    public void ModuleHasAssemblyLoadContextIsolation_DetectsPowerForgeGeneratedBootstrapper()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "Demo.psm1"),
                "$ModuleAssembly = [Demo.ModuleLoadContext.ModuleAssemblyLoadContext]::LoadModule($ModuleAssemblyPath, 'Demo')");

            Assert.True(BinaryConflictMitigationClassifier.ModuleHasAssemblyLoadContextIsolation(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void IsRequiredModuleConflictMitigatedByAlc_UsesEitherSideInCoreAutoMode()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var isolatedRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "IsolatedModule"));
            var regularRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "RegularModule"));
            File.WriteAllText(
                Path.Combine(isolatedRoot.FullName, "IsolatedModule.psm1"),
                "Import-Module -Assembly $ModuleAssembly # AssemblyLoadContext");

            var isolated = new InstalledModuleMetadata("IsolatedModule", "1.0.0", null, isolatedRoot.FullName);
            var regular = new InstalledModuleMetadata("RegularModule", "1.0.0", null, regularRoot.FullName);

            Assert.True(BinaryConflictMitigationClassifier.IsRequiredModuleConflictMitigatedByAlc(
                isolated,
                regular,
                "Core",
                strictAnalysis: false));
            Assert.False(BinaryConflictMitigationClassifier.IsRequiredModuleConflictMitigatedByAlc(
                isolated,
                regular,
                "Desktop",
                strictAnalysis: false));
            Assert.False(BinaryConflictMitigationClassifier.IsRequiredModuleConflictMitigatedByAlc(
                isolated,
                regular,
                "Core",
                strictAnalysis: true));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static BinaryConflictDetectionResult CreateResult(string edition)
    {
        return new BinaryConflictDetectionResult(
            powerShellEdition: edition,
            moduleRoot: @"C:\Repo\TestModule",
            assemblyRootPath: @"C:\Repo\TestModule\Lib\Core",
            assemblyRootRelativePath: @"Lib\Core",
            issues: new[]
            {
                new BinaryConflictDetectionIssue(
                    powerShellEdition: edition,
                    assemblyName: "SharedAuth",
                    payloadAssemblyFileName: "SharedAuth.dll",
                    payloadAssemblyVersion: "2.0.0.0",
                    installedModuleName: "OtherModule",
                    installedModuleVersion: "1.0.0",
                    installedAssemblyVersion: "1.0.0.0",
                    installedAssemblyRelativePath: "OtherModule/1.0.0/bin/SharedAuth.dll",
                    installedAssemblyPath: @"C:\Modules\OtherModule\1.0.0\bin\SharedAuth.dll",
                    versionComparison: 1)
            },
            summary: "1 conflict across 1 module source");
    }
}
