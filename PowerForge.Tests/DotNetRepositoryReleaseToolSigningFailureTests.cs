using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseToolSigningFailureTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, "PackAsTool")]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, "PackAsTool")]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, "IntermediateOutputPath")]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, "IntermediateOutputPath")]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, "PublishDir")]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, "PublishDir")]
    public void Execute_WhenPreparedToolSigningDiscoveryFails_DoesNotSignOrPack(
        DotNetRepositoryPackStrategy strategy,
        string failingProperty)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Tool"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Tool.csproj");
            var projectXml = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.Tool</PackageId>
                    <VersionPrefix>1.0.0</VersionPrefix>
                    <PackAsTool>true</PackAsTool>
                    <ToolCommandName>sample-tool</ToolCommandName>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Update="Sample.Runtime.dll" CopyToPublishDirectory="PreserveNewest" TargetPath="runtimes/win-x64/lib/net8.0/Sample.Runtime.dll" />
                  </ItemGroup>
                </Project>
                """;
            File.WriteAllText(projectPath, projectXml);
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Program.cs"), "System.Console.WriteLine(\"sample\");");
            File.WriteAllBytes(Path.Combine(projectDirectory.FullName, "Sample.Runtime.dll"), new byte[] { 1, 2, 3, 4 });
            var logger = new TransientPropertyFailureLogger(projectPath, projectXml, failingProperty);
            var signingCalls = 0;

            var result = new DotNetRepositoryReleaseService(logger).Execute(
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    Configuration = "Release",
                    OutputPath = Path.Combine(root.FullName, "packages"),
                    Pack = true,
                    PackStrategy = strategy,
                    Publish = false,
                    UpdateVersions = false,
                    CreateReleaseZip = false,
                    CertificateThumbprint = "ABC123",
                    SignAssemblies = true,
                    SignDependencyAssemblies = true,
                    SignPackages = false
                },
                _ => signingCalls++,
                _ => { });

            Assert.True(logger.FailureInjected);
            Assert.True(logger.ProjectRestored);
            Assert.False(result.Success);
            Assert.Equal(0, signingCalls);
            Assert.All(result.Projects.Where(project => project.IsPackable), project =>
            {
                Assert.Contains(failingProperty, project.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(project.Packages);
            });
            Assert.Empty(Directory.EnumerateFiles(root.FullName, "*.nupkg", SearchOption.AllDirectories));
            Assert.Equal(projectXml, File.ReadAllText(projectPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    // A missing imported file models one transient MSBuild evaluation failure after
    // successful staging. Restore it when the completed process reports its output,
    // so later packing can succeed if discovery incorrectly ignores the failure.
    private sealed class TransientPropertyFailureLogger(string projectPath, string projectXml, string failingProperty) : ILogger
    {
        private bool _prepared;
        public bool FailureInjected { get; private set; }
        public bool ProjectRestored { get; private set; }
        public bool IsVerbose => true;

        public void Success(string message)
        {
            if (!message.Contains("prepared clean pack-tool publish output", StringComparison.Ordinal))
                return;
            _prepared = true;
            if (failingProperty == "PackAsTool")
                InjectFailure();
        }

        public void Verbose(string message)
        {
            if (!_prepared || ProjectRestored)
                return;

            if (FailureInjected && message.Contains($"dotnet msbuild {failingProperty} ", StringComparison.Ordinal))
            {
                File.WriteAllText(projectPath, projectXml);
                ProjectRestored = true;
                return;
            }

            var precedingProperty = failingProperty == "IntermediateOutputPath" ? "PackAsTool" : "IntermediateOutputPath";
            if (!FailureInjected && message.Contains($"dotnet msbuild {precedingProperty} stdout:", StringComparison.Ordinal))
                InjectFailure();
        }

        private void InjectFailure()
        {
            File.WriteAllText(projectPath, projectXml.Replace("</Project>", "<Import Project=\"Transient.Missing.props\" /></Project>"));
            FailureInjected = true;
        }

        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
    }
}
