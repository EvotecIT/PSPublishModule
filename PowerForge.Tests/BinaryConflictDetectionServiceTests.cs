using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed class BinaryConflictDetectionServiceTests
{
    [Fact]
    public void Analyze_FindsVersionMismatchAcrossInstalledModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var stagedAssembly = BuildLibrary(root.FullName, "SharedAuth", "2.0.0");
            var installedAssembly = BuildLibrary(root.FullName, "SharedAuth", "1.0.0", projectFolderName: "SharedAuth_Other");

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var libCore = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Lib", "Core"));
            File.Copy(stagedAssembly, Path.Combine(libCore.FullName, "SharedAuth.dll"), overwrite: true);

            var moduleSearchRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "PSModules"));
            var installedModuleDir = Directory.CreateDirectory(Path.Combine(moduleSearchRoot.FullName, "OtherModule", "1.0.0", "bin"));
            File.Copy(installedAssembly, Path.Combine(installedModuleDir.FullName, "SharedAuth.dll"), overwrite: true);

            var result = new BinaryConflictDetectionService(new NullLogger()).Analyze(
                moduleRoot.FullName,
                "Core",
                currentModuleName: "TestModule",
                searchRoots: new[] { moduleSearchRoot.FullName });

            Assert.True(result.HasConflicts);
            var issue = Assert.Single(result.Issues);
            Assert.Equal("SharedAuth", issue.AssemblyName);
            Assert.Equal("2.0.0.0", issue.PayloadAssemblyVersion);
            Assert.Equal("OtherModule", issue.InstalledModuleName);
            Assert.Equal("1.0.0", issue.InstalledModuleVersion);
            Assert.Equal("1.0.0.0", issue.InstalledAssemblyVersion);
            Assert.True(issue.VersionComparison > 0);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_IgnoresCurrentModuleCopies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var stagedAssembly = BuildLibrary(root.FullName, "SharedAuth", "2.0.0");
            var installedAssembly = BuildLibrary(root.FullName, "SharedAuth", "1.0.0", projectFolderName: "SharedAuth_SameModule");

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var libCore = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Lib", "Core"));
            File.Copy(stagedAssembly, Path.Combine(libCore.FullName, "SharedAuth.dll"), overwrite: true);

            var moduleSearchRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "PSModules"));
            var installedModuleDir = Directory.CreateDirectory(Path.Combine(moduleSearchRoot.FullName, "TestModule", "0.9.0", "bin"));
            File.Copy(installedAssembly, Path.Combine(installedModuleDir.FullName, "SharedAuth.dll"), overwrite: true);

            var result = new BinaryConflictDetectionService(new NullLogger()).Analyze(
                moduleRoot.FullName,
                "Core",
                currentModuleName: "TestModule",
                searchRoots: new[] { moduleSearchRoot.FullName });

            Assert.False(result.HasConflicts);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_IgnoresMatchingAssemblyVersions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var stagedAssembly = BuildLibrary(root.FullName, "SharedAuth", "2.0.0");
            var installedAssembly = BuildLibrary(root.FullName, "SharedAuth", "2.0.0", projectFolderName: "SharedAuth_Matching");

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var libCore = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Lib", "Core"));
            File.Copy(stagedAssembly, Path.Combine(libCore.FullName, "SharedAuth.dll"), overwrite: true);

            var moduleSearchRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "PSModules"));
            var installedModuleDir = Directory.CreateDirectory(Path.Combine(moduleSearchRoot.FullName, "OtherModule", "1.0.0", "bin"));
            File.Copy(installedAssembly, Path.Combine(installedModuleDir.FullName, "SharedAuth.dll"), overwrite: true);

            var result = new BinaryConflictDetectionService(new NullLogger()).Analyze(
                moduleRoot.FullName,
                "Core",
                currentModuleName: "TestModule",
                searchRoots: new[] { moduleSearchRoot.FullName });

            Assert.False(result.HasConflicts);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static string BuildLibrary(string rootPath, string assemblyName, string version, string? projectFolderName = null)
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(rootPath, projectFolderName ?? (assemblyName + "_" + version.Replace('.', '_'))));
        var projectPath = Path.Combine(projectRoot.FullName, assemblyName + ".csproj");
        var sourcePath = Path.Combine(projectRoot.FullName, "Class1.cs");

        File.WriteAllText(projectPath, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{{assemblyName}}</AssemblyName>
    <Version>{{version}}</Version>
    <AssemblyVersion>{{version}}.0</AssemblyVersion>
    <FileVersion>{{version}}.0</FileVersion>
  </PropertyGroup>
</Project>
""");

        File.WriteAllText(sourcePath, $$"""
namespace {{assemblyName}}Lib;

public sealed class Marker
{
}
""");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectRoot.FullName
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"dotnet build failed for test fixture.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        var assemblyPath = Path.Combine(projectRoot.FullName, "bin", "Release", "net8.0", assemblyName + ".dll");
        Assert.True(File.Exists(assemblyPath), $"Built assembly not found: {assemblyPath}");
        return assemblyPath;
    }
}
