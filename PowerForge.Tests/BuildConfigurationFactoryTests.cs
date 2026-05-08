using PowerForge;

namespace PowerForge.Tests;

public sealed class BuildConfigurationFactoryTests
{
    [Fact]
    public void Create_emits_build_signing_library_and_placeholder_segments()
    {
        var factory = new BuildConfigurationFactory();

        var segments = factory.Create(new BuildConfigurationRequest
        {
            EnableSpecified = true,
            Enable = true,
            MergeModuleOnBuildSpecified = true,
            MergeModuleOnBuild = true,
            SignModuleSpecified = true,
            SignModule = true,
            SyncNETProjectVersionSpecified = true,
            SyncNETProjectVersion = true,
            SignIncludeInternalsSpecified = true,
            SignIncludeInternals = true,
            CertificateThumbprintSpecified = true,
            CertificateThumbprint = "thumb",
            NETConfigurationSpecified = true,
            NETConfiguration = "Release",
            NETProjectPathSpecified = true,
            NETProjectPath = "src/Sample.csproj",
            NETAssemblyLoadContextSpecified = true,
            NETAssemblyLoadContext = true,
            SkipBuiltinReplacementsSpecified = true,
            SkipBuiltinReplacements = true
        });

        Assert.Equal(4, segments.Count);

        var build = Assert.IsType<ConfigurationBuildSegment>(segments[0]);
        Assert.True(build.BuildModule.Enable);
        Assert.True(build.BuildModule.Merge);
        Assert.True(build.BuildModule.SignMerged);
        Assert.True(build.BuildModule.SyncNETProjectVersion);

        var options = Assert.IsType<ConfigurationOptionsSegment>(segments[1]);
        var signing = Assert.IsType<SigningOptionsConfiguration>(options.Options.Signing);
        Assert.True(signing.IncludeInternals);
        Assert.Equal("thumb", signing.CertificateThumbprint);

        var libraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(segments[2]);
        Assert.True(libraries.BuildLibraries.Enable);
        Assert.Equal("Release", libraries.BuildLibraries.Configuration);
        Assert.Equal("src/Sample.csproj", libraries.BuildLibraries.NETProjectPath);
        Assert.True(libraries.BuildLibraries.UseAssemblyLoadContext);

        var placeholder = Assert.IsType<ConfigurationPlaceHolderOptionSegment>(segments[3]);
        Assert.True(placeholder.PlaceHolderOption.SkipBuiltinReplacements);
    }

    [Fact]
    public void Create_reads_missing_module_secret_from_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "secret-value");
        try
        {
            var factory = new BuildConfigurationFactory();

            var segments = factory.Create(new BuildConfigurationRequest
            {
                InstallMissingModulesCredentialUserNameSpecified = true,
                InstallMissingModulesCredentialUserName = "user",
                InstallMissingModulesCredentialSecretFilePathSpecified = true,
                InstallMissingModulesCredentialSecretFilePath = path
            });

            var build = Assert.IsType<ConfigurationBuildSegment>(Assert.Single(segments));
            var credential = Assert.IsType<RepositoryCredential>(build.BuildModule.InstallMissingModulesCredential);
            Assert.Equal("user", credential.UserName);
            Assert.Equal("secret-value", credential.Secret);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Create_throws_when_missing_module_secret_has_no_username()
    {
        var factory = new BuildConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new BuildConfigurationRequest
        {
            InstallMissingModulesCredentialSecretSpecified = true,
            InstallMissingModulesCredentialSecret = "secret-value"
        }));

        Assert.Contains("InstallMissingModulesCredentialUserName", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_throws_when_pfx_path_has_no_password()
    {
        var factory = new BuildConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new BuildConfigurationRequest
        {
            CertificatePFXPathSpecified = true,
            CertificatePFXPath = "code-signing.pfx"
        }));

        Assert.Contains("CertificatePFXPassword", ex.Message, StringComparison.Ordinal);
    }
}
