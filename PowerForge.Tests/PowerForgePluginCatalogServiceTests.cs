using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Tests;

public sealed class PowerForgePluginCatalogServiceTests
{
    [Fact]
    public void PlanFolderExport_SelectsGroupsAndResolvesPreferredFramework()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "SamplePlugin.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>
    <AssemblyName>Sample.Plugin</AssemblyName>
    <PackageId>Sample.Plugin.Package</PackageId>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgePluginCatalogService(new NullLogger());
            var plan = service.PlanFolderExport(
                new PowerForgePluginCatalogSpec
                {
                    ProjectRoot = ".",
                    Catalog = new[]
                    {
                        new PowerForgePluginCatalogEntry
                        {
                            Id = "sample",
                            ProjectPath = "SamplePlugin.csproj",
                            Groups = new[] { "public", "desktop" }
                        },
                        new PowerForgePluginCatalogEntry
                        {
                            Id = "private-only",
                            ProjectPath = "SamplePlugin.csproj",
                            Groups = new[] { "private" }
                        }
                    }
                },
                Path.Combine(root, "plugins.json"),
                new PowerForgePluginCatalogRequest
                {
                    Groups = new[] { "public" },
                    PreferredFramework = "net10.0-windows",
                    OutputRoot = "Artifacts/ExportedPlugins"
                });

            Assert.Equal(Path.Combine(root, "Artifacts", "ExportedPlugins"), plan.OutputRoot);
            Assert.Equal(new[] { "public" }, plan.SelectedGroups);

