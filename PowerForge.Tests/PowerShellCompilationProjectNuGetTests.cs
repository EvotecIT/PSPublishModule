using System.Text.Json;
using System.Xml.Linq;
using NuGet.Packaging;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_NuGetPackRequiresExplicitLibraryInputsAndSingleTarget()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 7 }", ".psm1", compactPath: true);
        var (project, manifest) = CreateNuGetProject(fixture, "net10.0");
        var service = new PowerShellCompilationProjectManifestService();
        var workflow = new PowerShellCompilationProjectWorkflowService();
        var metadata = manifest.NuGet;
        manifest.NuGet = null;
        service.Save(project, manifest);
        Assert.Contains("explicit nuGet metadata", workflow.PackNuGet(project).Targets.Single().Message);
        manifest.NuGet = metadata;
        manifest.Artifacts[0].EmitSource = false;
        service.Save(project, manifest);
        Assert.Contains("emitSource=true", workflow.PackNuGet(project).Targets.Single().Message);
        manifest.Artifacts[0].EmitSource = true;
        var library = manifest.Artifacts.Single();
        var moduleTarget = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, "net10.0", null, false, false, PowerShellCompilationExecutableOptimization.None, true);
        var module = service.Create(project, fixture.ScriptPath, manifest.Name, moduleTarget).Artifacts.Single();
        manifest.Artifacts = new[] { module };
        service.Save(project, manifest);
        Assert.Contains("Strict library target", workflow.PackNuGet(project).Targets.Single().Message);
        manifest.Artifacts = new[] { library, module };
        service.Save(project, manifest);
        var ambiguous = Assert.Throws<InvalidOperationException>(() => workflow.PackNuGet(project));
        Assert.Contains("exactly one", ambiguous.Message);
        Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, ".powerforge/packages")));
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_NuGetPackRebuildsLockedLibraryAndRunsOrdinaryConsumer(string framework)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("function Add-CompiledValues { param([int] $Left, [int] $Right) $Left + $Right }", ".psm1", compactPath: true);
        var (project, manifest) = CreateNuGetProject(fixture, framework);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        AssertProjectSuccess(workflow.Build(project));
        var premature = workflow.PackNuGet(project);
        Assert.False(premature.Succeeded);
        Assert.Contains("project test", premature.Targets.Single().Message);
        AssertProjectSuccess(workflow.Test(project));
        var packed = workflow.PackNuGet(project);
        AssertProjectSuccess(packed);
        var package = packed.Targets.Single();
        var receipt = ReadRunReceipt(fixture, manifest.Artifacts.Single());
        Assert.Equal(receipt.Manifest!.PublicAbi!.Sha256, package.PublicAbiSha256);
        Assert.Equal(PowerShellCompilationProjectManifestService.ComputeSha256(package.Path!), package.PackageSha256);
        using (var reader = new PackageArchiveReader(package.Path!))
        {
            Assert.Equal(manifest.NuGet!.PackageId, reader.NuspecReader.GetId());
            Assert.Equal("1.0.0", reader.NuspecReader.GetVersion().ToNormalizedString());
            Assert.Contains(reader.GetFiles(), path => path.StartsWith("lib/" + framework + "/") && path.EndsWith(".dll"));
            Assert.Contains("powerforge/generated-source/packages.lock.json", reader.GetFiles());
            using var lockStream = reader.GetStream("powerforge/generated-source/packages.lock.json");
            using var lockBytes = new MemoryStream();
            lockStream.CopyTo(lockBytes);
            var environment = JsonSerializer.Deserialize<PowerShellCompilationProjectEnvironment>(
                File.ReadAllText(Path.Combine(fixture.RootPath, ".powerforge/environment/environment.json")),
                PowerShellCompilationProjectManifestService.JsonOptions)!;
            Assert.Equal(File.ReadAllBytes(Path.Combine(fixture.RootPath, environment.ResolvedLocks.Single().Path)), lockBytes.ToArray());
            using var props = new StreamReader(reader.GetStream("powerforge/generated-source/Directory.Build.props"));
            Assert.Contains("<RestoreLockedMode>true</RestoreLockedMode>", props.ReadToEnd());
        }
        var prior = File.ReadAllBytes(package.Path!);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => workflow.PackNuGet(project, cancellationToken: canceled.Token));
        }
        Assert.Equal(prior, File.ReadAllBytes(package.Path!));
        AssertProjectSuccess(workflow.Pack(project));
        AssertProjectSuccess(workflow.Install(project));

        // Ordinary users consume the NuGet package without any compiler or PowerShell references.
        var consumer = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "consumer")).FullName;
        var consumerProject = Path.Combine(consumer, "Consumer.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", framework),
                new XElement("OutputType", "Exe"), new XElement("LangVersion", "latest"),
                new XElement("ImplicitUsings", "enable"),
                new XElement("RestoreSources", Path.GetDirectoryName(package.Path)),
                new XElement("RestorePackagesPath", Path.Combine(consumer, ".packages"))),
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", manifest.NuGet!.PackageId),
                new XAttribute("Version", manifest.NuGet.PackageVersion))))).Save(consumerProject);
        var abi = receipt.Manifest.PublicAbi;
        File.WriteAllText(Path.Combine(consumer, "Program.cs"),
            "using Methods = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            if (Convert.ToInt32(Methods.Add_CompiledValues(19, 23)) != 42) return 1;
            for (var i = 0; i < 50; i++) if (Convert.ToInt32(Methods.Add_CompiledValues(i, -i)) != 0) return 2;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation")) return 3;
            Console.WriteLine("project package consumer passed");
            return 0;
            """);
        var built = RunProcess("dotnet", "build", consumerProject, "-c", "Release", "--nologo", "-v:q");
        Assert.True(built.ExitCode == 0, built.StandardOutput + built.StandardError);
        var executed = framework == "net472"
            ? RunProcess(Path.Combine(consumer, "bin/Release/net472/Consumer.exe"))
            : RunProcess("dotnet", Path.Combine(consumer, "bin/Release/net10.0/Consumer.dll"));
        Assert.True(executed.ExitCode == 0, executed.StandardOutput + executed.StandardError);
        Assert.Contains("project package consumer passed", executed.StandardOutput);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_NuGetPackRejectsBreakingAbiAndTamperedArtifactWithoutReplacingPackage()
    {
        using var fixture = ArtifactFixture.Create("function Get-Original { return 7 }", ".psm1", compactPath: true);
        var (project, manifest) = CreateNuGetProject(fixture, "net10.0");
        var workflow = new PowerShellCompilationProjectWorkflowService();
        Qualify();
        var packed = workflow.PackNuGet(project);
        AssertProjectSuccess(packed);
        var packagePath = packed.Targets.Single().Path!;
        var prior = File.ReadAllBytes(packagePath);
        var receipt = ReadRunReceipt(fixture, manifest.Artifacts.Single());
        var generated = Directory.GetFiles(receipt.GeneratedSourcePath!, "*.cs").First();
        var originalGenerated = File.ReadAllBytes(generated);
        File.AppendAllText(generated, "\n// changed after project test\n");
        var tampered = workflow.PackNuGet(project);
        Assert.False(tampered.Succeeded);
        Assert.Contains("differs", tampered.Targets.Single().Message);
        Assert.Equal(prior, File.ReadAllBytes(packagePath));
        File.WriteAllBytes(generated, originalGenerated);

        File.WriteAllText(Path.Combine(fixture.RootPath, "baseline.json"),
            JsonSerializer.Serialize(receipt.Manifest!.PublicAbi, PowerShellCompilationProjectManifestService.JsonOptions));
        manifest.NuGet!.CompatibilityBaseline = "baseline.json";
        new PowerShellCompilationProjectManifestService().Save(project, manifest);
        File.WriteAllText(fixture.ScriptPath, "function Get-Replacement { return 9 }");
        Qualify();
        var incompatible = workflow.PackNuGet(project);
        Assert.False(incompatible.Succeeded);
        Assert.Contains("breaking public ABI", incompatible.Targets.Single().Message);
        Assert.Equal(prior, File.ReadAllBytes(packagePath));

        void Qualify()
        {
            AssertProjectSuccess(workflow.Lock(project));
            AssertProjectSuccess(workflow.Restore(project));
            AssertProjectSuccess(workflow.Build(project));
            AssertProjectSuccess(workflow.Test(project));
        }
    }

    private static (string Project, PowerShellCompilationProjectManifest Manifest) CreateNuGetProject(ArtifactFixture fixture, string framework)
    {
        var path = Path.Combine(fixture.RootPath, "powerforge.psproject.json");
        var service = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, framework, null, false, false, PowerShellCompilationExecutableOptimization.None, true);
        var manifest = service.Create(path, fixture.ScriptPath, "ProjectLibrary", target);
        manifest.Artifacts[0].EmitSource = true;
        manifest.NuGet = new PowerShellCompilationProjectNuGetPackage
        {
            PackageId = "Project.Library." + framework.Replace(".", string.Empty), PackageVersion = "1.0.0",
            Authors = "Project fixture", Description = "Ordinary compiled-library consumer proof.", LicenseExpression = "MIT"
        };
        service.Save(path, manifest);
        return (path, manifest);
    }
}
