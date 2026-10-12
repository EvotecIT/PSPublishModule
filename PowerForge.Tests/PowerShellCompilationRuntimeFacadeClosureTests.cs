using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void StrictClosureRejectsRuntimeProcessReferencesWithoutAuthenticatedPackageBytes()
    {
        using var fixture = new RuntimeFacadeFixture();
        var caller = fixture.ProcessCaller("Runtime.ProcessCaller");
        var files = fixture.Files(caller);
        // Even an exact, self-authored target graph is not independent publisher authority.
        var forged = Assert.Throws<InvalidOperationException>(() =>
            Verify(files, runtimeIdentifier: "win-x64", graph: fixture.Graph(caller)));
        Assert.Contains("native-process launch reference", forged.Message);

        var unreviewed = Assert.Throws<InvalidOperationException>(() => Verify(files, runtimeIdentifier: "win-x64"));
        Assert.Contains("native-process launch reference", unreviewed.Message);
        foreach (var mismatch in new[] { "hash", "target", "rid", "provenance", "role", "disposition", "exists", "primary" })
        {
            var graph = fixture.Graph(caller);
            var node = graph.Nodes[0];
            files = fixture.Files(caller);
            switch (mismatch)
            {
                case "hash": node.Identity.Sha256 = new string('0', 64); break;
                case "target": node.Identity.TargetFramework = "net9.0"; break;
                case "rid": node.Identity.RuntimeIdentifier = "linux-x64"; break;
                case "provenance": node.Identity.Provenance = "Provider"; break;
                case "role": node.Roles = PowerShellCompilationDependencyGraphRole.Dependency; break;
                case "disposition": node.Disposition = default; break;
                case "exists": node.Exists = false; break;
                case "primary": files[0].Role = "Primary"; break;
            }
            var error = Assert.Throws<InvalidOperationException>(() => Verify(files, runtimeIdentifier: "win-x64", graph: graph));
            Assert.Contains("native-process launch reference", error.Message);
        }
    }

    [Fact]
    public void StrictClosureRejectsForgedRuntimeNativeCallersIncludingCoreLibIntrinsics()
    {
        using var fixture = new RuntimeFacadeFixture();
        foreach (var name in new[] { "System.Private.CoreLib", "Runtime.UntrustedCaller" })
        foreach (var import in new[] { "QCall", "__Internal", "System.Globalization.Native", "kernel32.dll" })
        {
            var caller = fixture.NativeCaller(name, import);
            var graph = fixture.Graph(caller);
            var error = Assert.Throws<InvalidOperationException>(() =>
                Verify(fixture.Files(caller), runtimeIdentifier: "win-x64", graph: graph));
            Assert.Contains("only exact reviewed", error.Message);
        }
    }

    [Fact]
    public void StrictClosureRejectsPowerShellReferencesEvenInExactReviewedRuntimeBytes()
    {
        using var fixture = new RuntimeFacadeFixture();
        var caller = fixture.ProcessCaller("Runtime.ForbiddenCaller", referencesPowerShell: true);
        var error = Assert.Throws<InvalidOperationException>(() =>
            Verify(fixture.Files(caller), runtimeIdentifier: "win-x64", graph: fixture.Graph(caller)));
        Assert.Contains("forbidden PowerShell assembly 'System.Management.Automation'", error.Message);
    }

    [WindowsX64Fact]
    public void StrictClosureRejectsRuntimeHelperExecutableWithoutAuthenticatedPackageBytes()
    {
        var helper = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "createdump.exe");
        Assert.True(File.Exists(helper), "The Windows .NET runtime must supply createdump.exe for this closure fixture.");
        var files = new[] { new PowerShellCompilationArtifactFile { Path = helper, Role = "RuntimeDependency" } };
        var identity = new PowerShellCompilationDependencyIdentity
        {
            Name = "createdump.exe", TargetFramework = "net10.0", RuntimeIdentifier = "win-x64",
            Provenance = "DotNetRuntimePack", Source = "runtime-pack/native/createdump.exe",
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(helper))).ToLowerInvariant()
        };
        var graph = new PowerShellCompilationDependencyGraph
        {
            Nodes = new[] { new PowerShellCompilationDependencyNode
            {
                Id = "runtime-helper", Exists = true, Kind = PowerShellCompilationDependencyNodeKind.NativeLibrary,
                Roles = PowerShellCompilationDependencyGraphRole.Deployment,
                Disposition = PowerShellCompilationDependencyGraphDisposition.PrivateRestored, Identity = identity
            } }
        };
        Assert.False(Verify(files, runtimeIdentifier: "win-x64", graph: graph).Verified);
        Assert.False(Verify(files, runtimeIdentifier: "win-x64").Verified);
        identity.TargetFramework = "net9.0";
        Assert.False(Verify(files, runtimeIdentifier: "win-x64", graph: graph).Verified);
        identity.TargetFramework = "net10.0";
        identity.RuntimeIdentifier = "linux-x64";
        Assert.False(Verify(files, runtimeIdentifier: "win-x64", graph: graph).Verified);
        identity.RuntimeIdentifier = "win-x64";
        var hash = identity.Sha256;
        identity.Sha256 = new string('0', 64);
        Assert.False(Verify(files, runtimeIdentifier: "win-x64", graph: graph).Verified);
        identity.Sha256 = hash;
        files[0].Role = "Primary";
        Assert.False(Verify(files, runtimeIdentifier: "win-x64", graph: graph).Verified);
    }

    [Fact]
    public void StrictClosureAllowsUnusedOptionalForwardersOnlyFromExactReviewedRuntimeFacades()
    {
        using var fixture = new RuntimeFacadeFixture();
        var facade = fixture.Facade("Reviewed.Facade", "Optional.Dependency");
        var graph = fixture.Graph(facade);
        Assert.True(Verify(fixture.Files(facade), runtimeIdentifier: "win-x64", graph: graph).Verified);

        foreach (var mismatch in new[] { "hash", "target", "rid", "provenance", "role", "disposition", "exists" })
        {
            graph = fixture.Graph(facade);
            var node = graph.Nodes[0];
            switch (mismatch)
            {
                case "hash": node.Identity.Sha256 = new string('0', 64); break;
                case "target": node.Identity.TargetFramework = "net9.0"; break;
                case "rid": node.Identity.RuntimeIdentifier = "linux-x64"; break;
                case "provenance": node.Identity.Provenance = "Provider"; break;
                case "role": node.Roles = PowerShellCompilationDependencyGraphRole.Dependency; break;
                case "disposition": node.Disposition = default; break;
                case "exists": node.Exists = false; break;
            }
            var error = Assert.Throws<InvalidOperationException>(() =>
                Verify(fixture.Files(facade), runtimeIdentifier: "win-x64", graph: graph));
            Assert.Contains("missing managed assembly", error.Message);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StrictClosureRejectsUsedMissingForwardedTypes(bool nested, bool chained)
    {
        using var fixture = new RuntimeFacadeFixture();
        var facade = fixture.Facade("Reviewed.Facade", chained ? "Reviewed.Second" : "Optional.Dependency", nested);
        var second = chained ? fixture.Facade("Reviewed.Second", "Optional.Dependency", nested) : null;
        var consumer = fixture.Consumer("Consumer", "Reviewed.Facade", nested);
        var delivered = second is null ? new[] { facade, consumer } : new[] { facade, second, consumer };
        var error = Assert.Throws<InvalidOperationException>(() => Verify(fixture.Files(delivered),
            runtimeIdentifier: "win-x64", graph: fixture.Graph(delivered)));
        Assert.Contains("uses forwarded type", error.Message);
        Assert.Contains("Optional.Dependency", error.Message);
    }

    [Fact]
    public void StrictClosureAcceptsUsedForwardedTypesWhenTheirDestinationIsDelivered()
    {
        using var fixture = new RuntimeFacadeFixture();
        var facade = fixture.Facade("Reviewed.Facade", "Optional.Dependency");
        var consumer = fixture.Consumer("Consumer", "Reviewed.Facade");
        var destination = fixture.Definition("Optional.Dependency");
        var delivered = new[] { facade, consumer, destination };
        Assert.True(Verify(fixture.Files(delivered), runtimeIdentifier: "win-x64", graph: fixture.Graph(delivered)).Verified);
    }

    [Fact]
    public void StrictClosureRejectsUsedCyclicForwarders()
    {
        using var fixture = new RuntimeFacadeFixture();
        var first = fixture.Facade("Reviewed.First", "Reviewed.Second");
        var second = fixture.Facade("Reviewed.Second", "Reviewed.First");
        var consumer = fixture.Consumer("Consumer", "Reviewed.First");
        var delivered = new[] { first, second, consumer };
        var error = Assert.Throws<InvalidOperationException>(() => Verify(fixture.Files(delivered),
            runtimeIdentifier: "win-x64", graph: fixture.Graph(delivered)));
        Assert.Contains("cyclic forwarding chain", error.Message);
    }

    [Fact]
    public void StrictClosureResolvesZeroVersionImplementationOnlyWithDeliveredReviewedIdentity()
    {
        using var fixture = new RuntimeFacadeFixture();
        var consumer = fixture.Consumer("Consumer", "Runtime.Implementation", version: new Version(0, 0, 0, 0));
        var implementation = fixture.Definition("Runtime.Implementation");
        var delivered = new[] { consumer, implementation };
        var graph = fixture.Graph(delivered);
        Assert.True(Verify(fixture.Files(delivered), runtimeIdentifier: "win-x64", graph: graph).Verified);
        Assert.Throws<InvalidOperationException>(() => Verify(fixture.Files(consumer), runtimeIdentifier: "win-x64", graph: graph));
        graph.Nodes[1].Identity.Sha256 = new string('0', 64);
        Assert.Throws<InvalidOperationException>(() => Verify(fixture.Files(delivered), runtimeIdentifier: "win-x64", graph: graph));
    }

    // Metadata-only images keep forwarding and identity edge cases independent of C#'s
    // eager resolution of forwarded types. They are never loaded or executed.
    private sealed class RuntimeFacadeFixture : IDisposable
    {
        private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PFC", Guid.NewGuid().ToString("N"))).FullName;
        private readonly byte[] publicKey = typeof(object).Assembly.GetName().GetPublicKey()!;
        private readonly Version version = new(10, 0, 0, 0);

        internal string Facade(string name, string destination, bool nested = false)
            => Write(name, metadata =>
            {
                var target = Reference(metadata, destination, version);
                var outer = metadata.AddExportedType(TypeAttributes.Public | (TypeAttributes)0x00200000,
                    metadata.GetOrAddString("Sample"), metadata.GetOrAddString("Value"), target, 0);
                if (nested) metadata.AddExportedType(TypeAttributes.NestedPublic, default,
                    metadata.GetOrAddString("Nested"), outer, 0);
            });

        internal string Consumer(string name, string destination, bool nested = false, Version? version = null)
            => Write(name, metadata =>
            {
                var target = Reference(metadata, destination, version ?? this.version);
                var outer = metadata.AddTypeReference(target, metadata.GetOrAddString("Sample"), metadata.GetOrAddString("Value"));
                if (nested) metadata.AddTypeReference(outer, default, metadata.GetOrAddString("Nested"));
            });

        internal string Definition(string name)
            => Write(name, metadata => metadata.AddTypeDefinition(TypeAttributes.Public,
                metadata.GetOrAddString("Sample"), metadata.GetOrAddString("Value"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1)));

        internal string ProcessCaller(string name, bool referencesPowerShell = false)
            => Write(name, metadata =>
            {
                var processAssembly = typeof(System.Diagnostics.Process).Assembly.GetName();
                var target = metadata.AddAssemblyReference(metadata.GetOrAddString(processAssembly.Name!),
                    processAssembly.Version!, default, metadata.GetOrAddBlob(processAssembly.GetPublicKey()!), AssemblyFlags.PublicKey, default);
                var process = metadata.AddTypeReference(target, metadata.GetOrAddString("System.Diagnostics"), metadata.GetOrAddString("Process"));
                metadata.AddMemberReference(process, metadata.GetOrAddString("Start"), default);
                if (referencesPowerShell) Reference(metadata, "System.Management.Automation", version);
            });

        internal string NativeCaller(string name, string import)
            => Write(name, metadata =>
            {
                var method = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
                    MethodImplAttributes.PreserveSig, metadata.GetOrAddString("Invoke"),
                    metadata.GetOrAddBlob(new byte[] { 0, 0, 1 }), 0, MetadataTokens.ParameterHandle(1));
                metadata.AddMethodImport(method, MethodImportAttributes.ExactSpelling, metadata.GetOrAddString("Invoke"),
                    metadata.AddModuleReference(metadata.GetOrAddString(import)));
            });

        internal PowerShellCompilationArtifactFile[] Files(params string[] paths)
            => paths.Select(path => new PowerShellCompilationArtifactFile { Path = path, Role = "RuntimeDependency" }).ToArray();

        internal PowerShellCompilationDependencyGraph Graph(params string[] paths)
            => new()
            {
                Nodes = paths.Select(path => new PowerShellCompilationDependencyNode
                {
                    Id = Path.GetFileNameWithoutExtension(path), Exists = true,
                    Kind = PowerShellCompilationDependencyNodeKind.ManagedLibrary,
                    Roles = PowerShellCompilationDependencyGraphRole.Deployment,
                    Disposition = PowerShellCompilationDependencyGraphDisposition.PrivateRestored,
                    Identity = new PowerShellCompilationDependencyIdentity
                    {
                        Name = Path.GetFileNameWithoutExtension(path), Version = version.ToString(),
                        PublicKeyToken = PowerShellTargetRuntimeAssemblyCatalog.ComputePublicKeyToken(publicKey),
                        Culture = "neutral", ContentType = "Default", Provenance = "DotNetRuntimePack",
                        TargetFramework = "net10.0", RuntimeIdentifier = "win-x64",
                        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
                    }
                }).ToArray()
            };

        private AssemblyReferenceHandle Reference(MetadataBuilder metadata, string name, Version referenceVersion)
            => metadata.AddAssemblyReference(metadata.GetOrAddString(name), referenceVersion, default,
                metadata.GetOrAddBlob(publicKey), AssemblyFlags.PublicKey, default);

        private string Write(string name, Action<MetadataBuilder> populate)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            metadata.AddAssembly(metadata.GetOrAddString(name), version, default, metadata.GetOrAddBlob(publicKey),
                AssemblyFlags.PublicKey, AssemblyHashAlgorithm.None);
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            populate(metadata);
            var image = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
                new MetadataRootBuilder(metadata), new BlobBuilder());
            var blob = new BlobBuilder();
            image.Serialize(blob);
            var path = Path.Combine(root, name + ".dll");
            File.WriteAllBytes(path, blob.ToArray());
            return path;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