            var entry = Assert.Single(plan.Entries);
            Assert.Equal("sample", entry.Id);
            Assert.Equal("net10.0-windows", entry.Framework);
            Assert.Equal("Sample.Plugin", entry.AssemblyName);
            Assert.Equal("Sample.Plugin.Package", entry.PackageId);
            Assert.Equal(Path.Combine(root, "Artifacts", "ExportedPlugins", "Sample.Plugin.Package"), entry.OutputPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ExportFolders_WritesGenericManifestAndStripsSymbols()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "SamplePlugin.csproj");
            var sourcePath = Path.Combine(root, "Plugin.cs");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <AssemblyName>Sample.Plugin</AssemblyName>
    <PackageId>Sample.Plugin.Package</PackageId>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));
            File.WriteAllText(sourcePath, """
namespace Demo.Plugins;

public interface IPluginContract {}

public sealed class SamplePlugin : IPluginContract
{
}
""", new UTF8Encoding(false));

            var invoked = false;
            var service = new PowerForgePluginCatalogService(
                new NullLogger(),
                runProcess: psi =>
                {
                    invoked = true;
                    var outputPath = ExtractOptionValue(psi.Arguments, "-o")
                        ?? throw new InvalidOperationException("Publish output path was not provided.");

                    Directory.CreateDirectory(outputPath);
                    File.WriteAllText(Path.Combine(outputPath, "Sample.Plugin.dll"), "dll", new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputPath, "Sample.Plugin.pdb"), "pdb", new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputPath, "dependency.json"), "{}", new UTF8Encoding(false));
                    return new PluginCatalogProcessResult(0, "ok", string.Empty);
                });

            var plan = service.PlanFolderExport(
                new PowerForgePluginCatalogSpec
                {
                    ProjectRoot = ".",
                    Catalog = new[]
                    {
                        new PowerForgePluginCatalogEntry
                        {
                            Id = "sample",
                            ProjectPath = "SamplePlugin.csproj",
                            Groups = new[] { "public" },
                            Manifest = new PowerForgePluginManifestOptions
                            {
                                FileName = "plugin.manifest.json",
                                EntryTypeMatchBaseType = "IPluginContract",
                                Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["displayName"] = "{packageId}",
                                    ["project"] = "{projectPath}"
                                }
                            }
                        }
                    }
                },
                Path.Combine(root, "plugins.json"),
                new PowerForgePluginCatalogRequest
                {
                    Groups = new[] { "public" }
                });

            var result = service.ExportFolders(plan);

            Assert.True(invoked);
            Assert.True(result.Success);
            var entry = Assert.Single(result.Entries);
            Assert.NotNull(entry.ManifestPath);
            Assert.False(File.Exists(Path.Combine(entry.OutputPath, "Sample.Plugin.pdb")));

            using var document = JsonDocument.Parse(File.ReadAllText(entry.ManifestPath!, Encoding.UTF8));
            var rootElement = document.RootElement;
            Assert.Equal(1, rootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("sample", rootElement.GetProperty("id").GetString());
            Assert.Equal("Sample.Plugin.Package", rootElement.GetProperty("packageId").GetString());
            Assert.Equal("Sample.Plugin.dll", rootElement.GetProperty("entryAssembly").GetString());
            Assert.Equal("Demo.Plugins.SamplePlugin", rootElement.GetProperty("entryType").GetString());
            Assert.Equal("Sample.Plugin.Package", rootElement.GetProperty("displayName").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveEntryType_ThrowsWhenMultipleMatchesExist()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "SamplePlugin.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "One.cs"), """
namespace Demo;
public sealed class One : IPluginContract {}
""", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "Two.cs"), """
namespace Demo;
public sealed class Two : IPluginContract {}
""", new UTF8Encoding(false));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                PowerForgePluginCatalogService.ResolveEntryType(projectPath, "IPluginContract"));

            Assert.Contains("Expected exactly one type assignable", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PlanPackages_SelectsPackGroupsAndAppliesVersionOverrides()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "SamplePlugin.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Sample.Plugin</AssemblyName>
    <PackageId>Sample.Plugin.Package</PackageId>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgePluginCatalogService(new NullLogger());
            var plan = service.PlanPackages(
                new PowerForgePluginCatalogSpec
                {
                    ProjectRoot = ".",
                    Catalog = new[]
                    {
                        new PowerForgePluginCatalogEntry
                        {
                            Id = "sample",
                            ProjectPath = "SamplePlugin.csproj",
                            Groups = new[] { "pack-public", "plugin-public" }
                        },
                        new PowerForgePluginCatalogEntry
                        {
                            Id = "private-only",
                            ProjectPath = "SamplePlugin.csproj",
                            Groups = new[] { "pack-private" }
                        }
                    }
                },
                Path.Combine(root, "plugins.json"),
                new PowerForgePluginPackageRequest
                {
                    Groups = new[] { "pack-public" },
                    OutputRoot = "Artifacts/NuGet",
                    PackageVersion = "1.2.3"
                });

            Assert.Equal(Path.Combine(root, "Artifacts", "NuGet"), plan.OutputRoot);
            Assert.Equal(new[] { "pack-public" }, plan.SelectedGroups);
            Assert.Equal("1.2.3", plan.PackageVersion);
            Assert.Null(plan.VersionSuffix);

            var entry = Assert.Single(plan.Entries);
            Assert.Equal("sample", entry.Id);
            Assert.Equal("Sample.Plugin.Package", entry.PackageId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PackPackages_CollectsOutputsAndPushesProducedPackages()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "SamplePlugin.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Sample.Plugin</AssemblyName>
    <PackageId>Sample.Plugin.Package</PackageId>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var pushedPackages = new List<string>();
            var service = new PowerForgePluginCatalogService(
                new NullLogger(),
                runProcess: psi =>
                {
                    var outputPath = ExtractOptionValue(psi.Arguments, "-o")
                        ?? throw new InvalidOperationException("Pack output path was not provided.");

                    Directory.CreateDirectory(outputPath);
                    File.WriteAllText(Path.Combine(outputPath, "Sample.Plugin.Package.1.2.3.nupkg"), "pkg", new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputPath, "Sample.Plugin.Package.1.2.3.snupkg"), "sym", new UTF8Encoding(false));
                    return new PluginCatalogProcessResult(0, "ok", string.Empty);
                },
                pushPackage: request =>
                {
                    pushedPackages.Add(request.PackagePath);
                    return new DotNetNuGetPushResult(0, "ok", string.Empty, "dotnet", TimeSpan.FromMilliseconds(1), timedOut: false, errorMessage: null);
                });

            var plan = service.PlanPackages(
                new PowerForgePluginCatalogSpec
                {
                    ProjectRoot = ".",
                    Catalog = new[]
                    {
                        new PowerForgePluginCatalogEntry
                        {
                            Id = "sample",
                            ProjectPath = "SamplePlugin.csproj",
                            Groups = new[] { "pack-public" }
                        }
                    }
                },
                Path.Combine(root, "plugins.json"),
                new PowerForgePluginPackageRequest
                {
                    Groups = new[] { "pack-public" },
                    OutputRoot = "Artifacts/NuGet",
                    PackageVersion = "1.2.3",
                    IncludeSymbols = true,
                    PushPackages = true,
                    PushSource = "https://example.invalid/v3/index.json",
                    ApiKey = "secret"
                });

            var result = service.PackPackages(plan, apiKey: "secret");

            Assert.True(result.Success);
            var entry = Assert.Single(result.Entries);
            Assert.Single(entry.PackagePaths);
            Assert.Single(entry.SymbolPackagePaths);
            Assert.Single(entry.PushResults);
            Assert.Single(pushedPackages);
            Assert.EndsWith(".nupkg", entry.PackagePaths[0], StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".snupkg", entry.SymbolPackagePaths[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string? ExtractOptionValue(string arguments, string optionName)
    {
        var pattern = Regex.Escape(optionName) + "\\s+\"([^\"]+)\"";
        var quotedMatch = Regex.Match(arguments, pattern);
        if (quotedMatch.Success)
            return quotedMatch.Groups[1].Value;

        var plainPattern = Regex.Escape(optionName) + "\\s+([^\\s]+)";
        var plainMatch = Regex.Match(arguments, plainPattern);
        return plainMatch.Success ? plainMatch.Groups[1].Value : null;
    }

    private static string CreateSandbox()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.PluginCatalogTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }
}
