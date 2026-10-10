namespace PowerForge.Tests;

public sealed class BoundProjectVersionReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-bound-version-" + Guid.NewGuid().ToString("N"));
    private const string Props = "<Project><PropertyGroup><ProductVersion>2.0.0</ProductVersion><AssemblyVersion>$(ProductVersion).0</AssemblyVersion></PropertyGroup></Project>";

    [Theory]
    [InlineData("Version")]
    [InlineData("PackageVersion")]
    public void Execute_BindingPreservesSharedReferences(string versionTag)
    {
        CreateFixture(versionTag);
        var originalAlpha = File.ReadAllText(ProjectPath("Example.Alpha"));
        var originalBeta = File.ReadAllText(ProjectPath("Example.Beta"));

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(result.Projects, project => Assert.Equal("2.0.1", project.NewVersion));
        Assert.Equal(originalAlpha, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Equal(originalBeta, File.ReadAllText(ProjectPath("Example.Beta")));
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>$(ProductVersion).0</AssemblyVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WhatIfKeepsEverySourceUnchanged()
    {
        CreateFixture();
        var originalAlpha = File.ReadAllText(ProjectPath("Example.Alpha"));
        var originalBeta = File.ReadAllText(ProjectPath("Example.Beta"));
        var spec = Spec();
        spec.WhatIf = true;

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(result.Projects, project => Assert.Equal("2.0.1", project.NewVersion));
        Assert.Equal(Props, File.ReadAllText(PropsPath));
        Assert.Equal(originalAlpha, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Equal(originalBeta, File.ReadAllText(ProjectPath("Example.Beta")));
    }

    [Fact]
    public void Execute_InconsistentVersionsFailBeforeChangingAnyFile()
    {
        CreateFixture();
        var originalAlpha = File.ReadAllText(ProjectPath("Example.Alpha"));
        var originalBeta = File.ReadAllText(ProjectPath("Example.Beta"));
        var spec = Spec();
        spec.ExpectedVersionsByProject = new Dictionary<string, string> { ["Example.Beta"] = "2.0.2" };

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.False(result.Success);
        Assert.Contains("does not match resolved version", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(Props, File.ReadAllText(PropsPath));
        Assert.Equal(originalAlpha, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Equal(originalBeta, File.ReadAllText(ProjectPath("Example.Beta")));
    }

    [Fact]
    public void Execute_NearestPropsRetainsItsSeparateVersionOwnership()
    {
        CreateFixture();
        var betaProps = Path.Combine(_root, "Example.Beta", "Directory.Build.props");
        File.WriteAllText(betaProps, Props.Replace("2.0.0", "7.0.0", StringComparison.Ordinal));

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<Version>$(ProductVersion)</Version>", File.ReadAllText(ProjectPath("Example.Alpha")), StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.1</Version>", File.ReadAllText(ProjectPath("Example.Beta")), StringComparison.Ordinal);
        Assert.Contains("<ProductVersion>7.0.0</ProductVersion>", File.ReadAllText(betaProps), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ProjectLocalPropertyRetainsItsSeparateOwnership()
    {
        CreateFixture();
        var betaPath = ProjectPath("Example.Beta");
        File.WriteAllText(betaPath, File.ReadAllText(betaPath).Replace(
            "<Version>", "<ProductVersion>7.0.0</ProductVersion><Version>", StringComparison.Ordinal));

        var result = Execute();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<Version>$(ProductVersion)</Version>", File.ReadAllText(ProjectPath("Example.Alpha")), StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.1</Version>", File.ReadAllText(betaPath), StringComparison.Ordinal);
        Assert.Contains("<ProductVersion>7.0.0</ProductVersion>", File.ReadAllText(betaPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ConditionalBoundPropertyFailsWithoutPartialEdits()
    {
        CreateFixture();
        var conditionalProps = Props.Replace("<PropertyGroup>", "<PropertyGroup Condition=\"'$(Configuration)' == 'Release'\">", StringComparison.Ordinal);
        File.WriteAllText(PropsPath, conditionalProps);
        var originalAlpha = File.ReadAllText(ProjectPath("Example.Alpha"));
        var originalBeta = File.ReadAllText(ProjectPath("Example.Beta"));

        var result = Execute();

        Assert.False(result.Success);
        Assert.Contains("one unconditional definition", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(conditionalProps, File.ReadAllText(PropsPath));
        Assert.Equal(originalAlpha, File.ReadAllText(ProjectPath("Example.Alpha")));
        Assert.Equal(originalBeta, File.ReadAllText(ProjectPath("Example.Beta")));
    }

    [Fact]
    public void Execute_ComposedProjectBindingPreservesSharedReferenceAndMetadata()
    {
        CreateFixture();
        var path = ProjectPath("Example.Alpha");
        File.WriteAllText(path, File.ReadAllText(path).Replace("</PropertyGroup>",
            "<ToolVersion>2.0.0</ToolVersion></PropertyGroup>", StringComparison.Ordinal));
        var spec = Spec();
        spec.VersionBindings = spec.VersionBindings!.Concat(new[] { new ProjectVersionBinding
        {
            Path = "Example.Alpha/Example.Alpha.csproj", Project = "Example.Alpha",
            Pattern = @"(?<=<ToolVersion>)\d+\.\d+\.\d+(?=</ToolVersion>)"
        } }).ToArray();

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("<Version>$(ProductVersion)</Version>", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("<ToolVersion>2.0.1</ToolVersion>", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("<ProductVersion>2.0.1</ProductVersion>", File.ReadAllText(PropsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Execute_UnrelatedPropsBindingRetainsOrdinaryVersionUpdate(bool conditional, bool wholeElement)
    {
        CreateFixture();
        var originalProps = Props.Replace("<ProductVersion>", "<ToolVersion>2.0.0</ToolVersion><ProductVersion>", StringComparison.Ordinal);
        if (conditional)
            originalProps = originalProps.Replace("<ProductVersion>", "<ProductVersion Condition=\"'$(Configuration)' == 'Release'\">", StringComparison.Ordinal);
        File.WriteAllText(PropsPath, originalProps);
        var spec = Spec();
        spec.VersionBindings = new[] { new ProjectVersionBinding
        {
            Path = "Directory.Build.props", Project = "Example.Alpha",
            Pattern = wholeElement ? @"<ToolVersion>\d+\.\d+\.\d+</ToolVersion>" : @"(?<=<ToolVersion>)\d+\.\d+\.\d+(?=</ToolVersion>)",
            Replacement = wholeElement ? "<ToolVersion>{Version}</ToolVersion>" : "{Version}"
        } };

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(result.Projects, project => Assert.Contains("<Version>2.0.1</Version>",
            File.ReadAllText(project.CsprojPath), StringComparison.Ordinal));
        Assert.Equal(originalProps.Replace("<ToolVersion>2.0.0", "<ToolVersion>2.0.1", StringComparison.Ordinal), File.ReadAllText(PropsPath));
    }

    private string PropsPath => Path.Combine(_root, "Directory.Build.props");
    private string ProjectPath(string name) => Path.Combine(_root, name, name + ".csproj");

    private void CreateFixture(string versionTag = "Version")
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(PropsPath, Props);
        foreach (var name in new[] { "Example.Alpha", "Example.Beta" })
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
            File.WriteAllText(ProjectPath(name), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><" +
                versionTag + ">$(ProductVersion)</" + versionTag + "></PropertyGroup></Project>");
        }
    }

    private DotNetRepositoryReleaseResult Execute() => new DotNetRepositoryReleaseService(new NullLogger()).Execute(Spec());

    private DotNetRepositoryReleaseSpec Spec() => new()
    {
        RootPath = _root,
        IncludeProjects = new[] { "Example.Alpha", "Example.Beta" },
        ExpectedVersion = "2.0.1",
        UpdateVersions = true,
        Pack = false,
        VersionBindings = new[] { new ProjectVersionBinding
        {
            Path = "Directory.Build.props", Project = "Example.Alpha",
            Pattern = @"(?<=<ProductVersion>)\d+\.\d+\.\d+(?=</ProductVersion>)"
        } }
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
