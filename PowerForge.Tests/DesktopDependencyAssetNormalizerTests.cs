using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PowerForge;

namespace PowerForge.Tests;

public sealed class DesktopDependencyAssetNormalizerTests
{
    [Theory]
    [InlineData("System.Memory", "4.6.3", "4.0.5.0", "4.0.2.0")]
    [InlineData("System.Buffers", "4.6.1", "4.0.5.0", "4.0.2.0")]
    [InlineData("System.Threading.Tasks.Extensions", "4.6.3", "4.2.4.0", "4.2.1.0")]
    public void Normalize_ResolvedFacade_SelectsSamePackageAssetAndRecordsVerifiedBinding(
        string package, string version, string before, string after)
    {
        using var fixture = new DesktopAssetFixture(package, version);
        string originalCacheHash = DesktopAssetFixture.Hash(fixture.OriginalAsset);
        string selectedCacheHash = DesktopAssetFixture.Hash(fixture.SelectedAsset);
        string corePayload = Path.Combine(Directory.CreateDirectory(Path.Combine(fixture.Root, "Core")).FullName, package + ".dll");
        File.Copy(fixture.OriginalAsset, corePayload);

        fixture.Normalize();

        Assert.Equal(selectedCacheHash, DesktopAssetFixture.Hash(fixture.PayloadAsset));
        Assert.Equal(originalCacheHash, DesktopAssetFixture.Hash(fixture.OriginalAsset));
        Assert.Equal(selectedCacheHash, DesktopAssetFixture.Hash(fixture.SelectedAsset));
        Assert.Equal(originalCacheHash, DesktopAssetFixture.Hash(corePayload));
        Assert.Equal(new Version(after), AssemblyName.GetAssemblyName(fixture.PayloadAsset).Version);
        string line = Assert.Single(File.ReadAllLines(fixture.PolicyPath));
        Assert.Equal(new[] { package, before, after, "cc7b13ffcd2ddd51", selectedCacheHash }, line.Split('|'));
    }

    [Fact]
    public void Normalize_CoreFramework_PreservesPayloadAndDoesNotRequireDesktopAssets()
    {
        using var fixture = new DesktopAssetFixture();
        string original = DesktopAssetFixture.Hash(fixture.PayloadAsset);

        DesktopDependencyAssetNormalizer.Normalize(fixture.Payload, null, "net10.0", fixture.Mapping);

        Assert.Equal(original, DesktopAssetFixture.Hash(fixture.PayloadAsset));
        Assert.False(File.Exists(fixture.PolicyPath));
    }

    [Fact]
    public void Normalize_NoExplicitMapping_PreservesDefaultAsset()
    {
        using var fixture = new DesktopAssetFixture();
        string original = DesktopAssetFixture.Hash(fixture.PayloadAsset);

        DesktopDependencyAssetNormalizer.Normalize(fixture.Payload, null, "net472", new Dictionary<string, string>());

        Assert.Equal(original, DesktopAssetFixture.Hash(fixture.PayloadAsset));
        Assert.False(File.Exists(fixture.PolicyPath));
    }

    [Theory]
    [InlineData("missing-package")]
    [InlineData("unlisted-selected-asset")]
    [InlineData("traversal")]
    [InlineData("different-package-version")]
    [InlineData("payload-provenance")]
    public void Normalize_InvalidResolvedProvenance_RejectsBeforeChangingPayload(string fault)
    {
        using var fixture = new DesktopAssetFixture();
        JsonObject library = fixture.Library;
        switch (fault)
        {
            case "missing-package":
                fixture.Target.Remove(fixture.LibraryKey);
                break;
            case "unlisted-selected-asset":
                library["files"] = new JsonArray(fixture.OriginalRelative);
                break;
            case "traversal":
                library["path"] = "../outside";
                fixture.CopyPackageAssets(Path.Combine(fixture.Root, "outside"));
                break;
            case "different-package-version":
                library["path"] = fixture.Package.ToLowerInvariant() + "/0.0.0";
                fixture.CopyPackageAssets(Path.Combine(fixture.PackageRoot, fixture.Package.ToLowerInvariant(), "0.0.0"));
                break;
            case "payload-provenance":
                File.AppendAllText(fixture.PayloadAsset, "changed-payload");
                break;
        }
        fixture.WriteAssets();
        string before = DesktopAssetFixture.Hash(fixture.PayloadAsset);

        Assert.Throws<InvalidOperationException>(() => fixture.Normalize());

        Assert.Equal(before, DesktopAssetFixture.Hash(fixture.PayloadAsset));
        Assert.False(File.Exists(fixture.PolicyPath));
    }

