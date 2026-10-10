namespace PowerForge.Tests;

public sealed partial class BoundProjectVersionReleaseTests
{
    [Theory]
    [InlineData("Reference", false)]
    [InlineData("Reference", true)]
    [InlineData("Inherited", false)]
    [InlineData("Inherited", true)]
    [InlineData("Literal", false)]
    [InlineData("Literal", true)]
    public void Execute_CurrentVersionMetadataBindingUsesActualPlannedProject(string versionKind, bool whatIf)
    {
        var spec = CurrentVersionMetadataSpec(versionKind);
        spec.WhatIf = whatIf;
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path);
        var props = File.ReadAllText(PropsPath);

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(result.Projects, project => Assert.Equal("2.0.0", project.NewVersion));
        Assert.Equal(whatIf ? original : original.Replace("<ToolVersion>old</ToolVersion>",
            "<ToolVersion>2.0.0</ToolVersion>", StringComparison.Ordinal), File.ReadAllText(path));
        Assert.Equal(props, File.ReadAllText(PropsPath));
    }

    [Fact]
    public void Execute_DisabledUpdatesKeepCurrentVersionAndMetadataUnchanged()
    {
        var spec = CurrentVersionMetadataSpec("Reference");
        spec.UpdateVersions = false;
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));
        var props = File.ReadAllText(PropsPath);

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Equal(props, File.ReadAllText(PropsPath));
    }

    [Theory]
    [InlineData("Reference")]
    [InlineData("Inherited")]
    public void Execute_CurrentVersionMetadataCannotComposeAConflictingVersion(string versionKind)
    {
        var spec = CurrentVersionMetadataSpec(versionKind);
        spec.VersionBindings![0].Replacement = "7.0.0</ToolVersion><Version>7.0.0</Version><ToolVersion>{Version}";

        AssertAtomicFailure(spec, "version element layout changed");
    }

    private DotNetRepositoryReleaseSpec CurrentVersionMetadataSpec(string versionKind)
    {
        CreateFixture();
        if (versionKind == "Inherited")
            File.WriteAllText(PropsPath, Props.Replace("</PropertyGroup>", "<Version>$(ProductVersion)</Version></PropertyGroup>", StringComparison.Ordinal));
        var path = ProjectPath("Example.Alpha");
        var content = File.ReadAllText(path);
        if (versionKind == "Inherited")
            content = content.Replace("<Version>$(ProductVersion)</Version>", "", StringComparison.Ordinal);
        else if (versionKind == "Literal")
            content = content.Replace("$(ProductVersion)", "2.0.0", StringComparison.Ordinal);
        File.WriteAllText(path, content.Replace("</PropertyGroup>", "<ToolVersion>old</ToolVersion></PropertyGroup>", StringComparison.Ordinal));
        var spec = Spec();
        spec.ExpectedVersion = null;
        spec.VersionBindings = new[] { new ProjectVersionBinding
        {
            Path = "Example.Alpha/Example.Alpha.csproj", Project = "Example.Alpha",
            Pattern = @"(?<=<ToolVersion>)old(?=</ToolVersion>)"
        } };
        return spec;
    }
}
