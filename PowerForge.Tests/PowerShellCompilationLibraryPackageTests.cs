namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void LibraryPackage_PreservesRootSourceIdentityAcrossMultipleSourceFiles()
    {
        using var fixture = ArtifactFixture.Create("""
            param([int] $Seed = 1)
            [int] $script:Value = $Seed
            function Get-RootValue { $script:Value }
            """, ".psm1");
        var privateDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Private")).FullName;
        var helper = Path.Combine(privateDirectory, "A.ps1");
        File.WriteAllText(helper, "function Get-HelperValue { 42 }");
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.MultiSourceLibrary",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true,
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper }
        });
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.NotNull(build.Manifest!.PublicAbi!.ModuleLifetime);

        var package = new PowerShellCompilationLibraryPackageBuilder().Build(
            new PowerShellCompilationLibraryPackageBuildRequest(
                build,
                Path.Combine(fixture.RootPath, "Generated.MultiSourceLibrary.1.0.0.nupkg"),
                "Generated.MultiSourceLibrary",
                "1.0.0"));

        using var archive = System.IO.Compression.ZipFile.OpenRead(package.PackagePath);
        using var manifestReader = new StreamReader(archive.GetEntry("powerforge/compilation-manifest.json")!.Open());
        using var manifestDocument = System.Text.Json.JsonDocument.Parse(manifestReader.ReadToEnd());
        var rootSource = manifestDocument.RootElement.GetProperty("sourcePath").GetString();
        Assert.Equal("input.psm1", rootSource, ignoreCase: true);
        using var abiReader = new StreamReader(archive.GetEntry("powerforge/public-abi.json")!.Open());
        using var abiDocument = System.Text.Json.JsonDocument.Parse(abiReader.ReadToEnd());
        var lifetimeSource = abiDocument.RootElement.GetProperty("moduleLifetime").GetProperty("sourcePath").GetString();
        Assert.Equal("input.psm1", lifetimeSource, ignoreCase: true);
        Assert.False(string.Equals("Private/A.ps1", lifetimeSource, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void LibraryPackage_RejectsSignedOrTamperedGeneratedInput()
    {
        using var fixture = ArtifactFixture.Create("function Get-CompiledValue { 42 }", ".psm1");
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.PackageIntegrity",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true
        });
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var output = Path.Combine(fixture.RootPath, "Generated.PackageIntegrity.1.0.0.nupkg");
        var request = new PowerShellCompilationLibraryPackageBuildRequest(
            build, output, "Generated.PackageIntegrity", "1.0.0");

        build.Manifest!.AuthenticodeSigned = true;
        Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationLibraryPackageBuilder().Build(request));
        build.Manifest.AuthenticodeSigned = false;
        var project = Assert.Single(build.Manifest.Files, static file => file.Role == "GeneratedProject");
        File.AppendAllText(project.Path, Environment.NewLine + "<!-- tampered -->");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationLibraryPackageBuilder().Build(request));
        Assert.Contains("recorded size and SHA-256", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void LibraryPackage_RestoresAndRunsNet472Consumer()
    {
        using var fixture = ArtifactFixture.Create("""
            function Add-CompiledValues {
                param([int] $Left, [int] $Right)
                $Left + $Right
            }
            """, ".psm1");
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.Net472Library",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net472",
            EmitSource = true
        });
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        var feed = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "net472-feed")).FullName;
        var canceledPath = Path.Combine(feed, "canceled.nupkg");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => new PowerShellCompilationLibraryPackageBuilder().Build(
                new PowerShellCompilationLibraryPackageBuildRequest(
                    build, canceledPath, "Generated.Net472Library", "1.0.0"), cancellation.Token));
        }
        Assert.False(File.Exists(canceledPath));
        var package = new PowerShellCompilationLibraryPackageBuilder().Build(
            new PowerShellCompilationLibraryPackageBuildRequest(
                build,
                Path.Combine(feed, "Generated.Net472Library.1.0.0.nupkg"),
                "Generated.Net472Library",
                "1.0.0"));
        Assert.Equal("net472", package.TargetFramework);
        Assert.Contains("lib/net472/Generated.Net472Library.dll", package.Files);
        Assert.Contains("lib/net472/Generated.Net472Library.xml", package.Files);
        Assert.Contains("lib/net472/Generated.Net472Library.pdb", package.Files);

        var consumer = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "net472-consumer")).FullName;
        var project = Path.Combine(consumer, "Net472Consumer.csproj");
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new System.Xml.Linq.XElement("PropertyGroup",
                new System.Xml.Linq.XElement("TargetFramework", "net472"),
                new System.Xml.Linq.XElement("OutputType", "Exe"),
                new System.Xml.Linq.XElement("LangVersion", "latest"),
                new System.Xml.Linq.XElement("ImplicitUsings", "enable"),
                new System.Xml.Linq.XElement("RestoreSources", feed),
                new System.Xml.Linq.XElement("RestorePackagesPath", Path.Combine(consumer, ".packages"))),
            new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("PackageReference",
                new System.Xml.Linq.XAttribute("Include", "Generated.Net472Library"),
                new System.Xml.Linq.XAttribute("Version", "1.0.0"))))).Save(project);
        var abi = build.Manifest!.PublicAbi!;
        File.WriteAllText(Path.Combine(consumer, "Program.cs"),
            "using Methods = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            if (Convert.ToInt32(Methods.Add_CompiledValues(19, 23)) != 42) return 1;
            for (var i = 0; i < 100; i++) if (Convert.ToInt32(Methods.Add_CompiledValues(i, -i)) != 0) return 2;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation")) return 3;
            Console.WriteLine("net472 local-feed consumer passed");
            return 0;
            """);
        var consumerBuild = RunProcess("dotnet", "build", project, "-c", "Release", "--nologo", "-v:q");
        Assert.True(consumerBuild.ExitCode == 0, consumerBuild.StandardOutput + consumerBuild.StandardError);
        var run = RunProcess(Path.Combine(consumer, "bin", "Release", "net472", "Net472Consumer.exe"));
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Contains("net472 local-feed consumer passed", run.StandardOutput, StringComparison.Ordinal);
    }
}
