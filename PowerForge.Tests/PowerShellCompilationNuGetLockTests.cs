using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationNuGetLockTests
{
    [Fact]
    public void ExactLockPreparation_CopiesLockAndAddsOnlyMissingDirectReferences()
    {
        using var fixture = Fixture.Create();
        var hash = ValidContentHash(3);
        File.WriteAllText(fixture.LockPath, JsonSerializer.Serialize(new
        {
            version = 1,
            dependencies = new Dictionary<string, object>
            {
                ["net8.0"] = new Dictionary<string, object>
                {
                    ["Exact.Direct"] = new { type = "Direct", requested = "[1.2.3, )", resolved = "1.2.3", contentHash = hash },
                    ["Exact.Transitive"] = new { type = "Transitive", resolved = "4.5.6", contentHash = ValidContentHash(4) }
                }
            }
        }));
        File.WriteAllText(fixture.ProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.SourcePath,
            fixture.OutputPath,
            "ExactLock",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            NuGetLockFilePath = fixture.LockPath
        };

        var lockSha256 = PowerShellCompilationArtifactBuilder.PrepareExactNuGetClosureLock(spec, fixture.ProjectPath);

        Assert.Equal(PowerShellCompilationProjectManifestService.ComputeSha256(fixture.LockPath), lockSha256);
        Assert.Equal(File.ReadAllText(fixture.LockPath), File.ReadAllText(Path.Combine(fixture.Root, "packages.lock.json")));
        var project = XDocument.Load(fixture.ProjectPath);
        var reference = Assert.Single(project.Descendants("PackageReference"));
        Assert.Equal("Exact.Direct", reference.Attribute("Include")?.Value);
        Assert.Equal("1.2.3", reference.Attribute("Version")?.Value);
        Assert.Equal("all", reference.Attribute("PrivateAssets")?.Value);
    }

    [Fact]
    public void ResolvedCatalog_RejectsPackagesOutsideExactTargetLock()
    {
        using var fixture = Fixture.Create();
        var expectedHash = ValidContentHash(5);
        var unexpectedHash = ValidContentHash(6);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "obj"));
        File.WriteAllText(Path.Combine(fixture.Root, "obj", "project.assets.json"), JsonSerializer.Serialize(new
        {
            libraries = new Dictionary<string, object>
            {
                ["Exact.Direct/1.2.3"] = new { type = "package", sha512 = expectedHash },
                ["Unexpected/9.9.9"] = new { type = "package", sha512 = unexpectedHash }
            }
        }));
        File.WriteAllText(fixture.LockPath, JsonSerializer.Serialize(new
        {
            version = 1,
            dependencies = new Dictionary<string, object>
            {
                ["net8.0"] = new Dictionary<string, object>
                {
                    ["Exact.Direct"] = new { type = "Direct", requested = "[1.2.3, )", resolved = "1.2.3", contentHash = expectedHash }
                }
            }
        }));

        var exception = Assert.Throws<InvalidDataException>(() =>
            PowerShellCompilationResolvedPackageCatalog.ReadAndVerify(
                fixture.Root,
                new PowerShellCompilationDependencyGraph(),
                fixture.LockPath));

        Assert.Contains("differs from its exact target", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidContentHash(byte value)
        => Convert.ToBase64String(Enumerable.Repeat(value, 64).ToArray());

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root)
        {
            Root = root;
            SourcePath = Path.Combine(root, "Source.ps1");
            OutputPath = Path.Combine(root, "out");
            ProjectPath = Path.Combine(root, "Generated.csproj");
            LockPath = Path.Combine(root, "reviewed.packages.lock.json");
            File.WriteAllText(SourcePath, "return 42");
        }

        internal string Root { get; }
        internal string SourcePath { get; }
        internal string OutputPath { get; }
        internal string ProjectPath { get; }
        internal string LockPath { get; }

        internal static Fixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeNuGetLockTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new Fixture(root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
