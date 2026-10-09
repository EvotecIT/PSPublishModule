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
                Assert.Equal(manifestGuid, ModulePipelineRunner.ValidateApprovedModuleManifestIdentity(root.FullName, constraint));
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
}
