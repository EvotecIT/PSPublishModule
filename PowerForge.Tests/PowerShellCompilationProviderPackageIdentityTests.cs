namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData(false, "Valid..Package", "1.2.3", "valid NuGet PackageId")]
    [InlineData(true, "Valid..Package", "1.2.3", "valid NuGet PackageId")]
    [InlineData(false, "Valid.Package", "1.+2.3", "three-part")]
    [InlineData(false, "Valid.Package", "1. 2.3", "three-part")]
    [InlineData(false, "Valid.Package", "1.2.-0", "three-part")]
    [InlineData(true, "Valid.Package", "1.+2.3", "three-part")]
    [InlineData(true, "Valid.Package", "1. 2.3", "three-part")]
    [InlineData(true, "Valid.Package", "1.2.-0", "three-part")]
    public void ProviderPackage_RejectsMalformedNuGetIdentityBeforeReplacement(bool dependency, string id, string version, string diagnostic)
    {
        using var fixture = ProviderFixture.Create();
        if (dependency)
            fixture.Manifest.Dependencies = new[] { new PowerShellCompilationProviderDependency { PackageId = id, Version = version, ContentHash = "sha512-fixture" } };
        else
        {
            fixture.Manifest.PackageId = id;
            fixture.Manifest.PackageVersion = version;
        }
        var output = fixture.PackagePath("provider.nupkg");
        File.WriteAllText(output, "previous package");

        var error = Assert.Throws<InvalidOperationException>(() => fixture.BuildPackage("provider.nupkg"));

        Assert.Contains(diagnostic, error.Message, StringComparison.Ordinal);
        Assert.Equal("previous package", File.ReadAllText(output));
        Assert.Empty(Directory.GetFiles(fixture.RootPath, "*.tmp"));
    }
}
