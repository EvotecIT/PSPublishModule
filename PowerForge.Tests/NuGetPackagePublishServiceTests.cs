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
    public void ExecutePackages_PublishesOnlyExplicitPackagePaths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-nuget-publish-explicit-" + Guid.NewGuid().ToString("N")));
        try
        {
            var packagePath = Path.Combine(root.FullName, "Sample.1.0.0.nupkg");
            var oldPackagePath = Path.Combine(root.FullName, "Sample.0.9.0.nupkg");
            File.WriteAllText(packagePath, "pkg");
            File.WriteAllText(oldPackagePath, "old");

            var pushed = new List<string>();
            var service = new NuGetPackagePublishService(
                new NullLogger(),
                (package, _, _, _) =>
                {
                    pushed.Add(package);
                    return new DotNetRepositoryReleaseService.PackagePushResult
                    {
                        Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Published
                    };
                });

            var result = service.ExecutePackages(
                new[] { packagePath },
                "key",
                "https://api.nuget.org/v3/index.json",
                skipDuplicate: true);

            Assert.True(result.Success);
            Assert.Equal(new[] { packagePath }, pushed);
            Assert.Contains(packagePath, result.PublishedItems, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(oldPackagePath, result.PublishedItems, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExecutePackages_StopsOnFirstFailureWhenPublishFailFast()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-nuget-publish-explicit-failfast-" + Guid.NewGuid().ToString("N")));
        try
        {
            var firstPackagePath = Path.Combine(root.FullName, "Sample.One.1.0.0.nupkg");
            var secondPackagePath = Path.Combine(root.FullName, "Sample.Two.1.0.0.nupkg");
            File.WriteAllText(firstPackagePath, "pkg");
            File.WriteAllText(secondPackagePath, "pkg");

            var pushed = new List<string>();
            var service = new NuGetPackagePublishService(
                new NullLogger(),
                (package, _, _, _) =>
                {
                    pushed.Add(package);
                    return new DotNetRepositoryReleaseService.PackagePushResult
                    {
                        Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
                        Message = "push failed"
                    };
                });

            var result = service.ExecutePackages(
                new[] { firstPackagePath, secondPackagePath },
                "key",
                "https://api.nuget.org/v3/index.json",
                skipDuplicate: true,
                publishFailFast: true);

            Assert.False(result.Success);
            Assert.Equal(new[] { firstPackagePath }, pushed);
            Assert.Contains(firstPackagePath, result.FailedItems, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(secondPackagePath, result.FailedItems, StringComparer.OrdinalIgnoreCase);
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
