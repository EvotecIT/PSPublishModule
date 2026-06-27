namespace PowerForge.Tests;

public sealed class ModuleStateVersionPolicyTests
{
    [Fact]
    public void Parse_ExactVersionRequiresSameNormalizedVersion()
    {
        var policy = ModuleStateVersionPolicy.Parse("=2.38");

        Assert.True(policy.IsSatisfiedBy("2.38.0"));
        Assert.False(policy.IsSatisfiedBy("2.39.0"));
    }

    [Fact]
    public void Parse_RangeAcceptsVersionWithinBounds()
    {
        var policy = ModuleStateVersionPolicy.Parse(">=2.36.0 <2.39.0");

        Assert.True(policy.IsSatisfiedBy("2.36.0"));
        Assert.True(policy.IsSatisfiedBy("2.38.1"));
        Assert.False(policy.IsSatisfiedBy("2.39.0"));
    }

    [Fact]
    public void Parse_ExclusiveMinimumRejectsBoundary()
    {
        var policy = ModuleStateVersionPolicy.Parse(">2.36.0");

        Assert.False(policy.IsSatisfiedBy("2.36.0"));
        Assert.True(policy.IsSatisfiedBy("2.36.1"));
    }

    [Fact]
    public void IsSatisfiedBy_RejectsPrereleaseUnlessAllowed()
    {
        var stableOnly = ModuleStateVersionPolicy.Parse(">=2.38.0");
        var prereleaseAllowed = ModuleStateVersionPolicy.Parse(">=2.38.0-preview1", allowPrerelease: true);

        Assert.False(stableOnly.IsSatisfiedBy("2.39.0-preview1"));
        Assert.True(prereleaseAllowed.IsSatisfiedBy("2.39.0-preview1"));
        Assert.False(prereleaseAllowed.IsSatisfiedBy("2.38.0-alpha"));
    }

    [Fact]
    public void IsSatisfiedBy_AllowsExactPrereleasePolicyToMatchItself()
    {
        var policy = ModuleStateVersionPolicy.Parse("=1.2.0-preview1");

        Assert.True(policy.IsSatisfiedBy("1.2.0-preview1"));
        Assert.False(policy.IsSatisfiedBy("1.2.0-preview2"));
    }

    [Fact]
    public void IsSatisfiedBy_SortsMixedPrereleaseIdentifiersNaturally()
    {
        var policy = ModuleStateVersionPolicy.Parse(">=1.2.0-preview9", allowPrerelease: true);

        Assert.True(policy.IsSatisfiedBy("1.2.0-preview10"));
        Assert.False(policy.IsSatisfiedBy("1.2.0-preview8"));
    }

    [Fact]
    public void Parse_RangeWithPrereleaseBoundAllowsPrereleaseByDefault()
    {
        var policy = ModuleStateVersionPolicy.Parse(">=1.2.0-preview1 <1.2.0");

        Assert.True(policy.IsSatisfiedBy("1.2.0-preview2"));
        Assert.False(policy.IsSatisfiedBy("1.2.0"));
    }
}
