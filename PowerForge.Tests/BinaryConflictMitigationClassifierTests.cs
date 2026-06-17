using System;
using System.IO;

namespace PowerForge.Tests;

public sealed class BinaryConflictMitigationClassifierTests
{
    [Fact]
    public void SuppressCurrentModuleConflictsMitigatedByAlc_SuppressesCoreAutoMode()
    {
        var root = CreateModuleRootWithAssemblyLoadContextMarker();
        try
        {
            var result = CreateResult("Core", root.FullName);

            var filtered = BinaryConflictMitigationClassifier.SuppressCurrentModuleConflictsMitigatedByAlc(
                result,
                useAssemblyLoadContext: true,
                strictAnalysis: false);

            Assert.False(filtered.HasConflicts);
            Assert.Contains("mitigated", filtered.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SuppressCurrentModuleConflictsMitigatedByAlc_KeepsDesktopAndStrictFindings()
    {
        var root = CreateModuleRootWithAssemblyLoadContextMarker();
        try
        {
            var desktop = BinaryConflictMitigationClassifier.SuppressCurrentModuleConflictsMitigatedByAlc(
                CreateResult("Desktop", root.FullName),
                useAssemblyLoadContext: true,
                strictAnalysis: false);
            var strict = BinaryConflictMitigationClassifier.SuppressCurrentModuleConflictsMitigatedByAlc(
                CreateResult("Core", root.FullName),
                useAssemblyLoadContext: true,
                strictAnalysis: true);

            Assert.True(desktop.HasConflicts);
            Assert.True(strict.HasConflicts);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SuppressCurrentModuleConflictsMitigatedByAlc_KeepsCoreFindingsWithoutGeneratedLoader()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Demo.psm1"), "# regular bootstrapper");

            var filtered = BinaryConflictMitigationClassifier.SuppressCurrentModuleConflictsMitigatedByAlc(
                CreateResult("Core", root.FullName),
                useAssemblyLoadContext: true,
                strictAnalysis: false);

            Assert.True(filtered.HasConflicts);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void IsCurrentModuleConflictMitigatedByAlc_RequiresGeneratedLoaderMarker()
    {
        var root = CreateModuleRootWithAssemblyLoadContextMarker();
        var plainRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(plainRoot.FullName, "Demo.psm1"), "# regular bootstrapper");

            Assert.True(BinaryConflictMitigationClassifier.IsCurrentModuleConflictMitigatedByAlc(
                useAssemblyLoadContext: true,
                powerShellEdition: "Core",
                strictAnalysis: false,
                moduleRoot: root.FullName));
            Assert.False(BinaryConflictMitigationClassifier.IsCurrentModuleConflictMitigatedByAlc(
                useAssemblyLoadContext: true,
                powerShellEdition: "Core",
                strictAnalysis: false,
                moduleRoot: plainRoot.FullName));
            Assert.False(BinaryConflictMitigationClassifier.IsCurrentModuleConflictMitigatedByAlc(
                useAssemblyLoadContext: true,
                powerShellEdition: "Desktop",
                strictAnalysis: false,
                moduleRoot: root.FullName));
            Assert.False(BinaryConflictMitigationClassifier.IsCurrentModuleConflictMitigatedByAlc(
                useAssemblyLoadContext: true,
                powerShellEdition: "Core",
                strictAnalysis: true,
                moduleRoot: root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { plainRoot.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ModuleHasAssemblyLoadContextIsolation_DetectsPowerForgeGeneratedBootstrapper()
    {
        var root = CreateModuleRootWithAssemblyLoadContextMarker();
        try
        {
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
            var isolatedRoot = CreateModuleRootWithAssemblyLoadContextMarker(root.FullName, "IsolatedModule");
            var regularRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "RegularModule"));

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

    private static DirectoryInfo CreateModuleRootWithAssemblyLoadContextMarker(string? parent = null, string moduleName = "Demo")
    {
        var rootPath = parent is null
            ? Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"))
            : Path.Combine(parent, moduleName);
        var root = Directory.CreateDirectory(rootPath);
        File.WriteAllText(
            Path.Combine(root.FullName, moduleName + ".psm1"),
            "$ModuleAssembly = [Demo.ModuleLoadContext.ModuleAssemblyLoadContext]::LoadModule($ModuleAssemblyPath, 'Demo')");
        return root;
    }

    private static BinaryConflictDetectionResult CreateResult(string edition, string moduleRoot)
    {
        return new BinaryConflictDetectionResult(
            powerShellEdition: edition,
            moduleRoot: moduleRoot,
            assemblyRootPath: Path.Combine(moduleRoot, "Lib", edition),
            assemblyRootRelativePath: "Lib/" + edition,
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
