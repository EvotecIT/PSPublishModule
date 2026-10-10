namespace PowerForge.Tests;

public sealed partial class BoundProjectVersionReleaseTests
{
    [Theory]
    [InlineData("Inherited")]
    [InlineData("Local")]
    [InlineData("Chained")]
    public void Execute_XmlVersionSupplierFailsAtomicallyWithoutExpectedVersion(string supplier)
    {
        CreateFixture();
        var spec = Spec();
        spec.ExpectedVersion = null;
        var path = ProjectPath("Example.Alpha");
        var property = "ProductVersion";
        var bindingPath = "Directory.Build.props";
        if (supplier == "Inherited")
        {
            File.WriteAllText(PropsPath, Props.Replace("</PropertyGroup>", "<Version>$(ProductVersion)</Version></PropertyGroup>", StringComparison.Ordinal));
            foreach (var name in new[] { "Example.Alpha", "Example.Beta" })
                File.WriteAllText(ProjectPath(name), File.ReadAllText(ProjectPath(name)).Replace("<Version>$(ProductVersion)</Version>", string.Empty, StringComparison.Ordinal));
        }
        else if (supplier == "Local")
        {
            File.WriteAllText(path, File.ReadAllText(path).Replace("<Version>", "<ProductVersion>2.0.0</ProductVersion><Version>", StringComparison.Ordinal));
            bindingPath = "Example.Alpha/Example.Alpha.csproj";
        }
        else
        {
            File.WriteAllText(PropsPath, Props.Replace("<ProductVersion>2.0.0</ProductVersion>",
                "<RawVersion>2.0.0</RawVersion><ProductVersion>$(RawVersion)</ProductVersion>", StringComparison.Ordinal));
            property = "RawVersion";
        }
        spec.VersionBindings = new[] { new ProjectVersionBinding
        {
            Path = bindingPath, Project = "Example.Alpha", Pattern = "<" + property + ">[^<]*</" + property + ">",
            Replacement = "<" + property + "><Value>{Version}</Value></" + property + ">"
        } };

        AssertAtomicFailure(spec, "scalar");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Execute_OpaqueXmlSettingsDoNotSupplyOrOwnPackageVersion(bool sharedFile)
    {
        CreateFixture();
        var path = sharedFile ? PropsPath : ProjectPath("Example.Alpha");
        const string settings = "<ToolSettings><PropertyGroup><ProductVersion>7.0.0</ProductVersion><Version>7.0.0</Version></PropertyGroup></ToolSettings>";
        File.WriteAllText(path, File.ReadAllText(path).Replace("<PropertyGroup>", "<PropertyGroup>" + settings, StringComparison.Ordinal));
        var project = File.ReadAllText(ProjectPath("Example.Alpha"));
        var props = File.ReadAllText(PropsPath);

        var spec = Spec();
        spec.VersionBindings![0].Pattern = "(?<=<ProductVersion>)2\\.0\\.0(?=</ProductVersion>)";
        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(project, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Equal(props.Replace("<ProductVersion>2.0.0", "<ProductVersion>2.0.1", StringComparison.Ordinal), File.ReadAllText(PropsPath));
        Assert.All(result.Projects, item => Assert.Equal("2.0.1", item.NewVersion));
    }

    [Theory]
    [InlineData("<![CDATA[$(ProductVersion)]]>")]
    [InlineData("$(ProductVersion)<!-- reference -->")]
    public void Execute_ProjectCdataAndCommentsKeepSharedReference(string reference)
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        var project = File.ReadAllText(path).Replace("$(ProductVersion)", reference, StringComparison.Ordinal);
        File.WriteAllText(path, project);

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(project, File.ReadAllText(path));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }
}
