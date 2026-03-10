using System;
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
        var actual = PSPublishModule.PrivateGalleryCommandSupport.VersionMeetsMinimum(versionText, minimumVersion);

        Assert.Equal(expected, actual);
    }
}
