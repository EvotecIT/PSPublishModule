namespace PowerForge.Tests;

public sealed class DotNetPublishWorkingTreeTests
{
    [Fact]
    public void Run_OrdinaryUnsignedPublishBuildsAndPackagesWithoutAGitCheckout()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "App.csproj");
            File.WriteAllText(project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "App.cs"), "public class App { public string Value => \"built from source\"; }");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"),
                "<configuration><packageSources><clear /></packageSources></configuration>");
            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var plan = runner.Plan(new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions { ProjectRoot = root, Build = false },
                Targets = [new DotNetPublishTarget
                {
                    Name = "App", ProjectPath = project, Kind = DotNetPublishTargetKind.Library,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0", Runtimes = [System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier],
                        Style = DotNetPublishStyle.FrameworkDependent, Zip = true
                    }
                }]
            }, configPath: null);
            Assert.True(plan.NoBuildInPublish);
            Assert.False(plan.UseControlledSourceProvenance);
            var result = runner.Run(plan, progress: null);
            Assert.True(result.Succeeded, result.ErrorMessage);
            var artifact = Assert.Single(result.Artefacts);
            Assert.True(File.Exists(Path.Combine(artifact.OutputDir, "App.dll")));
            Assert.True(File.Exists(artifact.ZipPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
