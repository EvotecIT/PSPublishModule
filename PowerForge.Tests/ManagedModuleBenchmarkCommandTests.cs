using System.Management.Automation;
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

    private static void WritePublishModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, moduleName + ".psm1"), string.Empty);
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
                "    FunctionsToExport = @()",
                "    CmdletsToExport = @()",
                "    AliasesToExport = @()",
                "}"
            }));
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
