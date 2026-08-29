using System.Reflection;

namespace PowerForge.Tests;

public sealed class AppleReleaseSourceTrustInfoPlistTests
{
    [Theory]
    [InlineData("PRODUCT_BUNDLE_PACKAGE_TYPE")]
    [InlineData("MACOSX_DEPLOYMENT_TARGET")]
    [InlineData("IPHONEOS_DEPLOYMENT_TARGET")]
    [InlineData("TVOS_DEPLOYMENT_TARGET")]
    [InlineData("WATCHOS_DEPLOYMENT_TARGET")]
    [InlineData("XROS_DEPLOYMENT_TARGET")]
    [InlineData("DRIVERKIT_DEPLOYMENT_TARGET")]
    public void ValidateInfoPlistBuildSettingReferences_AllowsStandardProductAndDeploymentReferences(
        string reference)
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.InfoPlist.");
        try
        {
            var plistPath = Path.Combine(root.FullName, "Info.plist");
            File.WriteAllText(plistPath, $"<plist><string>$({reference})</string></plist>");

            var exception = Record.Exception(() => ValidateInfoPlist(root.FullName, plistPath));

            Assert.Null(exception);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ValidateInfoPlistBuildSettingReferences_StillRejectsHostEnvironmentReferences()
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.InfoPlist.");
        try
        {
            var plistPath = Path.Combine(root.FullName, "Info.plist");
            File.WriteAllText(plistPath, "<plist><string>$(HOME)</string></plist>");

            var exception = Assert.Throws<TargetInvocationException>(
                () => ValidateInfoPlist(root.FullName, plistPath));
            var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("unapproved host or environment reference", inner.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void ValidateInfoPlist(string repositoryRoot, string plistPath)
    {
        var method = typeof(AppleReleaseSourceTrustService).GetMethod(
            "ValidateInfoPlistBuildSettingReferences",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(
            new AppleReleaseSourceTrustService(),
            new object[] { repositoryRoot, plistPath, "PBX build settings", false });
    }
}
