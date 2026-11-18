using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryBatchRestoreTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public async Task BuildSelected_FromCleanCheckout_BuildsProjectsWhoseReferencesComeFromPackages()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            // netstandard2.0 gets its framework references from the NETStandard.Library package, so it only compiles
            // when the build evaluates the project after restore wrote the NuGet imports.
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Standard"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Standard.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Code.cs"), "namespace Sample.Standard { public static class Code { public static string Name => typeof(Code).Assembly.GetName().Name; } }");
            var traversal = Path.Combine(root.FullName, "pack.proj");
            DotNetRepositoryReleaseService.WritePackTraversalProject(traversal,
                new[] { new DotNetRepositoryProjectResult { ProjectName = "Sample.Standard", PackageId = "Sample.Standard", CsprojPath = projectPath, IsPackable = true } },
                new DotNetRepositoryReleaseSpec { RootPath = root.FullName, Configuration = "Release" },
                Path.Combine(root.FullName, "packages"));

            var result = await new ProcessRunner(ownProcessTree: true).RunAsync(new ProcessRunRequest(
                "dotnet", root.FullName,
                new[] { "msbuild", traversal, "-target:BuildSelected", "-nodeReuse:false", "-verbosity:minimal" },
                TimeSpan.FromMinutes(3)));

            Assert.False(result.TimedOut, result.StdOut + result.StdErr);
            Assert.True(result.ExitCode == 0, result.StdOut + result.StdErr);
            Assert.True(File.Exists(Path.Combine(projectDirectory.FullName, "bin", "Release", "netstandard2.0", "Sample.Standard.dll")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public async Task RestoreSelected_SharedDependencyOutputs_DoNotOverlap()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var completed = Path.Combine(root.FullName, "completed.txt");
            var common = Path.Combine(root.FullName, "common.proj");
            new XDocument(new XElement("Project",
                new XElement("UsingTask",
                    new XAttribute("TaskName", "WriteSharedRestoreOutput"),
                    new XAttribute("TaskFactory", "RoslynCodeTaskFactory"),
                    new XAttribute("AssemblyFile", "$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll"),
                    new XElement("ParameterGroup",
                        new XElement("SharedPath", new XAttribute("ParameterType", "System.String"), new XAttribute("Required", "true")),
                        new XElement("CompletedPath", new XAttribute("ParameterType", "System.String"), new XAttribute("Required", "true")),
                        new XElement("Owner", new XAttribute("ParameterType", "System.String"), new XAttribute("Required", "true"))),
                    new XElement("Task", new XElement("Code", new XAttribute("Type", "Fragment"), new XAttribute("Language", "cs"),
                        new XCData("""
                            using (var output = new System.IO.FileStream(SharedPath, System.IO.FileMode.OpenOrCreate,
                                System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                            {
                                // Hold the actual shared writer open so parallel root restores contend.
                                System.Threading.Thread.Sleep(1500);
                                System.IO.File.AppendAllText(CompletedPath, Owner + System.Environment.NewLine);
                            }
                            """)))),
                new XElement("Target", new XAttribute("Name", "Restore"),
                    new XElement("WriteSharedRestoreOutput",
                        new XAttribute("SharedPath", Path.Combine(root.FullName, "shared.assets")),
                        new XAttribute("CompletedPath", completed),
                        new XAttribute("Owner", "$(RestoreRoot)")))))
                .Save(common);

            var projects = new[] { "First", "Second" }.Select(name =>
            {
                var path = Path.Combine(root.FullName, name + ".proj");
                new XDocument(new XElement("Project",
                    new XElement("Target", new XAttribute("Name", "Restore"),
                        new XElement("MSBuild", new XAttribute("Projects", common),
                            new XAttribute("Targets", "Restore"),
                            new XAttribute("Properties", "RestoreRoot=" + name)))))
                    .Save(path);
                return new DotNetRepositoryProjectResult
                {
                    ProjectName = name,
                    PackageId = "Sample." + name,
                    CsprojPath = path,
                    IsPackable = true
                };
            }).ToArray();
            var traversal = Path.Combine(root.FullName, "pack.proj");
            DotNetRepositoryReleaseService.WritePackTraversalProject(traversal, projects,
                new DotNetRepositoryReleaseSpec { RootPath = root.FullName, Configuration = "Release" },
                Path.Combine(root.FullName, "packages"));

            var result = await new ProcessRunner(ownProcessTree: true).RunAsync(new ProcessRunRequest(
                "dotnet", root.FullName,
                new[] { "msbuild", traversal, "-target:RestoreSelected", "-maxcpucount:2", "-nodeReuse:false", "-verbosity:minimal" },
                TimeSpan.FromMinutes(2)));

            Assert.False(result.TimedOut, result.StdOut + result.StdErr);
            Assert.True(result.ExitCode == 0, result.StdOut + result.StdErr);
            Assert.Equal(new[] { "First", "Second" }, File.ReadAllLines(completed));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
