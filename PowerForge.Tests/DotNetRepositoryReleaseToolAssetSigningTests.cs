using System.IO.Compression;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DotNetRepositoryReleaseToolAssetSigningTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, "Always", "8.0.100")]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, "Always", "8.0.100")]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, "Always", "10.0.100")]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, "Always", "10.0.100")]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, "IfDifferent", "10.0.100")]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, "IfDifferent", "10.0.100")]
    public void Execute_PreservesSignedToolRuntimeAssets(
        DotNetRepositoryPackStrategy strategy, string copyMode, string sdkVersion)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        // dotnet test exposes its own SDK to the host. Let fixture global.json
        // select the child SDK instead of inheriting the test host's SDK paths.
        var sdkEnvironment = new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in sdkEnvironment.Keys)
                Environment.SetEnvironmentVariable(name, null);
            // SDK 8 consumes ResolvedFileToPublish; SDK 10 reads the staging directory.
            // Exercise both packing contracts without changing the repository's SDK pin.
            File.WriteAllText(Path.Combine(root.FullName, "global.json"), $$"""
                { "sdk": { "version": "{{sdkVersion}}", "rollForward": "latestFeature" } }
                """);
            var dependencyDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Dependency"));
            File.WriteAllText(Path.Combine(dependencyDirectory.FullName, "Sample.Dependency.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework><IsPackable>false</IsPackable></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(dependencyDirectory.FullName, "Dependency.cs"), "namespace Sample.Dependency; public static class Value { public static string Text => \"dependency\"; }");
            var projectPath = Path.Combine(root.FullName, "Sample.Tool.csproj");
            File.WriteAllText(projectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <PackAsTool>true</PackAsTool>
                    <ToolCommandName>sample-tool</ToolCommandName>
                    <PackageId>Sample.Tool</PackageId>
                    <Version>1.0.0</Version>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Remove="Sample.Dependency/**/*.cs" />
                    <ProjectReference Include="Sample.Dependency/Sample.Dependency.csproj" />
                    <None Update="Sample.Runtime.dll" CopyToPublishDirectory="{{copyMode}}"
                          TargetPath="runtimes/win-x64/lib/net8.0/Sample.Runtime.dll" />
                    <None Update="settings.json" CopyToPublishDirectory="Always" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root.FullName, "Program.cs"), "System.Console.WriteLine(Sample.Dependency.Value.Text);");
            File.WriteAllText(Path.Combine(root.FullName, "Messages.fr.resx"), """
                <root>
                  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
                  <resheader name="version"><value>2.0</value></resheader>
                  <data name="Greeting" xml:space="preserve"><value>Bonjour</value></data>
                </root>
                """);
            var runtimePath = Path.Combine(root.FullName, "Sample.Runtime.dll");
            var originalBytes = new byte[] { 1, 2, 3, 4 };
            File.WriteAllBytes(runtimePath, originalBytes);
            File.WriteAllText(Path.Combine(root.FullName, "settings.json"), "{\"value\":\"sample\"}");
            var marker = new byte[] { 0x53, 0x49, 0x47, 0x4E };
            var signedRuntimePaths = new List<string>();

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    OutputPath = Path.Combine(root.FullName, "packages"),
                    Pack = true,
                    PackStrategy = strategy,
                    Publish = false,
                    UpdateVersions = false,
                    CreateReleaseZip = false,
                    SignAssemblies = true,
                    SignDependencyAssemblies = true,
                    CertificateThumbprint = "ABC123",
                    SignPackages = false
                },
                request =>
                {
                    foreach (var path in Assert.IsType<string[]>(request.FilePaths).Where(path =>
                                 Path.GetFileName(path) is "Sample.Runtime.dll" or "Sample.Dependency.dll" or "Sample.Tool.resources.dll"))
                    {
                        signedRuntimePaths.Add(path);
                        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
                        stream.Write(marker, 0, marker.Length);
                    }
                },
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEmpty(signedRuntimePaths);
            Assert.Equal(originalBytes, File.ReadAllBytes(runtimePath));
            var package = Assert.Single(Assert.Single(result.Projects, project => project.PackageId == "Sample.Tool").Packages);
            using var archive = ZipFile.OpenRead(package);
            Assert.Single(archive.Entries, entry => entry.FullName == "tools/net8.0/any/Sample.Tool.dll");
            Assert.Single(archive.Entries, entry => entry.FullName == "tools/net8.0/any/Sample.Tool.deps.json");
            Assert.Single(archive.Entries, entry => entry.FullName == "tools/net8.0/any/Sample.Dependency.dll");
            Assert.Single(archive.Entries, entry => entry.FullName == "tools/net8.0/any/fr/Sample.Tool.resources.dll");
            foreach (var name in new[] { "Sample.Dependency.dll", "fr/Sample.Tool.resources.dll" })
            {
                var entry = Assert.Single(archive.Entries, item => item.FullName == "tools/net8.0/any/" + name);
                using var dependencyInput = entry.Open();
                using var dependencyBytes = new MemoryStream();
                dependencyInput.CopyTo(dependencyBytes);
                Assert.Equal(marker, dependencyBytes.ToArray().TakeLast(marker.Length).ToArray());
            }
            var asset = Assert.Single(archive.Entries, entry => entry.FullName == "tools/net8.0/any/runtimes/win-x64/lib/net8.0/Sample.Runtime.dll");
            using var input = asset.Open();
            using var bytes = new MemoryStream();
            input.CopyTo(bytes);
            Assert.Equal(originalBytes.Concat(marker).ToArray(), bytes.ToArray());
            var settings = Assert.Single(archive.Entries, entry => entry.FullName == "tools/net8.0/any/settings.json");
            using var reader = new StreamReader(settings.Open());
            Assert.Equal("{\"value\":\"sample\"}", reader.ReadToEnd());
        }
        finally
        {
            foreach (var pair in sdkEnvironment)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
