namespace PowerForge.Tests;

public sealed partial class BoundProjectVersionReleaseTests
{
    [Theory]
    [InlineData("ProductVersion", "7.0.0")]
    [InlineData("ImportDirectoryBuildProps", "false")]
    public void Execute_UnusedTargetPropertiesDoNotOverrideEvaluatedSharedOwnership(string property, string value)
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path).Replace("</Project>",
            $"<Target Name=\"Unused\"><PropertyGroup><{property}>{value}</{property}></PropertyGroup></Target></Project>", StringComparison.Ordinal);
        File.WriteAllText(path, original);

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnusedSharedTargetDefinitionDoesNotMakeEvaluationOwnershipAmbiguous()
    {
        CreateFixture();
        File.WriteAllText(PropsPath, Props.Replace("</Project>",
            "<Target Name=\"Unused\"><PropertyGroup><ProductVersion>7.0.0</ProductVersion></PropertyGroup></Target></Project>", StringComparison.Ordinal));
        var spec = Spec();
        spec.VersionBindings![0].Pattern = @"(?<=<ProductVersion>)2\.0\.0(?=</ProductVersion>)";
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Contains("<ProductVersion>7.0.0</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TrailingHyphenSharedPropertyNameRemainsBound()
    {
        const string property = "Release-";
        CreateFixture();
        File.WriteAllText(PropsPath, Props.Replace("ProductVersion", property, StringComparison.Ordinal));
        foreach (var name in new[] { "Example.Alpha", "Example.Beta" })
            File.WriteAllText(ProjectPath(name), File.ReadAllText(ProjectPath(name)).Replace("ProductVersion", property, StringComparison.Ordinal));
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));
        var spec = Spec();
        spec.VersionBindings![0].Pattern = @"(?<=<" + property + @">)2\.0\.0(?=</" + property + ">)";

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Contains("<" + property + ">2.0.1</" + property + ">", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Version", false)]
    [InlineData("PackageVersion", false)]
    [InlineData("Version", true)]
    [InlineData("PackageVersion", true)]
    public void Execute_ComposedReferenceInLiteralProjectFailsBeforeAnySourceChanges(string tag, bool alreadyCurrent)
    {
        CreateFixture(tag);
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(path, File.ReadAllText(path).Replace("$(ProductVersion)", alreadyCurrent ? "2.0.1" : "2.0.0", StringComparison.Ordinal));
        var spec = Spec();
        AddProjectBinding(spec, "<" + tag + ">[^<]*</" + tag + ">",
            "<" + tag + ">$(MissingVersion)</" + tag + "><ToolVersion>{Version}</ToolVersion>");

        AssertAtomicFailure(spec, "does not match resolved version");
    }

    [Fact]
    public void Execute_ComposedNonemptyPackageVersionCannotBypassEmptyOriginalValidation()
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(path, File.ReadAllText(path).Replace("</PropertyGroup>",
            "<PackageVersion></PackageVersion></PropertyGroup>", StringComparison.Ordinal));
        var spec = Spec();
        AddProjectBinding(spec, "<PackageVersion></PackageVersion>", "<PackageVersion>{Version}-other</PackageVersion>");

        AssertAtomicFailure(spec, "does not match resolved version");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreserveReferences_AdditionalSdkRetainsOrdinaryLiteralUpdate(bool sdkElement)
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        var content = File.ReadAllText(path);
        File.WriteAllText(path, sdkElement
            ? content.Replace("<PropertyGroup>", "<Sdk Name=\"Microsoft.NET.Sdk.Web\" /><PropertyGroup>", StringComparison.Ordinal)
            : content.Replace("Microsoft.NET.Sdk\"", "Microsoft.NET.Sdk;Microsoft.NET.Sdk.Web\"", StringComparison.Ordinal));

        var original = File.ReadAllText(path);
        var updated = CsprojVersionEditor.UpdateVersionText(original, "2.0.1", out _);

        Assert.Equal(updated, PreserveVersionReference(original, updated));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreserveReferences_ComposedAdditionalSdkIsRejected(bool sdkElement)
    {
        CreateFixture();
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));
        var updated = CsprojVersionEditor.UpdateVersionText(original, "2.0.1", out _);
        updated = sdkElement
            ? updated.Replace("<PropertyGroup>", "<Sdk Name=\"Microsoft.NET.Sdk.Web\" /><PropertyGroup>", StringComparison.Ordinal)
            : updated.Replace("Sdk=\"Microsoft.NET.Sdk\"", "Sdk=\"Microsoft.NET.Sdk;Microsoft.NET.Sdk.Web\"", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => PreserveVersionReference(original, updated));
        Assert.Contains("version property ownership", error.Message, StringComparison.Ordinal);
    }

    private string PreserveVersionReference(string original, string updated) => BoundProjectVersionService.PreserveReferences(
        _root, ProjectPath("Example.Alpha"), original, updated, CsprojVersionEditor.UpdateVersionText(original, "2.0.1", out _), "2.0.1",
        new[] { new ProjectVersionBindingFileUpdate(new RepositoryTextFileUpdate(PropsPath, Props,
            Props.Replace("2.0.0", "2.0.1", StringComparison.Ordinal)), "Directory.Build.props", 1) }, Spec().VersionBindings);

    [Theory]
    [InlineData("VersionPrefix")]
    [InlineData("VersionSuffix")]
    public void Execute_ComposedPrefixOrSuffixCannotChangePlannedPackageVersion(string tag)
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(path, File.ReadAllText(path).Replace("<Version>$(ProductVersion)</Version>",
            "<VersionPrefix>2.0.0</VersionPrefix><VersionSuffix></VersionSuffix>", StringComparison.Ordinal));
        var spec = Spec();
        AddProjectBinding(spec, "<" + tag + ">[^<]*</" + tag + ">",
            "<" + tag + ">$(MissingVersion)</" + tag + "><ToolVersion>{Version}</ToolVersion>");

        AssertAtomicFailure(spec, "does not match resolved version");
    }

    [Fact]
    public void Execute_EmptyPackageVersionCanBeFilledWithTheResolvedVersion()
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(path, File.ReadAllText(path).Replace("</PropertyGroup>",
            "<PackageVersion></PackageVersion></PropertyGroup>", StringComparison.Ordinal));
        var spec = Spec();
        AddProjectBinding(spec, "<PackageVersion></PackageVersion>", "<PackageVersion>{Version}</PackageVersion>");

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<Version>$(ProductVersion)</Version>", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("<PackageVersion>2.0.1</PackageVersion>", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_EditingACommentDoesNotShiftTheActualVersionConsumer()
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(path, File.ReadAllText(path).Replace("<Version>",
            "<!-- <Version>$(UnusedVersion)</Version> --><Version>", StringComparison.Ordinal));
        var spec = Spec();
        AddProjectBinding(spec, @"<!-- <Version>[^<]*</Version> -->", "<ToolVersion>{Version}</ToolVersion>");

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<Version>$(ProductVersion)</Version>", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("<ToolVersion>2.0.1</ToolVersion>", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