    [Fact]
    public void Normalize_LaterPackageHasInvalidProvenance_LeavesEarlierValidatedChoiceUnchanged()
    {
        using var fixture = new DesktopAssetFixture("System.Buffers", "4.6.1");
        fixture.Mapping.Add("Unresolved.LaterPackage", "netstandard2.0");
        string before = DesktopAssetFixture.Hash(fixture.PayloadAsset);

        Assert.Throws<InvalidOperationException>(() => fixture.Normalize());

        Assert.Equal(before, DesktopAssetFixture.Hash(fixture.PayloadAsset));
        Assert.False(File.Exists(fixture.PolicyPath));
    }

    [Fact]
    public void Normalize_NonFacadeVersionChange_IsRejectedEvenForSamePackageVersion()
    {
        using var fixture = new DesktopAssetFixture("System.Runtime.CompilerServices.Unsafe", "6.1.2");
        Assert.NotEqual(AssemblyName.GetAssemblyName(fixture.OriginalAsset).Version,
            AssemblyName.GetAssemblyName(fixture.SelectedAsset).Version);
        string before = DesktopAssetFixture.Hash(fixture.PayloadAsset);

        Assert.Throws<InvalidOperationException>(() => fixture.Normalize());

        Assert.Equal(before, DesktopAssetFixture.Hash(fixture.PayloadAsset));
        Assert.False(File.Exists(fixture.PolicyPath));
    }

    [Fact]
    public void Normalize_NonFacadeIdenticalIdentity_DoesNotInventDowngradePolicy()
    {
        using var fixture = new DesktopAssetFixture("System.Runtime.CompilerServices.Unsafe", "6.1.2", "netstandard2.0");

        fixture.Normalize();

        Assert.Equal(DesktopAssetFixture.Hash(fixture.SelectedAsset), DesktopAssetFixture.Hash(fixture.PayloadAsset));
        Assert.Empty(File.Exists(fixture.PolicyPath) ? File.ReadAllLines(fixture.PolicyPath) : Array.Empty<string>());
    }

    [Theory]
    [InlineData("System.Memory", "4.0.5.0", "4.0.2.0", "cc7b13ffcd2ddd51", true)]
    [InlineData("System.Buffers", "4.0.5.0", "4.0.2.0", "cc7b13ffcd2ddd51", true)]
    [InlineData("System.Threading.Tasks.Extensions", "4.2.4.0", "4.2.1.0", "cc7b13ffcd2ddd51", true)]
    [InlineData("System.Memory", "4.0.4.0", "4.0.2.0", "cc7b13ffcd2ddd51", false)]
    [InlineData("System.Memory", "4.0.5.0", "4.0.1.0", "cc7b13ffcd2ddd51", false)]
    [InlineData("System.Memory", "4.0.5.0", "4.0.2.0", "0000000000000000", false)]
    [InlineData("Other.Library", "4.0.5.0", "4.0.2.0", "cc7b13ffcd2ddd51", false)]
    public void IsQualifiedFacadePair_IdentityBoundary_AllowsOnlyDeclaredMicrosoftPairs(
        string name, string before, string after, string token, bool expected)
    {
        var original = new AssemblyName($"{name}, Version={before}, Culture=neutral, PublicKeyToken={token}");
        var selected = new AssemblyName($"{name}, Version={after}, Culture=neutral, PublicKeyToken={token}");

        Assert.Equal(expected, DesktopDependencyAssetNormalizer.IsQualifiedFacadePair(original, selected));
    }
}

