using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class EmbeddedModuleDependencyServiceTests
{
    [Fact]
    public void Embed_CopiesInstalledModuleAndWritesDependencyManifest()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "BuiltModule"));
            var dependencyRoot = CreateModule(root.FullName, "Microsoft.Graph.Authentication", "2.25.0");
            var provider = new Provider(new InstalledModuleMetadata(
                "Microsoft.Graph.Authentication",
                "2.25.0",
                guid: null,
                moduleBasePath: dependencyRoot));

            var service = new EmbeddedModuleDependencyService(new NullLogger());
            var manifest = service.Embed(
                moduleRoot.FullName,
                new[] { new RequiredModuleReference("Microsoft.Graph.Authentication", requiredVersion: "2.25.0") },
                provider);

            var entry = Assert.Single(manifest.Dependencies);
            Assert.Equal("Microsoft.Graph.Authentication", entry.Name);
            Assert.Equal("2.25.0", entry.Version);
            Assert.True(File.Exists(Path.Combine(moduleRoot.FullName, "Internals", "Modules", "module-dependencies.json")));
            Assert.True(File.Exists(Path.Combine(moduleRoot.FullName, "Internals", "Modules", "Microsoft.Graph.Authentication", "2.25.0", "Microsoft.Graph.Authentication.psd1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Embed_PreservesDeclarationOrderInDependencyManifest()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "BuiltModule"));
            var zDependencyRoot = CreateModule(root.FullName, "Z.Dependency", "1.0.0");
            var aDependencyRoot = CreateModule(root.FullName, "A.Dependency", "1.0.0");
            var provider = new Provider(
                new InstalledModuleMetadata("Z.Dependency", "1.0.0", guid: null, moduleBasePath: zDependencyRoot),
                new InstalledModuleMetadata("A.Dependency", "1.0.0", guid: null, moduleBasePath: aDependencyRoot));

            var service = new EmbeddedModuleDependencyService(new NullLogger());
            var manifest = service.Embed(
                moduleRoot.FullName,
                new[]
                {
                    new RequiredModuleReference("Z.Dependency", requiredVersion: "1.0.0"),
                    new RequiredModuleReference("A.Dependency", requiredVersion: "1.0.0")
                },
                provider);

            Assert.Equal(new[] { "Z.Dependency", "A.Dependency" }, manifest.Dependencies.Select(static entry => entry.Name));

            var persisted = EmbeddedModuleDependencyService.ReadManifest(Path.Combine(moduleRoot.FullName, "Internals", "Modules", "module-dependencies.json"));
            Assert.Equal(new[] { "Z.Dependency", "A.Dependency" }, persisted.Dependencies.Select(static entry => entry.Name));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Embed_UsesVersionAwareProviderForRequiredVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "BuiltModule"));
            var exactDependencyRoot = CreateModule(root.FullName, "Pinned.Dependency", "1.0.0");
            var latestDependencyRoot = CreateModule(root.FullName, "Pinned.Dependency", "2.0.0");
            var provider = new VersionAwareProvider(
                latest: new InstalledModuleMetadata("Pinned.Dependency", "2.0.0", guid: null, moduleBasePath: latestDependencyRoot),
                requested: new InstalledModuleMetadata("Pinned.Dependency", "1.0.0", guid: null, moduleBasePath: exactDependencyRoot));

            var service = new EmbeddedModuleDependencyService(new NullLogger());
            var manifest = service.Embed(
                moduleRoot.FullName,
                new[] { new RequiredModuleReference("Pinned.Dependency", requiredVersion: "1.0.0") },
                provider);

            var entry = Assert.Single(manifest.Dependencies);
            Assert.Equal("1.0.0", entry.Version);
            Assert.True(File.Exists(Path.Combine(moduleRoot.FullName, "Internals", "Modules", "Pinned.Dependency", "1.0.0", "Pinned.Dependency.psd1")));
            Assert.False(Directory.Exists(Path.Combine(moduleRoot.FullName, "Internals", "Modules", "Pinned.Dependency", "2.0.0")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Embed_ClearsStalePayloadFoldersBeforeWritingManifest()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "BuiltModule"));
            var currentDependencyRoot = CreateModule(root.FullName, "Current.Dependency", "1.0.0");
            var staleDependencyRoot = CreateModule(root.FullName, "Stale.Dependency", "1.0.0");
            var provider = new Provider(
                new InstalledModuleMetadata("Current.Dependency", "1.0.0", guid: null, moduleBasePath: currentDependencyRoot),
                new InstalledModuleMetadata("Stale.Dependency", "1.0.0", guid: null, moduleBasePath: staleDependencyRoot));

            var service = new EmbeddedModuleDependencyService(new NullLogger());
            service.Embed(
                moduleRoot.FullName,
                new[]
                {
                    new RequiredModuleReference("Current.Dependency", requiredVersion: "1.0.0"),
                    new RequiredModuleReference("Stale.Dependency", requiredVersion: "1.0.0")
                },
                provider);

            var manifest = service.Embed(
                moduleRoot.FullName,
                new[] { new RequiredModuleReference("Current.Dependency", requiredVersion: "1.0.0") },
                provider);

            var entry = Assert.Single(manifest.Dependencies);
            Assert.Equal("Current.Dependency", entry.Name);
            Assert.True(File.Exists(Path.Combine(moduleRoot.FullName, "Internals", "Modules", "Current.Dependency", "1.0.0", "Current.Dependency.psd1")));
            Assert.False(Directory.Exists(Path.Combine(moduleRoot.FullName, "Internals", "Modules", "Stale.Dependency")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Install_CopiesEmbeddedPayloadToExplicitVersionFolderAndWritesReceipt()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "BuiltModule"));
            var dependencyRoot = CreateModule(root.FullName, "Microsoft.Graph.Authentication", "2.25.0");
            var provider = new Provider(new InstalledModuleMetadata(
                "Microsoft.Graph.Authentication",
                "2.25.0",
                guid: null,
                moduleBasePath: dependencyRoot));

            var service = new EmbeddedModuleDependencyService(new NullLogger());
            service.Embed(
                moduleRoot.FullName,
                new[] { new RequiredModuleReference("Microsoft.Graph.Authentication", requiredVersion: "2.25.0") },
                provider);

            var destinationRoot = Path.Combine(root.FullName, "PrivateDeps");
            var results = service.Install(
                Path.Combine(moduleRoot.FullName, "Internals", "Modules", "module-dependencies.json"),
                destinationRoot);

            var result = Assert.Single(results);
            Assert.Equal("Copy", result.Action);
            Assert.True(File.Exists(Path.Combine(destinationRoot, "module-dependencies.json")));
            Assert.True(File.Exists(Path.Combine(destinationRoot, "Microsoft.Graph.Authentication", "2.25.0", "Microsoft.Graph.Authentication.psd1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Install_DefaultMergeCopiesMissingFilesIntoExistingDependencyFolder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "BuiltModule"));
            var dependencyRoot = CreateModule(root.FullName, "Microsoft.Graph.Authentication", "2.25.0");
            File.WriteAllText(Path.Combine(dependencyRoot, "new-file.txt"), "new");
            var provider = new Provider(new InstalledModuleMetadata(
                "Microsoft.Graph.Authentication",
                "2.25.0",
                guid: null,
                moduleBasePath: dependencyRoot));

            var service = new EmbeddedModuleDependencyService(new NullLogger());
            service.Embed(
                moduleRoot.FullName,
                new[] { new RequiredModuleReference("Microsoft.Graph.Authentication", requiredVersion: "2.25.0") },
                provider);

            var destinationRoot = Path.Combine(root.FullName, "PrivateDeps");
            var destinationModule = Path.Combine(destinationRoot, "Microsoft.Graph.Authentication", "2.25.0");
            Directory.CreateDirectory(destinationModule);
            File.WriteAllText(Path.Combine(destinationModule, "existing.txt"), "keep");

            var results = service.Install(
                Path.Combine(moduleRoot.FullName, "Internals", "Modules", "module-dependencies.json"),
                destinationRoot);

            var result = Assert.Single(results);
            Assert.Equal("Merge", result.Action);
            Assert.True(File.Exists(Path.Combine(destinationModule, "existing.txt")));
            Assert.True(File.Exists(Path.Combine(destinationModule, "new-file.txt")));
            Assert.True(File.Exists(Path.Combine(destinationModule, "Microsoft.Graph.Authentication.psd1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Install_WithDependencyFilter_PreservesUnselectedExistingReceiptEntries()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "BuiltModule"));
            var aDependencyRoot = CreateModule(root.FullName, "A.Dependency", "1.0.0");
            var bDependencyRoot = CreateModule(root.FullName, "B.Dependency", "1.0.0");
            var provider = new Provider(
                new InstalledModuleMetadata("A.Dependency", "1.0.0", guid: null, moduleBasePath: aDependencyRoot),
                new InstalledModuleMetadata("B.Dependency", "1.0.0", guid: null, moduleBasePath: bDependencyRoot));

            var service = new EmbeddedModuleDependencyService(new NullLogger());
            var manifestPath = Path.Combine(moduleRoot.FullName, "Internals", "Modules", "module-dependencies.json");
            service.Embed(
                moduleRoot.FullName,
                new[]
                {
                    new RequiredModuleReference("A.Dependency", requiredVersion: "1.0.0"),
                    new RequiredModuleReference("B.Dependency", requiredVersion: "1.0.0")
                },
                provider);

            var destinationRoot = Path.Combine(root.FullName, "PrivateDeps");
            service.Install(manifestPath, destinationRoot);

            var results = service.Install(
                manifestPath,
                destinationRoot,
                dependencyNames: new[] { "A.Dependency" });

            var result = Assert.Single(results);
            Assert.Equal("A.Dependency", result.Name);
            var receipt = EmbeddedModuleDependencyService.ReadManifest(Path.Combine(destinationRoot, "module-dependencies.json"));
            Assert.Equal(new[] { "A.Dependency", "B.Dependency" }, receipt.Dependencies.Select(static entry => entry.Name));
            Assert.True(File.Exists(Path.Combine(destinationRoot, "B.Dependency", "1.0.0", "B.Dependency.psd1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveEntryPath_RejectsPathsEscapingManifestRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var manifestRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "BuiltModule", "Internals", "Modules"));
            var manifestPath = Path.Combine(manifestRoot.FullName, "module-dependencies.json");
            File.WriteAllText(manifestPath, "{}");

            var entry = new EmbeddedModuleDependencyEntry
            {
                Name = "Bad.Dependency",
                Version = "1.0.0",
                RelativePath = "../Bad.Dependency"
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                EmbeddedModuleDependencyService.ResolveEntryPath(manifestPath, entry));
            Assert.Contains("escapes", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Install_WithRootModule_CopiesPrivateRuntimeAndWritesRootReceipt()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleRoot = CreateModuleAt(Path.Combine(root.FullName, "Installed", "OurModule", "1.2.3"), "OurModule", "1.2.3");
            var dependencyRoot = CreateModule(root.FullName, "Microsoft.Graph.Authentication", "2.25.0");
            var provider = new Provider(new InstalledModuleMetadata(
                "Microsoft.Graph.Authentication",
                "2.25.0",
                guid: null,
                moduleBasePath: dependencyRoot));

            var service = new EmbeddedModuleDependencyService(new NullLogger());
            service.Embed(
                moduleRoot,
                new[] { new RequiredModuleReference("Microsoft.Graph.Authentication", requiredVersion: "2.25.0") },
                provider);

            var destinationRoot = Path.Combine(root.FullName, "PrivateDeps");
            var results = service.Install(
                Path.Combine(moduleRoot, "Internals", "Modules", "module-dependencies.json"),
                destinationRoot,
                rootModuleName: "OurModule",
                rootModuleVersion: "1.2.3",
                rootModuleBasePath: moduleRoot);

            Assert.Equal(new[] { "RootModule", "Dependency" }, results.Select(static result => result.Kind));
            Assert.True(File.Exists(Path.Combine(destinationRoot, "module-dependencies.json")));
            Assert.True(File.Exists(Path.Combine(destinationRoot, "OurModule", "1.2.3", "OurModule.psd1")));
            Assert.True(File.Exists(Path.Combine(destinationRoot, "Microsoft.Graph.Authentication", "2.25.0", "Microsoft.Graph.Authentication.psd1")));

            var receipt = EmbeddedModuleDependencyService.ReadManifest(Path.Combine(destinationRoot, "module-dependencies.json"));
            Assert.NotNull(receipt.RootModule);
            Assert.Equal("OurModule", receipt.RootModule!.Name);
            Assert.Equal("1.2.3", receipt.RootModule.Version);
            Assert.Equal("OurModule/1.2.3", receipt.RootModule.RelativePath);
            var dependency = Assert.Single(receipt.Dependencies);
            Assert.Equal("Microsoft.Graph.Authentication", dependency.Name);
            Assert.Equal("Microsoft.Graph.Authentication/2.25.0", dependency.RelativePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveRootModuleEntry_RequiresMatchingRootModule()
    {
        var manifest = new EmbeddedModuleDependencyManifest
        {
            RootModule = new EmbeddedModuleDependencyEntry
            {
                Name = "OurModule",
                Version = "1.2.3",
                RelativePath = "OurModule/1.2.3"
            }
        };

        var entry = EmbeddedModuleDependencyService.ResolveRootModuleEntry(manifest, "OurModule");
        Assert.Equal("OurModule", entry.Name);

        Assert.Throws<InvalidOperationException>(() => EmbeddedModuleDependencyService.ResolveRootModuleEntry(manifest, "OtherModule"));
    }

    private static string CreateModule(string root, string name, string version)
        => CreateModuleAt(Path.Combine(root, "Dependencies", name, version), name, version);

    private static string CreateModuleAt(string moduleRoot, string name, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{name}.psm1"), string.Empty);
        File.WriteAllText(
            Path.Combine(moduleRoot, $"{name}.psd1"),
            string.Join(Environment.NewLine, new[]
            {
                "@{",
                $"    RootModule = '{name}.psm1'",
                $"    ModuleVersion = '{version}'",
                "    FunctionsToExport = @()",
                "    CmdletsToExport = @()",
                "    AliasesToExport = @()",
                "}"
            }) + Environment.NewLine);
        return moduleRoot;
    }

    private sealed class Provider : IModuleDependencyMetadataProvider
    {
        private readonly IReadOnlyDictionary<string, InstalledModuleMetadata> _metadata;

        public Provider(params InstalledModuleMetadata[] metadata)
        {
            _metadata = metadata.ToDictionary(static item => item.Name, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => new Dictionary<string, InstalledModuleMetadata>(_metadata, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<RequiredModuleReference>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class VersionAwareProvider : IModuleDependencyVersionedMetadataProvider
    {
        private readonly InstalledModuleMetadata _latest;
        private readonly InstalledModuleMetadata _requested;

        public VersionAwareProvider(InstalledModuleMetadata latest, InstalledModuleMetadata requested)
        {
            _latest = latest;
            _requested = requested;
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                [_latest.Name] = _latest
            };

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetInstalledModules(IReadOnlyList<RequiredModuleReference> references)
            => new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                [_requested.Name] = _requested
            };

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<RequiredModuleReference>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
    }
}
