using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallDependencyFanoutTests
{
    [Fact]
    public async Task InstallAsync_seeds_first_dependency_before_broad_parallel_fanout()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var featureNames = Enumerable.Range(1, 33)
            .Select(static index => "Company.Feature" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateModuleFiles("Company.Core", "1.0.0"));

        foreach (var featureName in featureNames)
        {
            TestPackageFactory.Create(
                Path.Combine(feed.Path, featureName + ".1.0.0.nupkg"),
                featureName,
                "1.0.0",
                dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
                files: CreateModuleFiles(featureName, "1.0.0"));
        }

        var rootDependencies = new[] { new TestDependency("Company.Core", "[1.0.0]", null) }
            .Concat(featureNames.Select(static featureName => new TestDependency(featureName, "[1.0.0]", null)))
            .ToArray();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Root.1.0.0.nupkg"),
            "Company.Root",
            "1.0.0",
            dependencies: rootDependencies,
            files: CreateModuleFiles("Company.Root", "1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Root",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal("Company.Core", result.DependencyResults[0].Name);
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.DependencyResults[0].Status);
        var nestedCoreResults = result.DependencyResults
            .Skip(1)
            .SelectMany(static dependency => dependency.DependencyResults)
            .Where(static dependency => dependency.Name == "Company.Core")
            .ToArray();
        Assert.Equal(featureNames.Length, nestedCoreResults.Length);
        Assert.All(nestedCoreResults, dependency =>
        {
            Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, dependency.Status);
            Assert.Equal(TimeSpan.Zero, dependency.CoalescedWaitElapsed);
        });
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string moduleName, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleName + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };
}
