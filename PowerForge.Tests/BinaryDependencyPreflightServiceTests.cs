using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class BinaryDependencyPreflightServiceTests
{
    [Fact]
    public void Analyze_FindsMissingProjectDependency()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var paths = CreateDependencyFixture(root.FullName);
            BuildProject(paths.ConsumerProjectPath);

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var libCore = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Lib", "Core"));
            File.Copy(paths.ConsumerAssemblyPath, Path.Combine(libCore.FullName, "Consumer.dll"), overwrite: true);

            var result = new BinaryDependencyPreflightService(new NullLogger()).Analyze(moduleRoot.FullName, "Core");

            Assert.True(result.HasIssues);
            Assert.Contains(result.Issues, i => string.Equals(i.MissingDependencyName, "Dependency", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_DoesNotReportIssue_WhenDependencyIsPresent()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var paths = CreateDependencyFixture(root.FullName);
            BuildProject(paths.ConsumerProjectPath);

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var libCore = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Lib", "Core"));
            File.Copy(paths.ConsumerAssemblyPath, Path.Combine(libCore.FullName, "Consumer.dll"), overwrite: true);
            File.Copy(paths.DependencyAssemblyPath, Path.Combine(libCore.FullName, "Dependency.dll"), overwrite: true);

            var result = new BinaryDependencyPreflightService(new NullLogger()).Analyze(moduleRoot.FullName, "Core");

            Assert.False(result.HasIssues);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_WithManifestScopedScriptModule_IgnoresDeliveryInternalsDlls()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var paths = CreateDependencyFixture(root.FullName);
            BuildProject(paths.ConsumerProjectPath);

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "TestModule.psm1"), "# script module");
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "TestModule.psd1"), """
@{
    ModuleVersion = '1.0.0'
    RootModule = 'TestModule.psm1'
    PrivateData = @{
        PSData = @{
            Delivery = @{
                Enable = $true
                InternalsPath = 'Artefacts'
            }
        }
    }
}
""");

            var artefactsRoot = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Artefacts"));
            File.Copy(paths.ConsumerAssemblyPath, Path.Combine(artefactsRoot.FullName, "Consumer.dll"), overwrite: true);

            var result = new BinaryDependencyPreflightService(new NullLogger()).Analyze(
                moduleRoot.FullName,
                "Core",
                Path.Combine(moduleRoot.FullName, "TestModule.psd1"));

            Assert.False(result.HasIssues);
            Assert.Equal("no declared binary assemblies", result.Summary);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_WithManifestScopedRootBinary_FindsMissingDependency()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var paths = CreateDependencyFixture(root.FullName);
            BuildProject(paths.ConsumerProjectPath);

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "TestModule.psm1"), "# script module");
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "TestModule.psd1"), """
@{
    ModuleVersion = '1.0.0'
    RootModule = 'TestModule.psm1'
    NestedModules = @('Consumer.dll')
}
""");
            File.Copy(paths.ConsumerAssemblyPath, Path.Combine(moduleRoot.FullName, "Consumer.dll"), overwrite: true);

            var result = new BinaryDependencyPreflightService(new NullLogger()).Analyze(
                moduleRoot.FullName,
                "Core",
                Path.Combine(moduleRoot.FullName, "TestModule.psd1"));

            Assert.True(result.HasIssues);
            Assert.Contains(result.Issues, i => string.Equals(i.MissingDependencyName, "Dependency", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_WithManifestScopedRootBinary_FindsTransitiveMissingDependency()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var paths = CreateTransitiveDependencyFixture(root.FullName);
            BuildProject(paths.ConsumerProjectPath);

            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "TestModule.psm1"), "# script module");
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "TestModule.psd1"), """
@{
    ModuleVersion = '1.0.0'
    RootModule = 'TestModule.psm1'
    NestedModules = @('Consumer.dll')
}
""");
            File.Copy(paths.ConsumerAssemblyPath, Path.Combine(moduleRoot.FullName, "Consumer.dll"), overwrite: true);
            File.Copy(paths.DependencyAssemblyPath, Path.Combine(moduleRoot.FullName, "Dependency.dll"), overwrite: true);

            var result = new BinaryDependencyPreflightService(new NullLogger()).Analyze(
                moduleRoot.FullName,
                "Core",
                Path.Combine(moduleRoot.FullName, "TestModule.psd1"));

            Assert.True(result.HasIssues);
            Assert.Contains(result.Issues, i => string.Equals(i.MissingDependencyName, "Leaf", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildFailureMessage_IncludesClearHint()
    {
        var result = new BinaryDependencyPreflightResult(
            powerShellEdition: "Desktop",
            moduleRoot: @"C:\Temp\TestModule",
            assemblyRootPath: @"C:\Temp\TestModule\Lib\Default",
            assemblyRootRelativePath: @"Lib\Default",
            issues: new[]
            {
                new BinaryDependencyPreflightIssue("MimeKit.dll", "System.Data.SQLite", "1.0.119.0")
            },
            summary: "1 missing dependency");

        var message = BinaryDependencyPreflightService.BuildFailureMessage(
            result,
            modulePath: @"C:\Temp\TestModule\TestModule.psd1",
            validationTarget: "Windows PowerShell/Desktop");

        Assert.Contains("Binary dependency preflight failed during Windows PowerShell/Desktop validation.", message);
        Assert.Contains("Cause: MimeKit.dll references missing System.Data.SQLite.dll.", message);
        Assert.Contains(@"Payload: Lib\Default", message);
        Assert.Contains("SkipBinaryDependencyCheck", message);
    }

    [Fact]
    public void DesktopHostBaseline_DoesNotTreatCoreRuntimeAssembliesAsBuiltIn()
    {
        var method = typeof(BinaryDependencyPreflightService).GetMethod(
            "GetHostProvidedAssemblyNames",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var hostAssemblies = (System.Collections.Generic.IReadOnlyCollection<string>)method!.Invoke(null, new object[] { "Desktop" })!;
        Assert.Contains(hostAssemblies, name => string.Equals(name, "System.Management.Automation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(hostAssemblies, name => string.Equals(name, "System.Private.CoreLib", StringComparison.OrdinalIgnoreCase));
    }

    private static DependencyFixture CreateDependencyFixture(string rootPath)
    {
        var dependencyRoot = Directory.CreateDirectory(Path.Combine(rootPath, "Dependency"));
        var consumerRoot = Directory.CreateDirectory(Path.Combine(rootPath, "Consumer"));

        File.WriteAllText(Path.Combine(dependencyRoot.FullName, "Dependency.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(dependencyRoot.FullName, "Class1.cs"), """
namespace DependencyLib;

public sealed class DependencyMarker
{
}
""");

        File.WriteAllText(Path.Combine(consumerRoot.FullName, "Consumer.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Dependency\Dependency.csproj" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(consumerRoot.FullName, "Class1.cs"), """
using DependencyLib;

namespace ConsumerLib;

public sealed class ConsumerMarker
{
    public DependencyMarker Dependency { get; } = new();
}
""");

        return new DependencyFixture(
            Path.Combine(consumerRoot.FullName, "Consumer.csproj"),
            Path.Combine(consumerRoot.FullName, "bin", "Release", "net8.0", "Consumer.dll"),
            Path.Combine(dependencyRoot.FullName, "bin", "Release", "net8.0", "Dependency.dll"));
    }

    private static DependencyFixture CreateTransitiveDependencyFixture(string rootPath)
    {
        var leafRoot = Directory.CreateDirectory(Path.Combine(rootPath, "Leaf"));
        var dependencyRoot = Directory.CreateDirectory(Path.Combine(rootPath, "Dependency"));
        var consumerRoot = Directory.CreateDirectory(Path.Combine(rootPath, "Consumer"));

        File.WriteAllText(Path.Combine(leafRoot.FullName, "Leaf.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(leafRoot.FullName, "Class1.cs"), """
namespace LeafLib;

public sealed class LeafMarker
{
}
""");

        File.WriteAllText(Path.Combine(dependencyRoot.FullName, "Dependency.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Leaf\Leaf.csproj" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(dependencyRoot.FullName, "Class1.cs"), """
using LeafLib;

namespace DependencyLib;

public sealed class DependencyMarker
{
    public LeafMarker Leaf { get; } = new();
}
""");

        File.WriteAllText(Path.Combine(consumerRoot.FullName, "Consumer.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Dependency\Dependency.csproj" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(consumerRoot.FullName, "Class1.cs"), """
using DependencyLib;

namespace ConsumerLib;

public sealed class ConsumerMarker
{
    public DependencyMarker Dependency { get; } = new();
}
""");

        return new DependencyFixture(
            Path.Combine(consumerRoot.FullName, "Consumer.csproj"),
            Path.Combine(consumerRoot.FullName, "bin", "Release", "net8.0", "Consumer.dll"),
            Path.Combine(dependencyRoot.FullName, "bin", "Release", "net8.0", "Dependency.dll"));
    }

    private static void BuildProject(string projectPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"dotnet build failed for test fixture.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }

    private sealed class DependencyFixture
    {
        public string ConsumerProjectPath { get; }
        public string ConsumerAssemblyPath { get; }
        public string DependencyAssemblyPath { get; }

        public DependencyFixture(string consumerProjectPath, string consumerAssemblyPath, string dependencyAssemblyPath)
        {
            ConsumerProjectPath = consumerProjectPath;
            ConsumerAssemblyPath = consumerAssemblyPath;
            DependencyAssemblyPath = dependencyAssemblyPath;
        }
    }
}
