using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebDotNetProcessLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildAndPublish_UseRequestScopedBuildWorkersAndPreserveResults(bool failBuild)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-captured-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            var project = Path.Combine(root, "Sample.csproj");
            File.WriteAllText(project, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <Target Name="VerifyCapturedBuildLifetime" BeforeTargets="BeforeBuild;Publish">
                    <Error Condition="'$(MSBUILDDISABLENODEREUSE)' != '1' Or '$(DOTNET_CLI_USE_MSBUILD_SERVER)' != '0'" Text="Captured builds must use request-scoped MSBuild workers." />
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/process-lifetime.txt" Lines="$(MSBUILDDISABLENODEREUSE)|$(DOTNET_CLI_USE_MSBUILD_SERVER)" Overwrite="false" />
                    <Message Importance="high" Text="captured build output: café" />
                    <Error Condition="'{{failBuild}}' == 'True'" Text="requested build failure" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Sample.cs"), "public static class Sample { public static int Value => 42; }");

            var built = WebDotNetRunner.Build(new WebDotNetBuildOptions { ProjectOrSolution = project, Configuration = "Release" });

            Assert.True(built.Output.Contains("captured build output: café", StringComparison.Ordinal), built.Output + built.Error);
            Assert.All(File.ReadAllLines(Path.Combine(root, "process-lifetime.txt")), line => Assert.Equal("1|0", line));
            if (failBuild)
            {
                Assert.False(built.Success);
                Assert.NotEqual(0, built.ExitCode);
                Assert.Contains("requested build failure", built.Output, StringComparison.Ordinal);
                return;
            }

            Assert.True(built.Success, built.Output + built.Error);
            Assert.Equal(0, built.ExitCode);
            var output = Path.Combine(root, "published");
            var published = WebDotNetRunner.Publish(new WebDotNetPublishOptions
            {
                ProjectPath = project, OutputPath = output, Configuration = "Release", NoBuild = true, NoRestore = true
            });
            Assert.True(published.Success, published.Output + published.Error);
            Assert.Equal(0, published.ExitCode);
            Assert.Contains("captured build output: café", published.Output, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output, "Sample.dll")));
            Assert.Equal(new[] { "1|0", "1|0" }, File.ReadAllLines(Path.Combine(root, "process-lifetime.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
