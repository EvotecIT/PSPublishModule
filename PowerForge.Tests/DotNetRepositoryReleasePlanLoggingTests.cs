namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleasePlanLoggingTests
{
    [Fact]
    public void Execute_WhatIfPublish_ReportsPlannedArtifactsWithoutClaimingPublication()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Package"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Package.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.Package</PackageId>
                    <VersionPrefix>1.2.3</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);

            var logger = new RecordingLogger();
            var result = new DotNetRepositoryReleaseService(logger).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                Pack = true,
                Publish = true,
                WhatIf = true,
                PublishApiKey = "not-used-by-what-if",
                PublishSource = "https://api.nuget.org/v3/index.json",
                UpdateVersions = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(logger.SuccessMessages, message =>
                message.Contains("NuGet publish plan prepared", StringComparison.Ordinal) &&
                message.Contains("1 package artifact(s) would be published", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.SuccessMessages, message =>
                message.Contains("NuGet publish phase completed", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> SuccessMessages { get; } = new();

        public bool IsVerbose => false;

        public void Info(string message) { }

        public void Success(string message) => SuccessMessages.Add(message);

        public void Warn(string message) { }

        public void Error(string message) { }

        public void Verbose(string message) { }
    }
}
