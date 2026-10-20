using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationGitMetadataTests
{
    [Theory]
    [InlineData(true, PowerShellCompilationResourceMode.Declared)]
    [InlineData(false, PowerShellCompilationResourceMode.Declared)]
    [InlineData(true, PowerShellCompilationResourceMode.CompleteModule)]
    [InlineData(false, PowerShellCompilationResourceMode.CompleteModule)]
    public void Resolve_AdministrativeGitEntriesDoNotEnterResourceGraph(bool metadataDirectory, PowerShellCompilationResourceMode resourceMode)
    {
        using var fixture = new GitMetadataFixture();
        var metadata = fixture.Write(metadataDirectory ? ".git/powerforge/coordinated-release/release.json.lock" : ".git", "git metadata");
        fixture.Write("Vendor/.GIT/config", "nested git metadata");
        fixture.Write(".gitignore", "out/");
        fixture.Write(".config/settings.json", "authored");
        fixture.Write(".github/workflows/build.yml", "authored workflow");
        fixture.Write("Data/value.lock", "authored lock-named payload");
        using var held = new FileStream(metadata, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var resolved = fixture.Resolve();
        var planner = new PowerShellCompilationDependencyPlanner();
        var graph = planner.AnalyzeGraph(resolved, resourceMode: resourceMode);
        var dependencies = planner.Analyze(resolved, resourceMode: resourceMode);
        Assert.DoesNotContain(dependencies, dependency => dependency.RelativePath.Split('/').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(graph.Nodes, node => node.Identity.Source.Replace('\\', '/').Split('/').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)));
        foreach (var authored in new[] { ".gitignore", ".config/settings.json", ".github/workflows/build.yml", "Data/value.lock" })
        {
            var dependency = Assert.Single(dependencies, dependency => dependency.RelativePath == authored);
            Assert.Equal(resourceMode == PowerShellCompilationResourceMode.CompleteModule,
                dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent);
        }
        Assert.Equal(graph.LockSha256, planner.AnalyzeGraph(resolved, resourceMode: resourceMode).LockSha256);
        fixture.Write(".config/settings.json", "changed authored content");
        Assert.NotEqual(graph.LockSha256, planner.AnalyzeGraph(resolved, resourceMode: resourceMode).LockSha256);
    }

    [Fact]
    public void Resolve_AuthoredResourceLockStillFailsClosed()
    {
        using var fixture = new GitMetadataFixture();
        var authored = fixture.Write("Data/value.lock", "authored");
        using var held = new FileStream(authored, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => fixture.Resolve());
    }

    [Fact]
    public void Resolve_ManifestDirectoryRemainsAnExplicitRequiredContract()
    {
        using var fixture = new GitMetadataFixture("Assets");
        var declared = fixture.Write("Assets/.git/config", "explicitly declared payload");
        using var held = new FileStream(declared, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => fixture.Resolve());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Analyze_ExplicitGitMetadataIncludeIsRejectedForModulesAndScripts(bool module)
    {
        using var fixture = new GitMetadataFixture();
        fixture.Write(".git/config", "metadata");
        var input = module ? fixture.Resolve() : new PowerShellCompilationInputResolver().Resolve(
            fixture.Write("Tool.ps1", "'ok'"), PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict);
        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationDependencyPlanner().Analyze(
            input, includeResource: new[] { ".git/config" }));
        Assert.Contains("Git metadata", exception.Message, StringComparison.Ordinal);
    }

    private sealed class GitMetadataFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge Git Metadata Tests", Guid.NewGuid().ToString("N"));

        internal GitMetadataFixture(string? fileList = null)
        {
            Write("Demo.psm1", "function Get-Value { 1 }");
            Write("Demo.psd1", "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0'" +
                (fileList is null ? "" : $"; FileList = @('{fileList}')") + " }");
        }

        internal string Write(string relative, string content)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        internal PowerShellCompilationResolvedInput Resolve() => new PowerShellCompilationInputResolver().Resolve(
            _root, PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
