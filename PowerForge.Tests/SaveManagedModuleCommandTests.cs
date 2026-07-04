using System.Text.Json;
using System.IO.Compression;
using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class SaveManagedModuleCommandTests
{
    [Fact]
    public void SaveManagedModule_saves_dependency_closure_to_destination()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Core", "1.0.0", "Company.Core.psd1")));
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Fact]
    public void SaveManagedModule_skip_dependency_check_saves_only_requested_module()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("SkipDependencyCheck");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Empty(result.DependencyResults);
        Assert.False(Directory.Exists(Path.Combine(destination.Path, "Company.Core")));
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_saves_dependency_closure_to_destination()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        var dependency = Assert.Single(result.DependencyResults);
        Assert.True(result.SavedAsNupkg);
        Assert.True(dependency.SavedAsNupkg);
        Assert.Equal(Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"), result.ModulePath);
        Assert.Equal(result.ModulePath, result.Download?.PackagePath);
        Assert.Null(result.ReceiptPath);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg")));
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Core.1.0.0.nupkg")));
        Assert.False(Directory.Exists(Path.Combine(destination.Path, "Company.Tools")));
        Assert.False(Directory.Exists(Path.Combine(destination.Path, "Company.Core")));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_does_not_treat_unpacked_dependency_as_saved_package()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));
        var unpackedDependencyPath = Path.Combine(destination.Path, "Company.Core", "1.0.0");
        Directory.CreateDirectory(unpackedDependencyPath);
        File.WriteAllText(Path.Combine(unpackedDependencyPath, "Company.Core.psd1"), "@{ ModuleVersion = '1.0.0' }");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        var dependency = Assert.Single(result.DependencyResults);
        var dependencyPackagePath = Path.Combine(destination.Path, "Company.Core.1.0.0.nupkg");
        Assert.True(dependency.SavedAsNupkg);
        Assert.Equal(dependencyPackagePath, dependency.ModulePath);
        Assert.Equal(dependencyPackagePath, dependency.Download?.PackagePath);
        Assert.True(File.Exists(dependencyPackagePath));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_reports_package_paths_for_shared_transitive_dependencies()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.FeatureA.1.0.0.nupkg"),
            "Company.FeatureA",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateFeatureFiles("Company.FeatureA", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.FeatureB.1.0.0.nupkg"),
            "Company.FeatureB",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateFeatureFiles("Company.FeatureB", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[]
            {
                new TestDependency("Company.FeatureA", "[1.0.0]", null),
                new TestDependency("Company.FeatureB", "[1.0.0]", null)
            },
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("DependencyConcurrency", 2);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        var flattened = Flatten(result).ToArray();
        var coreResults = flattened.Where(item => item.Name == "Company.Core").ToArray();
        Assert.NotEmpty(coreResults);
        Assert.All(coreResults, item =>
        {
            Assert.True(item.SavedAsNupkg);
            Assert.Equal(Path.Combine(destination.Path, "Company.Core.1.0.0.nupkg"), item.ModulePath);
            Assert.Equal(item.ModulePath, item.Download?.PackagePath);
        });
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_recurses_existing_dependency_package_metadata()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Feature.1.0.0.nupkg"),
            "Company.Feature",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateFeatureFiles("Company.Feature", "1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Feature", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));
        File.Copy(
            Path.Combine(feed.Path, "Company.Feature.1.0.0.nupkg"),
            Path.Combine(destination.Path, "Company.Feature.1.0.0.nupkg"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        var flattened = Flatten(result).ToArray();
        Assert.Contains(flattened, item => item.Name == "Company.Feature" && item.SavedAsNupkg);
        Assert.Contains(flattened, item => item.Name == "Company.Core" && item.SavedAsNupkg);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg")));
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Feature.1.0.0.nupkg")));
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Core.1.0.0.nupkg")));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_matches_existing_dependency_package_id_case_insensitively()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "company.core.1.0.0.nupkg"),
            "company.core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        var dependency = Assert.Single(result.DependencyResults);
        var dependencyPackagePath = Path.Combine(destination.Path, "company.core.1.0.0.nupkg");
        Assert.Equal("Company.Core", dependency.Name);
        Assert.True(File.Exists(dependency.ModulePath));
        Assert.True(File.Exists(dependency.Download?.PackagePath));
        if (Path.DirectorySeparatorChar != '\\')
        {
            Assert.Equal(dependencyPackagePath, dependency.ModulePath);
            Assert.Equal(dependencyPackagePath, dependency.Download?.PackagePath);
        }

        Assert.True(File.Exists(dependencyPackagePath));
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg")));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_skip_dependency_check_saves_only_requested_package()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("SkipDependencyCheck");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.True(result.SavedAsNupkg);
        Assert.Empty(result.DependencyResults);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg")));
        Assert.False(File.Exists(Path.Combine(destination.Path, "Company.Core.1.0.0.nupkg")));
        Assert.False(Directory.Exists(Path.Combine(destination.Path, "Company.Tools")));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_detects_existing_package()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));
        File.Copy(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.SaveAsNupkg);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.Equal(Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"), plan.ModulePath);
        Assert.False(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_detects_existing_package_case_insensitively()
    {
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "company.tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(destination.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.Equal(packagePath, plan.ModulePath);
        Assert.False(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_reuses_case_matched_package_after_latest_resolution()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.2.0.0.nupkg"),
            "Company.Tools",
            "2.0.0",
            files: CreateToolFiles("2.0.0"));
        var matchedPackagePath = Path.Combine(destination.Path, "company.tools.2.0.0.nupkg");
        TestPackageFactory.Create(
            matchedPackagePath,
            "Company.Tools",
            "2.0.0",
            files: CreateToolFiles("2.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("AsNupkg");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(matchedPackagePath, result.ModulePath);
        Assert.Single(Directory.EnumerateFiles(destination.Path, "*.nupkg", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_detects_existing_package_for_version_range()
    {
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "Company.Tools.1.2.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.2.0",
            files: CreateToolFiles("1.2.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(destination.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("MinimumVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal("1.2.0", plan.Version);
        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.Equal(packagePath, plan.ModulePath);
        Assert.False(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_resolves_latest_when_existing_package_is_older()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.2.0.0.nupkg"),
            "Company.Tools",
            "2.0.0",
            files: CreateToolFiles("2.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("AsNupkg")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.False(plan.ExistingVersionFound);
        Assert.Equal("2.0.0", plan.Version);
        Assert.Equal(ManagedModuleInstallPlanAction.Install, plan.Action);
        Assert.Equal(Path.Combine(destination.Path, "Company.Tools.2.0.0.nupkg"), plan.ModulePath);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_does_not_treat_longer_package_id_as_existing_version()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.2024.1.0.0.nupkg"),
            "Company.2024",
            "1.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.1.0.0.nupkg"),
            "Company",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("AsNupkg");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal("Company", result.Name);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal(Path.Combine(destination.Path, "Company.1.0.0.nupkg"), result.ModulePath);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.2024.1.0.0.nupkg")));
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.1.0.0.nupkg")));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_reports_license_required_for_existing_package()
    {
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"),
            requireLicenseAcceptance: true);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(destination.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.ExistingVersionFound);
        Assert.True(plan.LicenseAcceptanceRequired);
        Assert.False(plan.LicenseAccepted);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_marks_existing_package_with_missing_dependencies_as_writing()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(feed.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.SaveAsNupkg);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_force_plan_does_not_read_corrupt_existing_package()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllText(packagePath, "not a package");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("Force")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.SaveAsNupkg);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(ManagedModuleInstallPlanAction.Reinstall, plan.Action);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_rejects_corrupt_existing_package_before_skip()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllText(packagePath, "not a package");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(ManagedModuleInstallPlanAction.Reinstall, plan.Action);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_replaces_corrupt_existing_package()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllText(packagePath, "not a package");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.True(result.SavedAsNupkg);
        Assert.Equal(packagePath, result.ModulePath);
        using var archive = ZipFile.OpenRead(packagePath);
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("Company.Tools.psd1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_does_not_reuse_prefix_package_as_dependency()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Core.Extensions.1.0.0.nupkg"),
            "Company.Core.Extensions",
            "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(feed.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_reuses_package_cache_when_exact_version_is_requested()
    {
        using var feed = new TemporaryDirectory();
        using var cache = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var repositoryCache = Path.Combine(cache.Path, CreateRepositoryCacheKey(feed.Path));
        TestPackageFactory.Create(
            Path.Combine(repositoryCache, "company.tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("PackageCacheDirectory", cache.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.True(result.Download?.FromCache);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void SaveManagedModule_accepts_expected_package_sha256()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));
        var expectedSha256 = TestHash.ComputeSha256(packagePath).ToUpperInvariant();

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("ExpectedPackageSha256", "sha256:" + expectedSha256);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(expectedSha256.ToLowerInvariant(), result.ExpectedPackageSha256);
        Assert.Equal(expectedSha256.ToLowerInvariant(), result.Download?.PackageSha256);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_reuses_existing_package_with_expected_sha256_offline()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));
        var expectedSha256 = TestHash.ComputeSha256(packagePath).ToUpperInvariant();

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(feed.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("ExpectedPackageSha256", "sha256:" + expectedSha256);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.True(result.SavedAsNupkg);
        Assert.Equal(packagePath, result.ModulePath);
        Assert.Equal(expectedSha256.ToLowerInvariant(), result.Download?.PackageSha256);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_reuses_existing_package_with_expected_sha256_offline()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));
        var expectedSha256 = TestHash.ComputeSha256(packagePath).ToUpperInvariant();

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(feed.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("ExpectedPackageSha256", "sha256:" + expectedSha256)
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.ExistingVersionFound);
        Assert.False(plan.WouldWriteFiles);
        Assert.Equal(ManagedModuleInstallPlanAction.SkipExisting, plan.Action);
        Assert.Equal(expectedSha256.ToLowerInvariant(), plan.ExpectedPackageSha256);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_reuses_existing_package_with_allowed_author_offline()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"),
            authors: "Evotec");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(feed.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("AllowedAuthor", new[] { "Evotec" });
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.True(result.SavedAsNupkg);
        Assert.Equal(packagePath, result.ModulePath);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_reuses_existing_dependency_with_allowed_author_offline()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Core.1.2.0.nupkg"),
            "Company.Core",
            "1.2.0",
            authors: "Evotec");
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0, )", null) },
            files: CreateToolFiles("1.0.0"),
            authors: "Evotec");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", Path.Combine(feed.Path, "Unavailable"))
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("AllowedAuthor", new[] { "Evotec" });
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal("1.2.0", dependency.Version);
        Assert.Equal(Path.Combine(destination.Path, "Company.Core.1.2.0.nupkg"), dependency.ModulePath);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_resolves_dependency_range_when_saved_dependency_fails_policy()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            authors: "Contoso");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.1.0.nupkg"),
            "Company.Core",
            "1.1.0",
            authors: "Evotec");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0, )", null) },
            files: CreateToolFiles("1.0.0"),
            authors: "Evotec");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("AllowedAuthor", new[] { "Evotec" });
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal("1.1.0", dependency.Version);
        Assert.Equal(Path.Combine(destination.Path, "Company.Core.1.1.0.nupkg"), dependency.ModulePath);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_refreshes_existing_package_when_author_policy_fails()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"),
            authors: "Contoso");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"),
            authors: "Evotec");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("AllowedAuthor", new[] { "Evotec" });
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.True(result.SavedAsNupkg);
        Assert.Equal(packagePath, result.ModulePath);
        Assert.Equal("Evotec", result.Download?.Metadata?.Authors);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_policy_refresh_reuses_matched_package_path()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var matchedPackagePath = Path.Combine(destination.Path, "company.tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            matchedPackagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"),
            authors: "Contoso");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"),
            authors: "Evotec");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("AllowedAuthor", new[] { "Evotec" });
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.True(result.SavedAsNupkg);
        Assert.Equal(matchedPackagePath, result.ModulePath);
        Assert.Equal("Evotec", result.Download?.Metadata?.Authors);
        Assert.Single(Directory.EnumerateFiles(destination.Path, "*.nupkg", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_detects_saved_dependency_author_policy_failure()
    {
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            authors: "Contoso");
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"),
            authors: "Evotec");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", destination.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("AllowedAuthor", new[] { "Evotec" })
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_plan_does_not_skip_same_path_local_feed_policy_failure()
    {
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"),
            authors: "Contoso");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", destination.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("AllowedAuthor", new[] { "Evotec" })
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.True(plan.ExistingVersionFound);
        Assert.Equal(ManagedModuleInstallPlanAction.Reinstall, plan.Action);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void SaveManagedModule_accept_license_saves_license_required_package()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"),
            requireLicenseAcceptance: true);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AcceptLicense");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void SaveManagedModule_plan_outputs_install_plan_without_writing_files()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateToolFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedModuleInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallPlanAction.Install, plan.Action);
        Assert.Equal("1.0.0", plan.Version);
        Assert.True(plan.WouldWriteFiles);
        Assert.False(Directory.Exists(Path.Combine(destination.Path, "Company.Tools")));
    }

    [Fact]
    public void SaveManagedModule_writes_offline_bundle_metadata()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));
        var metadataPath = Path.Combine(destination.Path, "bundle.metadata.json");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("BundleMetadataPath", metadataPath);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        Assert.Single(results);
        Assert.True(File.Exists(metadataPath));
        var metadata = JsonSerializer.Deserialize<ManagedModuleBundleMetadata>(File.ReadAllText(metadataPath));
        Assert.NotNull(metadata);
        Assert.Equal(destination.Path, metadata.ModuleRoot);
        Assert.Contains(metadata.Modules, entry => entry.Name == "Company.Tools" && entry.DependencyOf is null);
        Assert.Contains(metadata.Modules, entry => entry.Name == "Company.Core" && entry.DependencyOf == "Company.Tools");
        Assert.All(metadata.Modules, entry => Assert.Equal(64, entry.PackageSha256?.Length));
    }

    [Fact]
    public void SaveManagedModule_as_nupkg_writes_offline_bundle_metadata_with_package_root()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateCoreFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateToolFiles("1.0.0"));
        var metadataPath = Path.Combine(destination.Path, "bundle.metadata.json");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AsNupkg")
            .AddParameter("BundleMetadataPath", metadataPath);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        Assert.Single(results);
        var metadata = JsonSerializer.Deserialize<ManagedModuleBundleMetadata>(File.ReadAllText(metadataPath));
        Assert.NotNull(metadata);
        Assert.Equal(destination.Path, metadata.ModuleRoot);
        Assert.Contains(metadata.Modules, entry =>
            entry.Name == "Company.Tools" &&
            entry.ModulePath == Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg") &&
            entry.PackagePath == entry.ModulePath);
        Assert.Contains(metadata.Modules, entry =>
            entry.Name == "Company.Core" &&
            entry.DependencyOf == "Company.Tools" &&
            entry.ModulePath == Path.Combine(destination.Path, "Company.Core.1.0.0.nupkg") &&
            entry.PackagePath == entry.ModulePath);
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(SaveManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static IReadOnlyDictionary<string, string> CreateToolFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static IReadOnlyDictionary<string, string> CreateCoreFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Core.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static IReadOnlyDictionary<string, string> CreateFeatureFiles(string name, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [name + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static IEnumerable<ManagedModuleInstallResult> Flatten(ManagedModuleInstallResult result)
    {
        yield return result;
        foreach (var dependency in result.DependencyResults)
        {
            foreach (var child in Flatten(dependency))
                yield return child;
        }
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }

    private static string CreateRepositoryCacheKey(string source)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source.Trim()));
        return string.Concat(hash.Take(8).Select(static value => value.ToString("x2")));
    }
}
