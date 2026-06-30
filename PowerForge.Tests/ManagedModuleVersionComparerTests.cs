using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleVersionComparerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0+build.5", 0)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.0-preview.1", 1)]
    [InlineData("1.0.0-preview.10", "1.0.0-preview.2", 1)]
    [InlineData("1.0.0-preview.1", "1.0.0-preview.alpha", -1)]
    [InlineData("1.0.0-preview.alpha.1", "1.0.0-preview.alpha", 1)]
    public void Compare_orders_semantic_versions(string left, string right, int expectedSign)
    {
        var comparison = ManagedModuleVersionComparer.Instance.Compare(left, right);

        Assert.Equal(expectedSign, Math.Sign(comparison));
    }
}
