using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ApprovedModuleRepositoryIdentityTests
{
    [Fact]
    public void RepositorySelectionAcceptsMissingGuidMetadataForManifestValidation()
    {
        var constraint = new RequiredModuleReference(
            "Approved.Donor", requiredVersion: "2.0.0", guid: "11111111-1111-1111-1111-111111111111");
        var candidate = new PSResourceInfo("Approved.Donor", "2.0.0", "PSGallery", null, null);

        Assert.Same(candidate, ModulePipelineRunner.SelectApprovedModuleRepositoryCandidate(constraint, new[] { candidate }));
    }

    [Fact]
    public void RepositorySelectionPrefersKnownMatchingIdentityWithinVersionRange()
    {
        const string donorGuid = "11111111-1111-1111-1111-111111111111";
        var constraint = new RequiredModuleReference("Approved.Donor", moduleVersion: "1.0.0", guid: donorGuid);
        var known = new PSResourceInfo("Approved.Donor", "2.0.0", "PSGallery", null, null, guid: donorGuid);
        var unknown = new PSResourceInfo("Approved.Donor", "3.0.0", "PSGallery", null, null);

        Assert.Same(known, ModulePipelineRunner.SelectApprovedModuleRepositoryCandidate(constraint, new[] { known, unknown }));
    }

    [Fact]
    public void RepositorySelectionAllowsUnknownIdentityWhenKnownVersionIsOutsideConstraint()
    {
        const string donorGuid = "11111111-1111-1111-1111-111111111111";
        var constraint = new RequiredModuleReference("Approved.Donor", requiredVersion: "3.0.0", guid: donorGuid);
        var known = new PSResourceInfo("Approved.Donor", "2.0.0", "PSGallery", null, null, guid: donorGuid);
        var unknown = new PSResourceInfo("Approved.Donor", "3.0.0", "PSGallery", null, null);

        Assert.Same(unknown, ModulePipelineRunner.SelectApprovedModuleRepositoryCandidate(constraint, new[] { known, unknown }));
    }

    [Fact]
    public void RepositorySelectionDoesNotDowngradeResolvedLatestVersionForKnownIdentity()
    {
        const string donorGuid = "11111111-1111-1111-1111-111111111111";
        var constraint = new ResolvedRequiredModuleReference(
            "Approved.Donor", "3.0.0", null, null, donorGuid, "3.0.0", "3.0.0", false);
        var known = new PSResourceInfo("Approved.Donor", "2.0.0", "PSGallery", null, null, guid: donorGuid);
        var latest = new PSResourceInfo("Approved.Donor", "3.0.0", "PSGallery", null, null);

        Assert.Same(latest, ModulePipelineRunner.SelectApprovedModuleRepositoryCandidate(constraint, new[] { known, latest }));
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111", true)]
    [InlineData("22222222-2222-2222-2222-222222222222", "11111111-1111-1111-1111-111111111111", false)]
    [InlineData(null, "11111111-1111-1111-1111-111111111111", false)]
    [InlineData("11111111-1111-1111-1111-111111111111", null, true)]
    public void DownloadedManifestEnforcesDonorIdentity(string? manifestGuid, string? requiredGuid, bool accepted)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Approved.Donor.psd1"),
                "@{ RootModule = 'Approved.Donor.psm1'; ModuleVersion = '2.0.0'; " +
                (manifestGuid is null ? "" : $"GUID = '{manifestGuid}'; ") + "}");
            var constraint = new RequiredModuleReference("Approved.Donor", requiredVersion: "2.0.0", guid: requiredGuid);

            if (accepted)
            {
                Assert.Equal(manifestGuid, ModulePipelineRunner.ValidateApprovedModuleManifestIdentity(root.FullName, constraint).Guid);
            }
            else
            {
                var error = Assert.Throws<InvalidOperationException>(() =>
                    ModulePipelineRunner.ValidateApprovedModuleManifestIdentity(root.FullName, constraint));
                Assert.Contains("Approved.Donor", error.Message);
                Assert.Contains(requiredGuid!, error.Message);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DownloadedManifestPreservesCanonicalNameForCaseInsensitiveDeclarations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string expectedGuid = "11111111-1111-1111-1111-111111111111";
            File.WriteAllText(Path.Combine(root.FullName, "Approved.Donor.psd1"),
                $"@{{ RootModule = 'Approved.Donor.psm1'; ModuleVersion = '2.0.0'; GUID = '{expectedGuid}' }}");
            var constraint = new RequiredModuleReference("approved.donor", requiredVersion: "2.0.0", guid: expectedGuid);

            var identity = ModulePipelineRunner.ValidateApprovedModuleManifestIdentity(root.FullName, constraint);

            Assert.Equal("Approved.Donor", identity.Name);
            Assert.Equal(expectedGuid, identity.Guid);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
