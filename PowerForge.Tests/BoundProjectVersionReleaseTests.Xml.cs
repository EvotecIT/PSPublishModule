namespace PowerForge.Tests;

public sealed partial class BoundProjectVersionReleaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreserveReferences_PropsSdkImportsRetainOrdinaryLiteralUpdate(bool sdkElement)
    {
        CreateFixture();
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));
        var updated = CsprojVersionEditor.UpdateVersionText(original, "2.0.1", out _);
        var props = sdkElement
            ? Props.Replace("<PropertyGroup>", "<Sdk Name=\"Synthetic.VersionOverride\" /><PropertyGroup>", StringComparison.Ordinal)
            : Props.Replace("<Project>", "<Project Sdk=\"Synthetic.VersionOverride\">", StringComparison.Ordinal);

        Assert.Equal(updated, PreserveXmlReference(original, updated, props, props.Replace("2.0.0", "2.0.1", StringComparison.Ordinal), Spec().VersionBindings!));
    }

    [Fact]
    public void Execute_BindingCannotWriteNestedSharedVersionValue()
    {
        CreateFixture();
        var spec = Spec();
        spec.VersionBindings![0].Pattern = "<ProductVersion>[^<]*</ProductVersion>";
        spec.VersionBindings[0].Replacement = "<ProductVersion><Value>{Version}</Value></ProductVersion>";

        AssertAtomicFailure(spec, "scalar");
    }

    [Theory]
    [InlineData("Version")]
    [InlineData("PackageVersion")]
    [InlineData("VersionPrefix")]
    [InlineData("VersionSuffix")]
    public void Execute_BindingCannotWriteNestedProjectVersionValue(string tag)
    {
        CreateFixture(tag);
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(path, File.ReadAllText(path).Replace("$(ProductVersion)", "2.0.0", StringComparison.Ordinal));
        var spec = Spec();
        AddProjectBinding(spec, "<" + tag + ">[^<]*</" + tag + ">", "<" + tag + "><Value>{Version}</Value></" + tag + ">");

        AssertAtomicFailure(spec, "scalar");
    }

    [Theory]
    [InlineData("<![CDATA[2.0.0]]>", "\n")]
    [InlineData("2.0.0<!-- closing </ProductVersion> inside comment -->", "\r\n")]
    [InlineData("<![CDATA[2.0.0]]><!-- retained -->", "\r")]
    public void Execute_TextAndCdataDefinitionsKeepSharedReferenceAndSource(string value, string newline)
    {
        CreateFixture();
        var props = "<Project>" + newline + "<PropertyGroup>" + newline + "<ProductVersion>" + value + "</ProductVersion>" + newline + "</PropertyGroup></Project>";
        File.WriteAllText(PropsPath, props);
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));
        var spec = Spec();
        spec.VersionBindings![0].Pattern = "2\\.0\\.0";

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Equal(props.Replace("2.0.0", "2.0.1", StringComparison.Ordinal), File.ReadAllText(PropsPath));
        Assert.All(result.Projects, project => Assert.Equal("2.0.1", project.NewVersion));
    }

    [Fact]
    public void Execute_CdataTextInAnUnrelatedPropertyDoesNotClaimSharedOwnership()
    {
        CreateFixture();
        var props = Props.Replace("<PropertyGroup>", "<PropertyGroup><ToolVersion><![CDATA[2.0.0]]></ToolVersion>", StringComparison.Ordinal);
        File.WriteAllText(PropsPath, props);
        var spec = Spec();
        spec.VersionBindings![0].Pattern = "(?<=<!\\[CDATA\\[)2\\.0\\.0(?=\\]\\]>)";

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<Version>2.0.1</Version>", File.ReadAllText(ProjectPath("Example.Alpha")), StringComparison.Ordinal);
        Assert.Contains("<ProductVersion>2.0.0</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Execute_NestedVersionFailsBeforeWritesWithoutExpectedVersion(bool sharedFile)
    {
        CreateFixture();
        var spec = Spec();
        spec.ExpectedVersion = null;
        var path = sharedFile ? PropsPath : ProjectPath("Example.Alpha");
        spec.VersionBindings = new[] { new ProjectVersionBinding
        {
            Path = sharedFile ? "Directory.Build.props" : "Example.Alpha/Example.Alpha.csproj", Project = "Example.Alpha",
            Pattern = sharedFile ? "<ProductVersion>[^<]*</ProductVersion>" : "<Version>[^<]*</Version>",
            Replacement = sharedFile ? "<ProductVersion><Value>{Version}</Value></ProductVersion>" : "<Version><Value>$(ProductVersion)</Value></Version><ToolVersion>{Version}</ToolVersion>"
        } };

        AssertAtomicFailure(spec, "scalar");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Execute_UnrelatedXmlValuedPropertyRemainsSupported(bool sharedFile, bool targetTime)
    {
        CreateFixture();
        var spec = Spec();
        var path = sharedFile ? PropsPath : ProjectPath("Example.Alpha");
        var setting = "<PropertyGroup><ToolVersion>2.0.0</ToolVersion></PropertyGroup>";
        if (targetTime)
            setting = "<Target Name=\"Unused\">" + setting + "</Target>";
        File.WriteAllText(path, File.ReadAllText(path).Replace("</Project>", setting + "</Project>", StringComparison.Ordinal));
        spec.VersionBindings = new[] { new ProjectVersionBinding
        {
            Path = sharedFile ? "Directory.Build.props" : "Example.Alpha/Example.Alpha.csproj", Project = "Example.Alpha",
            Pattern = "<ToolVersion>[^<]*</ToolVersion>", Replacement = "<ToolVersion><Value>{Version}</Value></ToolVersion>"
        } };

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<ToolVersion><Value>2.0.1</Value></ToolVersion>", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WholeEmptyDefinitionBindingHonorsQuotedTagDelimiter()
    {
        CreateFixture();
        const string element = "<ProductVersion Condition=\"'>' == '>'\" />";
        File.WriteAllText(PropsPath, Props.Replace("<ProductVersion>2.0.0</ProductVersion>", element, StringComparison.Ordinal));
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));
        var spec = Spec();
        spec.VersionBindings![0].Pattern = System.Text.RegularExpressions.Regex.Escape(element);
        spec.VersionBindings[0].Replacement = "<ProductVersion>{Version}</ProductVersion>";

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    private string PreserveXmlReference(string original, string updated, string originalProps, string updatedProps,
        IReadOnlyList<ProjectVersionBinding> bindings) => BoundProjectVersionService.PreserveReferences(
        _root, ProjectPath("Example.Alpha"), original, updated, CsprojVersionEditor.UpdateVersionText(original, "2.0.1", out _), "2.0.1",
        new[] { new ProjectVersionBindingFileUpdate(new RepositoryTextFileUpdate(PropsPath, originalProps, updatedProps), "Directory.Build.props", 1) }, bindings);
}
