using System.IO.Compression;
using System.Management.Automation;
using System.Xml.Linq;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkCommandTests
{
    [Fact]
    public void MeasureManagedModule_ReturnsBenchmarkResultForInstall()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var reportRoot = new TemporaryDirectory();
        var jsonPath = Path.Combine(reportRoot.Path, "benchmark.json");
        var markdownPath = Path.Combine(reportRoot.Path, "benchmark.md");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Measure-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Operation", ManagedModuleBenchmarkOperation.Install)
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("Version", "1.0.0")
            .AddParameter("ReportPath", jsonPath)
            .AddParameter("MarkdownReportPath", markdownPath);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleBenchmarkResult>(Assert.Single(results).BaseObject);
        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal("Install:Company.Tools", run.ScenarioId);
        Assert.Equal("Installed", run.Status);
        Assert.Equal("1.0.0", run.Version);
        Assert.True(run.PackageBytes > 0);
        Assert.True(run.ExtractionElapsed.GetValueOrDefault() > TimeSpan.Zero);
        Assert.Equal(0, run.RepositoryRequestCount);
        Assert.Equal("1.0.0", run.ValidatedVersion);
        Assert.True(run.VersionValidationSucceeded);
        Assert.True(run.FinalDiskBytes > 0);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
        Assert.Contains("\"ScenarioId\": \"Install:Company.Tools\"", File.ReadAllText(jsonPath), StringComparison.Ordinal);
        Assert.Contains("\"FinalDiskBytes\"", File.ReadAllText(jsonPath), StringComparison.Ordinal);
        Assert.Contains("\"RepositoryRequestCount\"", File.ReadAllText(jsonPath), StringComparison.Ordinal);
        Assert.Contains("# Managed Module Benchmark Report", File.ReadAllText(markdownPath), StringComparison.Ordinal);
        Assert.Contains("Disk bytes", File.ReadAllText(markdownPath), StringComparison.Ordinal);
        Assert.Contains("Import check", File.ReadAllText(markdownPath), StringComparison.Ordinal);
        Assert.Contains("Requests", File.ReadAllText(markdownPath), StringComparison.Ordinal);
        Assert.Contains("Install:Company.Tools", File.ReadAllText(markdownPath), StringComparison.Ordinal);
    }

    [Fact]
    public void MeasureManagedModule_exposes_engine_selector()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument("Measure-ManagedModule");

        var command = Assert.IsAssignableFrom<CommandInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("Engine"));
        Assert.True(command.Parameters.ContainsKey("ValidateImport"));
        Assert.True(command.Parameters.ContainsKey("ImportHost"));
        Assert.True(command.Parameters.ContainsKey("ModulePath"));
        Assert.True(command.Parameters.ContainsKey("PackageOutputDirectory"));
    }

    [Fact]
    public void MeasureManagedModule_ReturnsBenchmarkResultForPublish()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var packageOutput = new TemporaryDirectory();
        WritePublishModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Measure-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Operation", ManagedModuleBenchmarkOperation.Publish)
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ModulePath", moduleRoot.Path)
            .AddParameter("PackageOutputDirectory", packageOutput.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleBenchmarkResult>(Assert.Single(results).BaseObject);
        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(ManagedModuleBenchmarkOperation.Publish, run.Operation);
        Assert.Equal("Published", run.Status);
        Assert.True(run.Published);
        Assert.Equal("1.0.0", run.Version);
        Assert.True(File.Exists(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg")));
    }

    [Fact]
    public void MeasureManagedModule_records_transitive_dependency_graph_evidence()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateGraphPackages(feed.Path);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Measure-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Operation", ManagedModuleBenchmarkOperation.Install)
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("Version", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleBenchmarkResult>(Assert.Single(results).BaseObject);
        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal("Installed", run.Status);
        Assert.Equal(6, run.DependencyCount);
        Assert.Equal(7, run.PackageCount);
        Assert.True(run.TotalPackageBytes > run.PackageBytes);
        Assert.True(run.TotalFileCount > run.FileCount);
        Assert.True(run.TotalExtractedBytes > run.ExtractedBytes);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Leaf3", "1.0.0", "Company.Leaf3.psd1")));
    }

    [Fact]
    public void MeasureManagedModule_publishes_module_with_dependency_metadata()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var packageOutput = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.2.0.0.nupkg"),
            "Company.Core",
            "2.0.0",
            files: CreateNamedModuleFiles("Company.Core", "2.0.0"));
        WritePublishModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '2.0.0' })");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Measure-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Operation", ManagedModuleBenchmarkOperation.Publish)
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ModulePath", moduleRoot.Path)
            .AddParameter("PackageOutputDirectory", packageOutput.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleBenchmarkResult>(Assert.Single(results).BaseObject);
        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.True(run.Published);
        Assert.Equal("Published", run.Status);
        Assert.True(File.Exists(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg")));
        AssertPackageDependency(run.PackagePath!, "Company.Core", "[2.0.0]");
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(InstallManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static IReadOnlyDictionary<string, string> CreateNamedModuleFiles(string name, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [name + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static void CreateGraphPackages(string feedPath)
    {
        TestPackageFactory.Create(
            Path.Combine(feedPath, "Company.Leaf1.1.0.0.nupkg"),
            "Company.Leaf1",
            "1.0.0",
            files: CreateNamedModuleFiles("Company.Leaf1", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feedPath, "Company.Leaf2.1.0.0.nupkg"),
            "Company.Leaf2",
            "1.0.0",
            files: CreateNamedModuleFiles("Company.Leaf2", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feedPath, "Company.Leaf3.1.0.0.nupkg"),
            "Company.Leaf3",
            "1.0.0",
            files: CreateNamedModuleFiles("Company.Leaf3", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feedPath, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            dependencies: new[]
            {
                new TestDependency("Company.Leaf1", "[1.0.0]", null),
                new TestDependency("Company.Leaf2", "[1.0.0]", null)
            },
            files: CreateNamedModuleFiles("Company.Core", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feedPath, "Company.Auth.1.0.0.nupkg"),
            "Company.Auth",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Leaf3", "[1.0.0]", null) },
            files: CreateNamedModuleFiles("Company.Auth", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feedPath, "Company.Users.1.0.0.nupkg"),
            "Company.Users",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateNamedModuleFiles("Company.Users", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feedPath, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[]
            {
                new TestDependency("Company.Auth", "[1.0.0]", null),
                new TestDependency("Company.Users", "[1.0.0]", null)
            },
            files: CreateModuleFiles("1.0.0"));
    }

    private static void WritePublishModule(
        string moduleRoot,
        string moduleName,
        string version,
        string? requiredModules = null)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, moduleName + ".psm1"), string.Empty);
        var requiredModulesLine = string.IsNullOrWhiteSpace(requiredModules)
            ? string.Empty
            : requiredModules + Environment.NewLine;
        File.WriteAllText(
            Path.Combine(moduleRoot, moduleName + ".psd1"),
            string.Join(Environment.NewLine, new[]
            {
                "@{",
                $"    RootModule = '{moduleName}.psm1'",
                $"    ModuleVersion = '{version}'",
                "    GUID = '11111111-1111-1111-1111-111111111111'",
                "    Author = 'Evotec'",
                "    Description = 'Benchmark publish module.'",
                requiredModulesLine.TrimEnd(),
                "    FunctionsToExport = @()",
                "    CmdletsToExport = @()",
                "    AliasesToExport = @()",
                "}"
            }.Where(static line => !string.IsNullOrWhiteSpace(line))));
    }

    private static void AssertPackageDependency(string packagePath, string id, string version)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        var dependencies = document.Descendants()
            .Where(static element => element.Name.LocalName == "dependency")
            .Select(static element => new
            {
                Id = element.Attribute("id")?.Value,
                Version = element.Attribute("version")?.Value
            });

        Assert.Contains(dependencies, dependency =>
            string.Equals(dependency.Id, id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(dependency.Version, version, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
