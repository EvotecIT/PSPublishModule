using System;

namespace PowerForge.Tests;

public sealed class ModuleDependencyTests
{
    [Fact]
    public void Constructor_PreservesFourArgumentMetadataSignature()
    {
        var constructor = typeof(ModuleDependency).GetConstructor(new[]
        {
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string)
        });

        Assert.NotNull(constructor);
        var dependency = Assert.IsType<ModuleDependency>(constructor!.Invoke(new object?[]
        {
            "Company.Tools",
            null,
            "1.0.0",
            "2.0.0"
        }));

        Assert.Equal("Company.Tools", dependency.Name);
        Assert.Equal("1.0.0", dependency.MinimumVersion);
        Assert.Equal("2.0.0", dependency.MaximumVersion);
        Assert.True(dependency.MinimumVersionInclusive);
        Assert.True(dependency.MaximumVersionInclusive);
        Assert.Null(dependency.InstallScope);
    }
}
