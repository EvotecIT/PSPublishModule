using System.Reflection;

namespace PowerForge.Tests;

public sealed class AppleReleaseSourceTrustBuildSettingReferenceTests
{
    [Fact]
    public void Capture_MarksFailuresForStructuralClassification()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleSourceTrustMarker",
            Guid.NewGuid().ToString("N")));
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseSourceTrustService().Capture(
                    root.FullName,
                    Path.Combine(root.FullName, "powerforge.release.json")));

            Assert.StartsWith("Apple source trust validation failed:", exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                AppleReleaseFailureClassifier.Classify(exception.Message),
                diagnostic => diagnostic.Code == "APPLE_SOURCE_TRUST");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ValidateUnclassifiedBuildSettingReferences_AllowsStandardBuildProductsDirectory()
    {
        var exception = Record.Exception(() => ValidateReferences(
            "TEST_HOST",
            "$(BUILT_PRODUCTS_DIR)/Example.app/Example"));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateUnclassifiedBuildSettingReferences_AllowsPowerForgeSourceRevision()
    {
        var exception = Record.Exception(() => ValidateReferences(
            "INFOPLIST_FILE contents",
            "$(POWERFORGE_SOURCE_REVISION)"));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateUnclassifiedBuildSettingReferences_StillRejectsHostEnvironmentReferences()
    {
        var exception = Assert.Throws<TargetInvocationException>(() =>
            ValidateReferences("TEST_HOST", "$(HOME)/Example.app/Example"));
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);

        Assert.Contains("unapproved host or environment reference", inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateReferences(string key, string value)
    {
        var method = typeof(AppleReleaseSourceTrustService).GetMethod(
            "ValidateUnclassifiedBuildSettingReferences",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(null, new object?[] { key, value, "PBX build settings", null });
    }
}
