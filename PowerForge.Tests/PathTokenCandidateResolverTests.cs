namespace PowerForge.Tests;

public sealed class PathTokenCandidateResolverTests
{
    [Fact]
    public void ResolveExistingPaths_MatchesTokensEmbeddedInDirectoryNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-token-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "Packed-v1.0.0")).FullName;
            var second = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "Packed-v1.1.0")).FullName;

            var paths = PathTokenCandidateResolver.ResolveExistingPaths(
                Path.Combine(root, "Artifacts", "Packed-<TagModuleVersionWithPreRelease>"));

            Assert.Equal(
                new[] { first, second }.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
