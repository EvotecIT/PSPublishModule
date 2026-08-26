using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_RefusesToReplaceDirectoryContainingInputSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "Foo");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Foo.psm1");
        const string source = "function Get-Value { return 1 }";
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                sourcePath,
                root,
                "Foo",
                PowerShellCompilationArtifactKind.Library,
                PowerShellCompilationMode.Strict));

            Assert.False(result.Succeeded);
            Assert.Contains("contains the input source", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(source, File.ReadAllText(sourcePath));
            Assert.Empty(Directory.EnumerateDirectories(root, ".*.artifact-backup-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_RefusesCaseAliasedSourceReplacementOnCaseInsensitivePlatforms()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (PowerShellCompilationPathSafety.GetPathComparison(root) != StringComparison.OrdinalIgnoreCase) return;
            var sourceDirectory = Path.Combine(root, "Foo");
            Directory.CreateDirectory(sourceDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "input.ps1");
            const string source = "function Get-Value { return 1 }";
            File.WriteAllText(sourcePath, source);
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                sourcePath,
                root,
                "foo",
                PowerShellCompilationArtifactKind.Library,
                PowerShellCompilationMode.Strict));

            Assert.False(result.Succeeded);
            Assert.Contains("contains the input source", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(source, File.ReadAllText(sourcePath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PathComparison_UsesTheMacVolumeCaseSensitivity()
    {
        Assert.Equal(StringComparison.OrdinalIgnoreCase, PowerShellCompilationPathSafety.GetPathComparison(isWindows: true, isMacOS: false, isCaseSensitiveFileSystem: true));
        Assert.Equal(StringComparison.OrdinalIgnoreCase, PowerShellCompilationPathSafety.GetPathComparison(isWindows: false, isMacOS: true, isCaseSensitiveFileSystem: false));
        Assert.Equal(StringComparison.Ordinal, PowerShellCompilationPathSafety.GetPathComparison(isWindows: false, isMacOS: true, isCaseSensitiveFileSystem: true));
        Assert.Equal(StringComparison.Ordinal, PowerShellCompilationPathSafety.GetPathComparison(isWindows: false, isMacOS: false, isCaseSensitiveFileSystem: false));
    }

    [Fact]
    public void Build_ReplacesTheCompleteArtifactShapeWithoutLeavingPriorFiles()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-TypedValue {
                param([int] $Value)
                return $Value
            }
            function Get-DynamicValue {
                param([string] $Path)
                return Get-Item -LiteralPath $Path
            }
            """);
        var librarySpec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ShapeReplacement",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Hybrid);
        var library = new PowerShellCompilationArtifactBuilder().Build(librarySpec);
        Assert.True(library.Succeeded, library.Error + Environment.NewLine + library.BuildOutput);
        var previousLibraryPath = library.ArtifactPath!;
        Assert.True(File.Exists(previousLibraryPath));

        var moduleSpec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ShapeReplacement",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);
        var module = new PowerShellCompilationArtifactBuilder().Build(moduleSpec);

        Assert.True(module.Succeeded, module.Error + Environment.NewLine + module.BuildOutput);
        Assert.False(File.Exists(previousLibraryPath));
        Assert.True(Directory.Exists(Path.Combine(fixture.OutputPath, "PowerForge.ShapeReplacement")));
        Assert.All(module.Manifest!.Files, file => Assert.True(File.Exists(file.Path), file.Path));
        Assert.Empty(Directory.EnumerateDirectories(fixture.OutputPath, ".PowerForge.ShapeReplacement.artifact-*"));
    }

    [Fact]
    public void Build_ReusesAbandonedPublicationLockFile()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        const string artifactName = "PowerForge.AbandonedLock";
        var lockPath = Path.Combine(fixture.OutputPath, "." + artifactName + ".artifact-publish.lock");
        File.WriteAllText(lockPath, "abandoned");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            artifactName,
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public void Build_RestoresPreviousArtifactSetWhenDurableCommitFails()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AtomicRollback",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);
        var first = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(first.Succeeded, first.Error + Environment.NewLine + first.BuildOutput);
        var originalArtifactHash = Hash(first.ArtifactPath!);
        var originalManifestHash = Hash(first.ManifestPath!);
        File.WriteAllText(fixture.ScriptPath, "function Get-Value { return 2 }");

        PowerShellCompilationBuildResult failed;
        using (new FileStream(first.ManifestPath!, FileMode.Open, FileAccess.Read, FileShare.None))
            failed = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(failed.Succeeded);
        Assert.Contains("previous durable artifact set was restored", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalArtifactHash, Hash(first.ArtifactPath!));
        Assert.Equal(originalManifestHash, Hash(first.ManifestPath!));
        Assert.Empty(Directory.EnumerateDirectories(fixture.OutputPath, ".PowerForge.AtomicRollback.artifact-*"));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
