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
                guid: "Auto")
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
            approvedModules: new[] { "Graphimo" });

        Assert.Single(filtered);
        Assert.Equal("PSWriteHTML", filtered[0].ModuleName);
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
