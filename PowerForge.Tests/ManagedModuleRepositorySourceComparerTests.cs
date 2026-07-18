namespace PowerForge.Tests;

public sealed class ManagedModuleRepositorySourceComparerTests
{
    [Fact]
    public void Local_sources_follow_the_filesystem_case_semantics()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "Feed");
        var differentCase = Path.Combine(root.Path, "feed");
        Directory.CreateDirectory(source);

        var expected = FrameworkCompatibility.GetPathStringComparison(root.Path) == StringComparison.Ordinal
            ? false
            : true;

        Assert.Equal(expected, ManagedModuleRepositorySourceComparer.Equals(source, differentCase));
    }

    [Fact]
    public void File_uri_and_local_path_identify_the_same_source()
    {
        using var source = new TemporaryDirectory();

        Assert.True(ManagedModuleRepositorySourceComparer.Equals(
            new Uri(source.Path).AbsoluteUri,
            source.Path));
    }

    [Fact]
    public void Relative_local_sources_follow_platform_directory_separator_semantics()
    {
        Assert.Equal(FrameworkCompatibility.IsWindows(), ManagedModuleRepositorySourceComparer.Equals(
            "packages/feed",
            @"packages\feed"));
    }

    [Fact]
    public void Remote_source_host_is_case_insensitive_but_path_is_case_sensitive()
    {
        Assert.True(ManagedModuleRepositorySourceComparer.Equals(
            "https://PACKAGES.example.test/Feed/",
            "https://packages.example.test/Feed"));
        Assert.False(ManagedModuleRepositorySourceComparer.Equals(
            "https://packages.example.test/Feed",
            "https://packages.example.test/feed"));
    }
}
