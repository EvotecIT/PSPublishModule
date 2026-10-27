namespace PowerForge.Tests;

public sealed class DesktopDependencyAssetBuildSpecTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildToStaging_AssetMapWithoutProject_RejectsExistingPayloadInsteadOfIgnoringSelection()
    {
        ModuleBuildSpec spec = CreateSpec();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ModuleBuildPipelineFactory.Create(new NullLogger()).BuildToStaging(spec));

        Assert.Contains("NETDesktopDependencyAssetFrameworks", error.Message);
        Assert.False(File.Exists(Path.Combine(spec.StagingPath!, "Lib", "Default", DesktopDependencyAssetNormalizer.BindingPolicyFileName)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BuildToStaging_ExplicitNoBuildMode_PreservesPayloadWithoutRequiringOrBuildingProject(bool skipBuild, bool refreshOnly)
    {
        ModuleBuildSpec spec = CreateSpec();
        spec.SkipDotNetBuild = skipBuild;
        spec.RefreshManifestOnly = refreshOnly;
        spec.CsprojPath = Path.Combine(_root, "source-not-in-artifact", "Example.csproj");
        string original = File.ReadAllText(Path.Combine(spec.SourcePath, "Example.psm1"));
        byte[] payload = File.ReadAllBytes(Path.Combine(spec.SourcePath, "Lib", "Default", "Example.dll"));

        ModuleBuildResult result = ModuleBuildPipelineFactory.Create(new NullLogger()).BuildToStaging(spec);

        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(result.StagingPath, "Lib", "Default", "Example.dll")));
        Assert.False(File.Exists(Path.Combine(result.StagingPath, "Lib", "Default", DesktopDependencyAssetNormalizer.BindingPolicyFileName)));
        Assert.Equal("netstandard2.0", spec.DesktopDependencyAssetFrameworks["System.Memory"]);
        if (refreshOnly) Assert.Equal(original, File.ReadAllText(Path.Combine(result.StagingPath, "Example.psm1")));
    }

    [Fact]
    public void BuildInPlace_AssetMapWithoutProject_RejectsDirectBuilderCall()
    {
        ModuleBuildSpec spec = CreateSpec();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ModuleBuilderTestDependencies.Create().BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = spec.SourcePath,
                ModuleName = spec.Name,
                DesktopDependencyAssetFrameworks = spec.DesktopDependencyAssetFrameworks,
                DisableBinaryCmdletScan = true,
            }));
        Assert.Contains("NETDesktopDependencyAssetFrameworks", error.Message);
    }

    private ModuleBuildSpec CreateSpec()
    {
        string source = Path.Combine(_root, "source");
        Directory.CreateDirectory(Path.Combine(source, "Lib", "Default"));
        File.Copy(typeof(ModuleBuilder).Assembly.Location, Path.Combine(source, "Lib", "Default", "Example.dll"));
        File.WriteAllText(Path.Combine(source, "Example.psd1"), "@{RootModule='Example.psm1';ModuleVersion='1.0.0';FunctionsToExport=@();CmdletsToExport=@();AliasesToExport=@()}");
        File.WriteAllText(Path.Combine(source, "Example.psm1"), "# Existing prebuilt module entry point.");
        return new ModuleBuildSpec
        {
            Name = "Example", SourcePath = source, StagingPath = Path.Combine(_root, "staged"), Version = "1.0.0",
            ExportAssemblies = new[] { "Example.dll" }, DisableBinaryCmdletScan = true,
            DesktopDependencyAssetFrameworks = new(StringComparer.OrdinalIgnoreCase) { ["System.Memory"] = "netstandard2.0" },
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
