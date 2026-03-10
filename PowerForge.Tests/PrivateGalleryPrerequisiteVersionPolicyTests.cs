using System;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class PrivateGalleryPrerequisiteVersionPolicyTests
{
    [Theory]
    [InlineData("1.1.1", "1.1.1", true)]
    [InlineData("1.2.0", "1.1.1", true)]
    [InlineData("1.1.1-preview1", "1.1.1", false)]
    [InlineData("1.2.0-preview2", "1.2.0-preview5", false)]
    [InlineData("1.2.0-preview5", "1.2.0-preview5", true)]
    [InlineData("1.2.0-preview6", "1.2.0-preview5", true)]
    [InlineData("1.2.0", "1.2.0-preview5", true)]
    [InlineData("1.0.9", "1.1.1", false)]
    [InlineData("", "1.1.1", false)]
    public void VersionMeetsMinimum_EvaluatesExpectedValues(string versionText, string minimumVersion, bool expected)
    {
        var helperType = typeof(PSPublishModule.RegisterModuleRepositoryCommand).Assembly
            .GetType("PSPublishModule.PrivateGalleryCommandSupport", throwOnError: true);
        var method = helperType!
            .GetMethod("VersionMeetsMinimum", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var actual = (bool)method!.Invoke(null, new object?[] { versionText, minimumVersion })!;

        Assert.Equal(expected, actual);
    }
}
