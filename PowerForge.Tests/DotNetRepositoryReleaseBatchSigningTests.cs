using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseBatchSigningTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Execute_WithMsBuildBatch_SignsEveryProjectInOneRequestAndKeepsLogsInProjectOrder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var names = new[] { "Sample.Alpha", "Sample.Bravo", "Sample.Charlie", "Sample.Delta" };
            foreach (var name in names)
            {
                var directory = Directory.CreateDirectory(Path.Combine(root.FullName, name));
                File.WriteAllText(Path.Combine(directory.FullName, name + ".csproj"), $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <PackageId>{name}</PackageId>
                        <VersionPrefix>1.0.0</VersionPrefix>
                      </PropertyGroup>
                    </Project>
                    """);
                File.WriteAllText(Path.Combine(directory.FullName, "Code.cs"), $"namespace {name}; public static class Code {{ }}");
            }

            var logger = new BufferedLogger();
            var requests = new List<string[]>();
            var result = new DotNetRepositoryReleaseService(logger).Execute(
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    Configuration = "Release",
                    OutputPath = Path.Combine(root.FullName, "packages"),
                    Pack = true,
                    PackStrategy = DotNetRepositoryPackStrategy.MSBuild,
                    Publish = false,
                    UpdateVersions = false,
                    CreateReleaseZip = false,
                    CertificateThumbprint = "ABC123",
                    SignAssemblies = true,
                    SignPackages = false
                },
                request => requests.Add(Assert.IsType<string[]>(request.FilePaths)),
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);

            // Plans are resolved in parallel, but the whole batch is still signed in a single request.
            var signed = Assert.Single(requests);
            foreach (var name in names)
                Assert.Contains(signed, path => Path.GetFileName(path).Equals(name + ".dll", StringComparison.OrdinalIgnoreCase));

            // Each project's buffered lines are replayed in project order, not completion order.
            var packable = result.Projects.Where(static project => project.IsPackable).Select(static project => project.ProjectName).ToArray();
            var logged = logger.Entries
                .Select(static entry => entry.Message)
                .Where(static message => message.Contains(": assembly signing include pattern(s):", StringComparison.Ordinal))
                .Select(static message => message.Substring(0, message.IndexOf(':')))
                .ToArray();
            Assert.Equal(packable, logged);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
