using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleRepositoryPathResolverTests
{
    [Fact]
    public void ResolveLocalFolder_normalizes_relative_folder_feeds()
    {
        var resolved = ManagedModuleRepositoryPathResolver.ResolveLocalFolder("packages/feed");

        Assert.Equal(
            Path.GetFullPath(Path.Combine("packages", "feed")),
            resolved,
            ModuleStatePathIdentity.Comparer);
    }

    [Fact]
    public void ResolveLocalFolder_uses_platform_native_backslash_semantics()
    {
        var root = Path.Combine(Path.GetTempPath(), "managed-repository-path");
        var literalBackslashPath = ManagedModuleRepositoryPathResolver.ResolveLocalFolder(
            Path.Combine(root, @"feed\prod"));
        var directoryPath = ManagedModuleRepositoryPathResolver.ResolveLocalFolder(
            Path.Combine(root, "feed", "prod"));

        Assert.Equal(
            OperatingSystem.IsWindows(),
            ModuleStatePathIdentity.Equals(literalBackslashPath, directoryPath));
        if (!OperatingSystem.IsWindows())
            Assert.EndsWith(@"feed\prod", literalBackslashPath, StringComparison.Ordinal);
    }
}
