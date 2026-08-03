namespace PowerForge.Tests;

public sealed class DocumentationBuildResultCompatibilityTests
{
    [Fact]
    public void PublicEightArgumentConstructor_RemainsAvailableForBinaryCallers()
    {
        var constructor = typeof(DocumentationBuildResult).GetConstructor(
        [
            typeof(bool),
            typeof(string),
            typeof(string),
            typeof(bool),
            typeof(int),
            typeof(int),
            typeof(string),
            typeof(string)
        ]);

        Assert.NotNull(constructor);
        var result = Assert.IsType<DocumentationBuildResult>(constructor!.Invoke(
        [
            true,
            "docs",
            "readme",
            true,
            0,
            1,
            "module-help.xml",
            null
        ]));
        Assert.Equal(new[] { "module-help.xml" }, result.ExternalHelpFilePaths);
    }
}
