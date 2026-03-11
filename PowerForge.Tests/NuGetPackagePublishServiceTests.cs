using PowerForge;

namespace PowerForge.Tests;

public sealed class NuGetPackagePublishServiceTests
{
    [Fact]
    public void Execute_in_plan_mode_reports_packages_as_published()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-nuget-publish-" + Guid.NewGuid().ToString("N")));
        try
        {
            var packagePath = Path.Combine(root.FullName, "Sample.1.0.0.nupkg");
            File.WriteAllText(packagePath, "pkg");

            var service = new NuGetPackagePublishService(new NullLogger());
            var result = service.Execute(new NuGetPackagePublishRequest
            {
                Roots = new[] { root.FullName },
                ApiKey = "key",
                Source = "https://api.nuget.org/v3/index.json"
            }, _ => false);

            Assert.True(result.Success);
            Assert.Contains(packagePath, result.PublishedItems, StringComparer.OrdinalIgnoreCase);
            Assert.Empty(result.FailedItems);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Execute_marks_failed_packages_when_push_fails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-nuget-publish-fail-" + Guid.NewGuid().ToString("N")));
        try
        {
            var packagePath = Path.Combine(root.FullName, "Sample.1.0.0.nupkg");
            File.WriteAllText(packagePath, "pkg");

            var service = new NuGetPackagePublishService(
                new NullLogger { IsVerbose = true },
                (_, _, _, _) => new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
                    Message = "push failed"
                });

            var result = service.Execute(new NuGetPackagePublishRequest
            {
                Roots = new[] { root.FullName },
                ApiKey = "key",
                Source = "https://api.nuget.org/v3/index.json"
            });

            Assert.False(result.Success);
            Assert.Empty(result.PublishedItems);
            Assert.Contains(packagePath, result.FailedItems, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Execute_treats_skipped_duplicate_as_success()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-nuget-publish-dup-" + Guid.NewGuid().ToString("N")));
        try
        {
            var packagePath = Path.Combine(root.FullName, "Sample.1.0.0.nupkg");
            File.WriteAllText(packagePath, "pkg");

            var service = new NuGetPackagePublishService(
                new NullLogger(),
                (_, _, _, _) => new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate,
                    Message = "already exists"
                });

            var result = service.Execute(new NuGetPackagePublishRequest
            {
                Roots = new[] { root.FullName },
                ApiKey = "key",
                Source = "https://api.nuget.org/v3/index.json",
                SkipDuplicate = true
            });

            Assert.True(result.Success);
            Assert.Contains(packagePath, result.PublishedItems, StringComparer.OrdinalIgnoreCase);
            Assert.Empty(result.FailedItems);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
