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

    private static string CreateModule(string root, string name, string version)
    {
        var moduleRoot = Path.Combine(root, "Dependencies", name, version);
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
}
