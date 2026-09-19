namespace PowerForge.Tests;

public sealed class NativeInstallerReleaseSafetyTests
{
    [Theory]
    [InlineData(DotNetPublishInstallerKind.Debian)]
    [InlineData(DotNetPublishInstallerKind.MacApp)]
    public void SharedReleaseVersion_RejectsNativePackageMismatch(DotNetPublishInstallerKind kind)
    {
        DotNetPublishInstallerPlan installer = CreateInstaller(kind, "1.2.4");
        var plan = new DotNetPublishPlan { Installers = new[] { installer } };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PowerForgeReleaseService.ValidateNativeInstallerReleaseVersions(plan, "1.2.3"));

        Assert.Contains(installer.Id, exception.Message, StringComparison.Ordinal);
        Assert.Contains("1.2.4", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DotNetPublishInstallerKind.Debian)]
    [InlineData(DotNetPublishInstallerKind.MacApp)]
    public void SharedReleaseVersion_AcceptsExactNativePackageVersion(DotNetPublishInstallerKind kind)
    {
        var plan = new DotNetPublishPlan { Installers = new[] { CreateInstaller(kind, "1.2.3") } };

        PowerForgeReleaseService.ValidateNativeInstallerReleaseVersions(plan, "1.2.3");
    }

    [Theory]
    [InlineData(DotNetPublishInstallerKind.Debian)]
    [InlineData(DotNetPublishInstallerKind.MacApp)]
    public void ToolsOnlyRelease_RejectsNativePackageTargetVersionMismatch(DotNetPublishInstallerKind kind)
    {
        var plan = new DotNetPublishPlan
        {
            Targets = new[] { new DotNetPublishTargetPlan { Name = "sample", Version = "1.2.3" } },
            Installers = new[] { CreateInstaller(kind, "1.2.4") }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PowerForgeReleaseService.ValidateNativeInstallerReleaseVersions(plan, sharedReleaseVersion: null));

        Assert.Contains("1.2.3", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1.2.4", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DotNetPublishInstallerKind.Debian)]
    [InlineData(DotNetPublishInstallerKind.MacApp)]
    public void ToolsOnlyRelease_AcceptsMatchingNativePackageTargetVersion(DotNetPublishInstallerKind kind)
    {
        var plan = new DotNetPublishPlan
        {
            Targets = new[] { new DotNetPublishTargetPlan { Name = "sample", Version = "1.2.3" } },
            Installers = new[] { CreateInstaller(kind, "1.2.3") }
        };

        PowerForgeReleaseService.ValidateNativeInstallerReleaseVersions(plan, sharedReleaseVersion: null);
    }

    [Fact]
    public void SharedReleaseVersion_RejectsMsiMismatchBeforeReservation()
    {
        var plan = new DotNetPublishPlan
        {
            Installers = new[] { new DotNetPublishInstallerPlan
            {
                Id = "studio.msi", Versioning = new DotNetPublishMsiVersionOptions { Enabled = true, ApplyToPublish = true }
            } },
            MsiVersions = new Dictionary<string, DotNetPublishMsiVersionPlan>
            {
                ["studio.msi|studio|net10.0|win-x64|PortableCompat"] = new() { Version = "0.1.9758" }
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ValidateNativeInstallerReleaseVersions(plan, "0.1.9759"));

        Assert.Contains("studio.msi", exception.Message, StringComparison.Ordinal);
        Assert.Contains("0.1.9758", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedReleaseVersion_AllowsSeparateMsiVersionWhenNotAppliedToPublish()
    {
        var plan = new DotNetPublishPlan
        {
            Installers = new[] { new DotNetPublishInstallerPlan
            {
                Id = "studio.msi", Versioning = new DotNetPublishMsiVersionOptions { Enabled = true, ApplyToPublish = false }
            } },
            MsiVersions = new Dictionary<string, DotNetPublishMsiVersionPlan>
            {
                ["studio.msi|studio|net10.0|win-x64|PortableCompat"] = new() { Version = "0.1.9758" }
            }
        };

        PowerForgeReleaseService.ValidateNativeInstallerReleaseVersions(plan, "0.1.0");
    }

    [Fact]
    public void MacAppExecutionBoundary_RejectsMutatedDistributionIdentity()
    {
        var options = new DotNetPublishMacAppOptions
        {
            Executable = "Sample",
            CodesignIdentity = "Developer ID Application: Example"
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => DotNetPublishPipelineRunner.ValidateMacAppExecutionBoundary(options, "sample.macapp"));

        Assert.Contains("ad-hoc", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("bin/Sample")]
    [InlineData("bin\\Sample")]
    public void MacAppExecutionBoundary_RejectsMutatedNestedExecutable(string executable)
    {
        var options = new DotNetPublishMacAppOptions
        {
            Executable = executable,
            CodesignIdentity = "-"
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => DotNetPublishPipelineRunner.ValidateMacAppExecutionBoundary(options, "sample.macapp"));

        Assert.Contains("file name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerChecksums_UseOnlyDeclaredOutputs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string package = Path.Combine(root, "sample.deb");
            string stale = Path.Combine(root, "stale.zip");
            File.WriteAllText(package, "package");
            File.WriteAllText(stale, "stale");
            var artifact = new DotNetPublishArtefactResult
            {
                Category = DotNetPublishArtefactCategory.Installer,
                OutputDir = root,
                OutputFiles = new[] { package }
            };

            string[] files = DotNetPublishPipelineRunner.EnumerateArtifactFilesForChecksum(artifact).ToArray();

            Assert.Equal(new[] { package }, files);
            Assert.DoesNotContain(stale, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DotNetPublishInstallerPlan CreateInstaller(DotNetPublishInstallerKind kind, string version)
        => new()
        {
            Id = kind == DotNetPublishInstallerKind.Debian ? "sample.debian" : "sample.macapp",
            Kind = kind,
            PrepareFromTarget = "sample",
            Debian = kind == DotNetPublishInstallerKind.Debian
                ? new DotNetPublishDebianOptions { Version = version }
                : null,
            MacApp = kind == DotNetPublishInstallerKind.MacApp
                ? new DotNetPublishMacAppOptions { Version = version }
                : null
        };
}
