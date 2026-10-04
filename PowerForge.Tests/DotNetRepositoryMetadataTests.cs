namespace PowerForge.Tests;

public sealed class DotNetRepositoryMetadataTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Execute_UsesEvaluatedPackabilityAndVersion(bool importedPackability)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-package-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><IsPackable>" + importedPackability.ToString().ToLowerInvariant() +
                "</IsPackable><VersionPrefix>2.3.4</VersionPrefix></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Sample.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>Sample.Evaluated</PackageId>
    <IsPackable Condition="'$(Configuration)' == 'Debug'">false</IsPackable>
  </PropertyGroup>
</Project>
""");
            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root,
                Configuration = "Release",
                WhatIf = true,
                UpdateVersions = false,
                Pack = false
            });
            var project = Assert.Single(result.Projects);
            Assert.Equal(importedPackability, project.IsPackable);
            Assert.Equal("Sample.Evaluated", project.PackageId);
            Assert.Equal(importedPackability, result.Success);
            if (importedPackability)
                Assert.Equal("2.3.4", project.NewVersion);
            else
                Assert.Contains("No packable projects", result.ErrorMessage);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Execute_RejectsSigningWithoutCertificateBeforeChangingOrBuildingSources(bool assemblies, bool packages)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-sign-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // No project is needed: invalid signing must fail before discovery or build work.
            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root,
                Pack = true,
                UpdateVersions = true,
                SignAssemblies = assemblies,
                SignPackages = packages
            });
            Assert.False(result.Success);
            Assert.Contains("CertificateThumbprint", result.ErrorMessage);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
