using System.Collections;
using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleasePreparationServiceTests
{
    [Fact]
    public void Prepare_resolves_root_credentials_and_spec()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-release-prepare-" + Guid.NewGuid().ToString("N")));
        var envName = "PF_TEST_DOTNET_RELEASE_" + Guid.NewGuid().ToString("N");

        try
        {
            var secretPath = Path.Combine(root.FullName, "secret.txt");
            File.WriteAllText(secretPath, " from-file ");
            Environment.SetEnvironmentVariable(envName, "publish-from-env");

            var request = new DotNetRepositoryReleasePreparationRequest
            {
                CurrentPath = root.FullName,
                RootPath = "repo",
                ExpectedVersion = "1.2.X",
                ExpectedVersionMap = new Hashtable { ["ProjectA"] = "2.0.0" },
                ExpectedVersionMapAsInclude = true,
                NugetCredentialUserName = "user",
                NugetCredentialSecretFilePath = secretPath,
                PublishApiKeyEnvName = envName,
                Configuration = "Debug",
                OutputPath = "artifacts",
                CertificateStore = CertificateStoreLocation.LocalMachine,
                TimeStampServer = "http://timestamp.test",
                Publish = true,
                PublishFailFast = true,
                SkipDuplicate = true
            };

            var context = new DotNetRepositoryReleasePreparationService().Prepare(request);

            Assert.Equal(Path.Combine(root.FullName, "repo"), context.RootPath);
            Assert.Equal("1.2.X", context.Spec.ExpectedVersion);
            Assert.True(context.Spec.ExpectedVersionMapAsInclude);
            Assert.Equal("2.0.0", context.Spec.ExpectedVersionsByProject!["ProjectA"]);
            Assert.Equal("user", context.Spec.VersionSourceCredential!.UserName);
            Assert.Equal("from-file", context.Spec.VersionSourceCredential.Secret);
            Assert.Equal("publish-from-env", context.Spec.PublishApiKey);
            Assert.Equal(Path.Combine(root.FullName, "repo", "artifacts"), context.Spec.OutputPath);
            Assert.Equal(PowerForge.CertificateStoreLocation.LocalMachine, context.Spec.CertificateStore);
            Assert.Equal("Debug", context.Spec.Configuration);
            Assert.True(context.Spec.Publish);
            Assert.True(context.Spec.PublishFailFast);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_throws_for_invalid_expected_version_map_entry()
    {
        var request = new DotNetRepositoryReleasePreparationRequest
        {
            CurrentPath = Directory.GetCurrentDirectory(),
            ExpectedVersionMap = new Hashtable { ["ProjectA"] = string.Empty }
        };

        var exception = Assert.Throws<ArgumentException>(() => new DotNetRepositoryReleasePreparationService().Prepare(request));
        Assert.Contains("ExpectedVersionMap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
