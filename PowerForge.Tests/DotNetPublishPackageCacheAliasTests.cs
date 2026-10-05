using System.IO.Compression;
using System.Reflection;
using NuGet.Packaging;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPackageCacheAliasTests
{
    [CacheAliasTheory]
    [InlineData("valid", true)]
    [InlineData("changed-file", false)]
    [InlineData("changed-archive", false)]
    [InlineData("linked-file", false)]
    [InlineData("linked-package", false)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void RelocatedCache_VerifiesContentAndRejectsLinksInsidePackages(string scenario, bool expected)
    {
        string root = Directory.CreateTempSubdirectory("pf-cache-alias-").FullName;
        string alias = Path.Combine(root, "cache-alias");
        string? nestedLink = null;
        try
        {
            string cacheRoot = Directory.CreateDirectory(Path.Combine(root, "physical-cache")).FullName;
            string package = Directory.CreateDirectory(Path.Combine(cacheRoot, "example", "1.0.0")).FullName;
            string input = Path.Combine(package, "build", "example.props");
            Directory.CreateDirectory(Path.GetDirectoryName(input)!);
            File.WriteAllText(input, "<Project />");
            string archivePath = Path.Combine(package, "example.1.0.0.nupkg");
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(input, "build/example.props");
            string hash;
            using (var reader = new PackageArchiveReader(archivePath))
                hash = reader.GetContentHash(CancellationToken.None);
            Directory.CreateSymbolicLink(alias, cacheRoot);

            if (scenario == "changed-file")
                File.WriteAllText(input, "<Project><Target Name=\"Injected\" /></Project>");
            if (scenario == "changed-archive")
                File.AppendAllText(archivePath, "changed");
            if (scenario == "linked-file")
            {
                string outside = Path.Combine(root, "outside.props");
                File.Copy(input, outside);
                File.Delete(input);
                File.CreateSymbolicLink(input, outside);
                nestedLink = input;
            }
            if (scenario == "linked-package")
            {
                string outside = Path.Combine(root, "outside-package");
                Directory.Move(package, outside);
                Directory.CreateSymbolicLink(package, outside);
                nestedLink = package;
            }

            Type runner = typeof(DotNetPublishPipelineRunner);
            Type catalogType = runner.GetNestedType("VerifiedPackageInputCatalog", BindingFlags.NonPublic)!;
            Type cacheType = runner.GetNestedType("VerifiedPackageArchiveCache", BindingFlags.NonPublic)!;
            using var archives = (IDisposable)Activator.CreateInstance(cacheType, nonPublic: true)!;
            string aliasArchive = Path.Combine(alias, "example", "1.0.0", "example.1.0.0.nupkg");
            var hashes = new Dictionary<string, string> { ["example|1.0.0"] = hash };
            var archivePaths = new Dictionary<string, string> { ["example|1.0.0"] = aliasArchive };
            object catalog = Activator.CreateInstance(catalogType,
                BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                args: [new[] { alias }, hashes, archives, archivePaths, Array.Empty<string>()], culture: null)!;
            string aliasInput = Path.Combine(alias, "example", "1.0.0", "build", "example.props");
            MethodInfo verify = catalogType.GetMethod("TryVerify", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object?[] verifyArguments = [aliasInput, null];
            Assert.Equal(expected, (bool)verify.Invoke(catalog, verifyArguments)!);
            Assert.True((bool)verifyArguments[1]!);

            MethodInfo controlled = catalogType.GetMethod("TrySetControlledBuildInputs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object?[] controlledArguments = [new[] { aliasInput }, null];
            Assert.Equal(expected, (bool)controlled.Invoke(catalog, controlledArguments)!);
            if (!expected)
                Assert.NotNull(controlledArguments[1]);
        }
        finally
        {
            // Unlink directory aliases explicitly before recursively removing the test data.
            if (nestedLink is not null)
            {
                if (Directory.Exists(nestedLink)) Directory.Delete(nestedLink);
                else File.Delete(nestedLink);
            }
            if (Directory.Exists(alias)) Directory.Delete(alias);
            Directory.Delete(root, recursive: true);
        }
    }
}

internal sealed class CacheAliasTheoryAttribute : TheoryAttribute
{
    public CacheAliasTheoryAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Unix cache-alias regression; Windows link creation requires separate host qualification.";
    }
}