// Shared only by these normalizer tests and the generated-bootstrapper subprocess proof.
// Real restored package bytes preserve the signature-bearing assembly identities.
internal sealed class DesktopAssetFixture : IDisposable
{
    internal DesktopAssetFixture(string package = "System.Memory", string version = "4.6.3", string originalFramework = "net462")
    {
        string cache = FindCachedPackage(package, version);
        foreach (string target in new[] { originalFramework, "netstandard2.0" }.Distinct(StringComparer.Ordinal))
        {
            string source = Path.Combine(cache, "lib", target, package + ".dll");
            Assert.True(File.Exists(source), $"Restored package asset is required: {source}. Restore the existing PowerForge net472 closure first.");
        }
        Root = Path.Combine(Path.GetTempPath(), "pf-desktop-assets-" + Guid.NewGuid().ToString("N"));
        Package = package;
        PackageRoot = Directory.CreateDirectory(Path.Combine(Root, "packages")).FullName;
        Payload = Directory.CreateDirectory(Path.Combine(Root, "Lib", "Default")).FullName;
        OriginalRelative = $"lib/{originalFramework}/{package}.dll";
        SelectedRelative = $"lib/netstandard2.0/{package}.dll";
        string packagePath = package.ToLowerInvariant() + "/" + version;
        string copiedPackage = Path.Combine(PackageRoot, packagePath.Replace('/', Path.DirectorySeparatorChar));
        OriginalAsset = Path.Combine(copiedPackage, OriginalRelative.Replace('/', Path.DirectorySeparatorChar));
        SelectedAsset = Path.Combine(copiedPackage, SelectedRelative.Replace('/', Path.DirectorySeparatorChar));
        foreach (string relative in new[] { OriginalRelative, SelectedRelative }.Distinct(StringComparer.Ordinal))
        {
            string source = Path.Combine(cache, relative.Replace('/', Path.DirectorySeparatorChar));
            string destination = Path.Combine(copiedPackage, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        PayloadAsset = Path.Combine(Payload, package + ".dll");
        File.Copy(OriginalAsset, PayloadAsset);
        LibraryKey = package + "/" + version;
        var runtime = new JsonObject { [OriginalRelative] = new JsonObject() };
        Target = new JsonObject { [LibraryKey] = new JsonObject { ["type"] = "package", ["runtime"] = runtime } };
        Library = new JsonObject
        {
            ["type"] = "package", ["path"] = packagePath,
            ["files"] = new JsonArray(new[] { OriginalRelative, SelectedRelative }.Distinct(StringComparer.Ordinal)
                .Select(path => (JsonNode?)JsonValue.Create(path)).ToArray())
        };
        Document = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = new JsonObject { [".NETFramework,Version=v4.7.2"] = Target },
            ["libraries"] = new JsonObject { [LibraryKey] = Library },
            ["packageFolders"] = new JsonObject { [PackageRoot + Path.DirectorySeparatorChar] = new JsonObject() }
        };
        AssetsFile = Path.Combine(Root, "project.assets.json");
        Mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [package] = "netstandard2.0" };
        WriteAssets();
    }

    internal string Root { get; }
    internal string Package { get; }
    internal string PackageRoot { get; }
    internal string Payload { get; }
    internal string PayloadAsset { get; }
    internal string OriginalAsset { get; }
    internal string SelectedAsset { get; }
    internal string OriginalRelative { get; }
    internal string SelectedRelative { get; }
    internal string LibraryKey { get; }
    internal string AssetsFile { get; }
    internal string PolicyPath => Path.Combine(Payload, DesktopDependencyAssetNormalizer.BindingPolicyFileName);
    internal JsonObject Document { get; }
    internal JsonObject Target { get; }
    internal JsonObject Library { get; }
    internal Dictionary<string, string> Mapping { get; }

    internal void Normalize() => DesktopDependencyAssetNormalizer.Normalize(Payload, AssetsFile, "net472", Mapping);
    internal void WriteAssets() => File.WriteAllText(AssetsFile, Document.ToJsonString());
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    internal void CopyPackageAssets(string destinationRoot)
    {
        foreach (string relative in new[] { OriginalRelative, SelectedRelative }.Distinct(StringComparer.Ordinal))
        {
            string source = relative == OriginalRelative ? OriginalAsset : SelectedAsset;
            string destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
    }

    internal static string FindCachedPackage(string package, string version)
    {
        string? configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        string settingsRoot = NuGet.Configuration.SettingsUtility.GetGlobalPackagesFolder(
            NuGet.Configuration.Settings.LoadDefaultSettings(null));
        string defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        foreach (string root in new[] { configured, settingsRoot, defaultRoot }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(root, package.ToLowerInvariant(), version);
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException($"Existing restored package {package}/{version} is required; this test never downloads packages.");
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
