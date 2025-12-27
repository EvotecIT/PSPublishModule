using System;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleBuilderIsCoreTests
{
    [Theory]
    [InlineData("net472", false)]
    [InlineData("net48", false)]
    [InlineData("netstandard2.0", true)]
    [InlineData("netcoreapp3.1", true)]
    [InlineData("net5.0", true)]
    [InlineData("net8.0", true)]
    [InlineData("net10.0", true)]
    [InlineData("net10.0-windows", true)]
    public void IsCore_IsFutureProof(string tfm, bool expected)
    {
        var method = typeof(ModuleBuilder).GetMethod("IsCore", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var actual = (bool)method!.Invoke(null, new object[] { tfm })!;
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void IsCore_HandlesEmptyInputs(string? tfm)
    {
        var method = typeof(ModuleBuilder).GetMethod("IsCore", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var actual = (bool)method!.Invoke(null, new object?[] { tfm })!;
        Assert.False(actual);
    }
}
