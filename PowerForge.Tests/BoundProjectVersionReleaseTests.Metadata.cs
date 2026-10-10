namespace PowerForge.Tests;

public sealed partial class BoundProjectVersionReleaseTests
{
    [Fact]
    public void Execute_ImportedImportTaskDoesNotDisableSharedOwnership()
    {
        CreateFixture();
        File.WriteAllText(PropsPath, Props.Replace("</Project>",
            "<Target Name=\"Unused\"><Import /></Target></Project>", StringComparison.Ordinal));
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path);

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<ItemGroup><ProductVersion Include=\"asset-2.0.0\" /></ItemGroup>", "\n")]
    [InlineData("<ItemGroup><ProductVersion Include=\"asset-2.0.0\" /></ItemGroup>", "\r\n")]
    [InlineData("<!-- <ProductVersion>asset-2.0.0</ProductVersion> -->", "\n")]
    [InlineData("<Target Name=\"Unused\"><ProductVersion Text=\"asset-2.0.0\" /></Target>", "\r\n")]
    public void Execute_ImportedNonPropertyBindingRetainsOrdinaryVersionUpdate(string elements, string newline)
    {
        CreateFixture();
        var originalProps = Props.Replace("</Project>", newline + elements + newline + "</Project>", StringComparison.Ordinal);
        File.WriteAllText(PropsPath, originalProps);
        var spec = Spec();
        spec.VersionBindings = new[] { new ProjectVersionBinding
        {
            Path = "Directory.Build.props", Project = "Example.Alpha", Pattern = @"(?<=asset-)2\.0\.0"
        } };

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(originalProps.Replace("asset-2.0.0", "asset-2.0.1", StringComparison.Ordinal), File.ReadAllText(PropsPath));
        Assert.All(result.Projects, project => Assert.Contains("<Version>2.0.1</Version>", File.ReadAllText(project.CsprojPath), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_SharedPackageIdentityRefreshIncludesUneditedProjects(bool literalConsumer)
    {
        CreateFixture();
        File.WriteAllText(PropsPath, Props.Replace("</PropertyGroup>",
            "<PackageId>$(MSBuildProjectName).2.0.0</PackageId></PropertyGroup>", StringComparison.Ordinal));
        var alpha = ProjectPath("Example.Alpha");
        var originalAlpha = File.ReadAllText(alpha);
        if (literalConsumer)
            File.WriteAllText(ProjectPath("Example.Beta"), File.ReadAllText(ProjectPath("Example.Beta")).Replace(
                "$(ProductVersion)", "2.0.0", StringComparison.Ordinal));
        var spec = Spec();
        spec.VersionBindings = spec.VersionBindings!.Concat(new[] { new ProjectVersionBinding
        {
            Path = "Directory.Build.props", Project = "Example.Alpha",
            Pattern = @"(?<=<PackageId>\$\(MSBuildProjectName\)\.)2\.0\.0(?=</PackageId>)"
        } }).ToArray();

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(originalAlpha, File.ReadAllText(alpha));
        Assert.All(result.Projects, project => Assert.Equal(project.ProjectName + ".2.0.1", project.PackageId));
        Assert.All(result.Projects, project => Assert.Equal("2.0.1", project.NewVersion));
    }

    [Theory]
    [InlineData("<ItemGroup><ProductVersion Include=\"asset\" /></ItemGroup>")]
    [InlineData("<Target Name=\"Unused\"><ProductVersion /></Target>")]
    [InlineData("<ItemGroup><ImportDirectoryBuildProps Include=\"asset\" /></ItemGroup>")]
    public void Execute_NonPropertyElementsDoNotChangeSharedOwnership(string elements)
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path).Replace("</Project>", elements + "</Project>", StringComparison.Ordinal);
        File.WriteAllText(path, original);

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<ItemGroup><ProductVersion Include=\"asset-{Version}\" /></ItemGroup>")]
    [InlineData("<Target Name=\"Unused\"><ProductVersion Text=\"{Version}\" /></Target>")]
    public void Execute_ComposedNonPropertyElementsDoNotChangeSharedOwnership(string elements)
    {
        CreateFixture();
        var spec = Spec();
        AddProjectBinding(spec, "</Project>", elements + "</Project>");

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<Version>$(ProductVersion)</Version>", File.ReadAllText(ProjectPath("Example.Alpha")), StringComparison.Ordinal);
        Assert.Contains(elements.Replace("{Version}", "2.0.1", StringComparison.Ordinal), File.ReadAllText(ProjectPath("Example.Alpha")), StringComparison.Ordinal);
    }
}
