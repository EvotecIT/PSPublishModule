using System.Collections.Generic;
using System.Linq;

namespace PowerForge.Tests;

public sealed class RequiredModuleResolutionEngineTests
{
    [Fact]
    public void ResolveRequiredModules_UsesOnlineMetadataForAutoValues_WhenInstalledInfoIsMissing()
    {
        var logger = new CollectingLogger();
        var engine = new RequiredModuleResolutionEngine(logger);
        var drafts = new[]
        {
            new RequiredModuleDraftDescriptor(
                moduleName: "PSWriteColor",
                moduleVersion: "Latest",
                minimumVersion: null,
                requiredVersion: null,
                guid: "Auto",
                versionSource: ModuleDependencyVersionSource.Auto)
        };

        var resolved = engine.ResolveRequiredModules(
            drafts,
            installed: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
            onlineLookup: _ => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSWriteColor"] = ("1.2.3", "11111111-1111-1111-1111-111111111111")
            },
            resolveMissingModulesOnline: true,
            warnIfRequiredModulesOutdated: false);

        var module = Assert.Single(resolved);
        Assert.Equal("PSWriteColor", module.ModuleName);
        Assert.Equal("1.2.3", module.ModuleVersion);
        Assert.Equal("11111111-1111-1111-1111-111111111111", module.Guid);
        Assert.Contains(logger.Infos, message => message.Contains("Resolved RequiredModules from repository without installing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveOutputRequiredModules_FiltersApprovedModules_WhenMergeMissingEnabled()
    {
        var modules = new[]
        {
            new RequiredModuleReference("PSWriteHTML", moduleVersion: "1.0.0"),
            new RequiredModuleReference("Graphimo", moduleVersion: "1.0.0")
        };

        var filtered = RequiredModuleResolutionEngine.ResolveOutputRequiredModules(
            modules,
            mergeMissing: true,
            fullyInlinedModules: new[] { "Graphimo" });

        Assert.Single(filtered);
        Assert.Equal("PSWriteHTML", filtered[0].ModuleName);
    }

    [Fact]
    public void ResolveRequiredModules_StripsSemVerMetadataFromManifestVersionFields()
    {
        const string installedVersion = "2.0.0-beta.1";
        var engine = new RequiredModuleResolutionEngine(new CollectingLogger());
        var resolved = engine.ResolveRequiredModules(
            new[]
            {
                new RequiredModuleDraftDescriptor(
                    "Preview.Tools",
                    moduleVersion: "Auto",
                    minimumVersion: null,
                    requiredVersion: null,
                    guid: null,
                    versionSource: ModuleDependencyVersionSource.Installed)
            },
            new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Preview.Tools"] = (installedVersion, null)
            },
            onlineLookup: null,
            resolveMissingModulesOnline: false,
            warnIfRequiredModulesOutdated: false);

        var reference = Assert.IsType<ResolvedRequiredModuleReference>(Assert.Single(resolved));
        Assert.Equal("2.0.0", reference.ModuleVersion);
        Assert.Equal(installedVersion, reference.ResolvedVersion);
        Assert.True(reference.MatchPrereleaseByBaseVersion);
    }

    [Fact]
    public void SelectLatestVersions_ComposesSeparateRepositoryPrereleaseLabel()
    {
        var selected = RequiredModuleResolutionEngine.SelectLatestVersions(
            new[]
            {
                new PSResourceInfo("Preview.Tools", "2.0.0", "PSGallery", null, null, preRelease: "beta.1")
            },
            allowPrerelease: true);

        Assert.Equal("2.0.0-beta.1", selected["Preview.Tools"].Version);
    }

    [Fact]
    public void ResolveRequiredModules_SeparatesExplicitPrereleaseRequiredVersionFromManifestConstraint()
    {
        var engine = new RequiredModuleResolutionEngine(new CollectingLogger());
        var resolved = engine.ResolveRequiredModules(
            new[]
            {
                new RequiredModuleDraftDescriptor(
                    "Preview.Tools",
                    moduleVersion: null,
                    minimumVersion: null,
                    requiredVersion: "2.0.0-beta.1",
                    guid: null,
                    versionSource: ModuleDependencyVersionSource.PSGallery)
            },
            new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
            onlineLookup: null,
            resolveMissingModulesOnline: false,
            warnIfRequiredModulesOutdated: false);

        var reference = Assert.IsType<ResolvedRequiredModuleReference>(Assert.Single(resolved));
        Assert.Equal("2.0.0", reference.RequiredVersion);
        Assert.Equal("2.0.0-beta.1", reference.ResolvedVersion);
        Assert.Null(reference.ResolvedMinimumVersion);
    }

    [Fact]
    public void ResolveRequiredModules_SeparatesExplicitPrereleaseMinimumVersionFromManifestConstraint()
    {
        var engine = new RequiredModuleResolutionEngine(new CollectingLogger());
        var resolved = engine.ResolveRequiredModules(
            new[]
            {
                new RequiredModuleDraftDescriptor(
                    "Preview.Tools",
                    moduleVersion: "2.0.0-beta.1",
                    minimumVersion: null,
                    requiredVersion: null,
                    guid: null,
                    versionSource: ModuleDependencyVersionSource.PSGallery)
            },
            new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
            onlineLookup: null,
            resolveMissingModulesOnline: false,
            warnIfRequiredModulesOutdated: false);

        var reference = Assert.IsType<ResolvedRequiredModuleReference>(Assert.Single(resolved));
        Assert.Equal("2.0.0", reference.ModuleVersion);
        Assert.Equal("2.0.0-beta.1", reference.ResolvedMinimumVersion);
        Assert.Null(reference.ResolvedVersion);
    }

    [Theory]
    [InlineData("2.0.0+build.42")]
    [InlineData("2.0.0-beta.1+build.42")]
    public void ResolveRequiredModules_RejectsBuildMetadataThatCannotBePreserved(string version)
    {
        var engine = new RequiredModuleResolutionEngine(new CollectingLogger());

        var exception = Assert.Throws<InvalidOperationException>(() => engine.ResolveRequiredModules(
            new[]
            {
                new RequiredModuleDraftDescriptor(
                    "Preview.Tools",
                    moduleVersion: null,
                    minimumVersion: null,
                    requiredVersion: version,
                    guid: null,
                    versionSource: ModuleDependencyVersionSource.PSGallery)
            },
            new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
            onlineLookup: null,
            resolveMissingModulesOnline: false,
            warnIfRequiredModulesOutdated: false));

        Assert.Contains("build metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRequiredModules_RejectsAutoResolvedBuildMetadataThatCannotBePreserved()
    {
        var engine = new RequiredModuleResolutionEngine(new CollectingLogger());

        var exception = Assert.Throws<InvalidOperationException>(() => engine.ResolveRequiredModules(
            new[]
            {
                new RequiredModuleDraftDescriptor(
                    "Preview.Tools",
                    moduleVersion: "Auto",
                    minimumVersion: null,
                    requiredVersion: null,
                    guid: null,
                    versionSource: ModuleDependencyVersionSource.PSGallery)
            },
            new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
            onlineLookup: _ => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Preview.Tools"] = ("2.0.0+build.42", null)
            },
            resolveMissingModulesOnline: true,
            warnIfRequiredModulesOutdated: false));

        Assert.Contains("build metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Infos { get; } = new();

        public bool IsVerbose => false;

        public void Info(string message) => Infos.Add(message ?? string.Empty);
        public void Success(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
