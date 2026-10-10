namespace PowerForge.Tests;

public sealed partial class BoundProjectVersionReleaseTests
{
    [Fact]
    public void Execute_ComposedLocalOverrideFailsBeforeAnyFileChanges()
    {
        CreateFixture();
        var spec = Spec();
        AddProjectBinding(spec, "<PropertyGroup>", "<PropertyGroup><ProductVersion>{Version}-other</ProductVersion>");

        AssertAtomicFailure(spec, "version property ownership");
    }

    [Fact]
    public void Execute_ComposedImportOverrideFailsBeforeAnyFileChanges()
    {
        CreateFixture();
        File.WriteAllText(Path.Combine(_root, "Example.Alpha", "Override.props"),
            "<Project><PropertyGroup><ProductVersion>2.0.1-other</ProductVersion></PropertyGroup></Project>");
        var spec = Spec();
        AddProjectBinding(spec, "<PropertyGroup>", "<Import Project=\"Override.props\" /><PropertyGroup><ToolVersion>{Version}</ToolVersion>");

        AssertAtomicFailure(spec, "version property ownership");
    }

    [Theory]
    [InlineData("<PropertyGroup><ProductVersion>2.0.0</ProductVersion></PropertyGroup><Choose><When Condition=\"'$(Configuration)' == 'Release'\"><PropertyGroup><ProductVersion>8.0.0</ProductVersion></PropertyGroup></When></Choose>")]
    [InlineData("<Choose><When Condition=\"'$(Configuration)' == 'Release'\"><PropertyGroup><ProductVersion>2.0.0</ProductVersion></PropertyGroup></When></Choose>")]
    [InlineData("<Choose><When Condition=\"'$(Configuration)' == 'Unused'\"><PropertyGroup><OtherProperty>0</OtherProperty></PropertyGroup></When><Otherwise><PropertyGroup><ProductVersion>2.0.0</ProductVersion></PropertyGroup></Otherwise></Choose>")]
    public void Execute_NestedSharedDefinitionsFailWithoutPartialEdits(string propertyGroups)
    {
        CreateFixture();
        File.WriteAllText(PropsPath, "<Project>" + propertyGroups + "</Project>");

        var spec = Spec();
        // Bind only the intended 2.0.0 definition; the conditional override must be
        // rejected by ownership validation rather than the exact-once regex check.
        spec.VersionBindings![0].Pattern = @"(?<=<ProductVersion>)2\.0\.0(?=</ProductVersion>)";
        AssertAtomicFailure(spec, "one unconditional definition");
    }

    [Theory]
    [InlineData("Version", "Example.Alpha")]
    [InlineData("PackageVersion", "Example.Alpha")]
    [InlineData("PackageVersion", "Example.Beta")]
    public void Execute_ComposedLiteralVersionConflictFailsWithoutPartialEdits(string literalTag, string bindingSource)
    {
        CreateFixture(literalTag == "Version" ? "PackageVersion" : "Version");
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(path, File.ReadAllText(path).Replace("</PropertyGroup>",
            "<" + literalTag + ">2.0.0</" + literalTag + "></PropertyGroup>", StringComparison.Ordinal));
        var spec = Spec();
        AddProjectBinding(spec, "<" + literalTag + ">[^<]*</" + literalTag + ">",
            "<" + literalTag + ">{Version}-other</" + literalTag + ">", bindingSource);

        AssertAtomicFailure(spec, "does not match resolved version");
    }

    [Fact]
    public void Execute_ExistingCustomImportRetainsOrdinaryProjectVersionUpdate()
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(Path.Combine(_root, "Example.Alpha", "Override.props"),
            "<Project><PropertyGroup><ProductVersion>7.0.0</ProductVersion></PropertyGroup></Project>");
        File.WriteAllText(path, File.ReadAllText(path).Replace("</Project>",
            "<Import Project=\"Override.props\" /></Project>", StringComparison.Ordinal));

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<Version>2.0.1</Version>", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("<Version>$(ProductVersion)</Version>", File.ReadAllText(ProjectPath("Example.Beta")), StringComparison.Ordinal);
    }

    private static void AddProjectBinding(DotNetRepositoryReleaseSpec spec, string pattern, string replacement, string source = "Example.Alpha")
        => spec.VersionBindings = spec.VersionBindings!.Concat(new[] { new ProjectVersionBinding
        {
            Path = "Example.Alpha/Example.Alpha.csproj", Project = source,
            Pattern = pattern, Replacement = replacement
        } }).ToArray();

    private void AssertAtomicFailure(DotNetRepositoryReleaseSpec spec, string message)
    {
        var originalProps = File.ReadAllText(PropsPath);
        var originalAlpha = File.ReadAllText(ProjectPath("Example.Alpha"));
        var originalBeta = File.ReadAllText(ProjectPath("Example.Beta"));

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.False(result.Success);
        Assert.Contains(message, result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(originalProps, File.ReadAllText(PropsPath));
        Assert.Equal(originalAlpha, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Equal(originalBeta, File.ReadAllText(ProjectPath("Example.Beta")));
    }
}
