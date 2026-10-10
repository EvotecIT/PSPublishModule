namespace PowerForge.Tests;

public sealed partial class BoundProjectVersionReleaseTests
{
    [Theory]
    [InlineData("version")]
    [InlineData("packageversion")]
    [InlineData("pAcKaGeVeRsIoN")]
    public void Execute_CaseInsensitiveTagsPreserveOriginalReference(string tag)
    {
        CreateFixture(tag);
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Version")]
    [InlineData("PackageVersion")]
    public void Execute_ConditionalConsumersPreserveEachReference(string tag)
    {
        CreateFixture(tag);
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path).Replace("</Project>",
            "<PropertyGroup Condition=\"'$(Configuration)' == 'Release'\"><" + tag +
            "> $(ProductVersion) </" + tag + "></PropertyGroup></Project>", StringComparison.Ordinal);
        File.WriteAllText(path, original);

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MixedLiteralAndReferenceConsumersRetainTheirOwnExpressions()
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path).Replace("</Project>",
            "<PropertyGroup Condition=\"'$(Configuration)' == 'Release'\"><Version>2.0.0</Version>" +
            "<packageversion>$(ProductVersion)</packageversion></PropertyGroup></Project>", StringComparison.Ordinal);
        File.WriteAllText(path, original);

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original.Replace("<Version>2.0.0</Version>", "<Version>2.0.1</Version>", StringComparison.Ordinal), File.ReadAllText(path));
    }

    [Fact]
    public void Execute_TrimmedBindingPathRetainsSharedOwnership()
    {
        CreateFixture();
        var original = File.ReadAllText(ProjectPath("Example.Alpha"));
        var spec = Spec();
        spec.VersionBindings![0].Path = " Directory.Build.props ";

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<ProductVersion />")]
    [InlineData("<ProductVersion></ProductVersion>")]
    public void Execute_BindingCanInitializeEmptySharedProperty(string element)
    {
        CreateFixture();
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

    [Theory]
    [InlineData("Version")]
    [InlineData("PackageVersion")]
    public void Execute_BindingThatAddsVersionConsumerFailsBeforeAnyFileChanges(string tag)
    {
        CreateFixture(tag);
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path);
        var spec = Spec();
        spec.VersionBindings = spec.VersionBindings!.Concat(new[] { new ProjectVersionBinding
        {
            Path = "Example.Alpha/Example.Alpha.csproj", Project = "Example.Alpha",
            Pattern = "<PropertyGroup>", Replacement = "<PropertyGroup><" + tag + ">{Version}</" + tag + ">"
        } }).ToArray();

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.False(result.Success);
        Assert.Contains("version element layout", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(Props, File.ReadAllText(PropsPath));
    }

    [Fact]
    public void Execute_BindingThatChangesReferenceValueFailsBeforeAnyFileChanges()
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path);
        var spec = Spec();
        spec.VersionBindings = spec.VersionBindings!.Concat(new[] { new ProjectVersionBinding
        {
            Path = "Example.Alpha/Example.Alpha.csproj", Project = "Example.Alpha",
            Pattern = "<Version>[^<]*</Version>", Replacement = "<Version>{Version}-other</Version>"
        } }).ToArray();

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.False(result.Success);
        Assert.Contains("does not match resolved version", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(Props, File.ReadAllText(PropsPath));
    }

    [Fact]
    public void Execute_BindingThatMovesReferenceToConditionalGroupFailsAtomically()
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path);
        var spec = Spec();
        spec.VersionBindings = spec.VersionBindings!.Concat(new[] { new ProjectVersionBinding
        {
            Path = "Example.Alpha/Example.Alpha.csproj", Project = "Example.Alpha",
            Pattern = "<Version>[^<]*</Version>",
            Replacement = "</PropertyGroup><PropertyGroup Condition=\"'$(Configuration)' == 'Release'\"><Version>{Version}</Version>"
        } }).ToArray();

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.False(result.Success);
        Assert.Contains("version element layout", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(Props, File.ReadAllText(PropsPath));
    }

    [Fact]
    public void Execute_ComposedMetadataBeforeVersionKeepsConsumerMapping()
    {
        CreateFixture("packageversion");
        var path = ProjectPath("Example.Alpha");
        var original = File.ReadAllText(path);
        var spec = Spec();
        spec.VersionBindings = spec.VersionBindings!.Concat(new[] { new ProjectVersionBinding
        {
            Path = "Example.Alpha/Example.Alpha.csproj", Project = "Example.Alpha",
            Pattern = "<PropertyGroup>", Replacement = "<PropertyGroup><ToolVersion>{Version}</ToolVersion>"
        } }).ToArray();

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original.Replace("<PropertyGroup>", "<PropertyGroup><ToolVersion>2.0.1</ToolVersion>", StringComparison.Ordinal), File.ReadAllText(path));
    }
}
