using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactConfigurationFactoryTests
{
    [Fact]
    public void Create_normalizes_paths_and_preserves_merge_scripts_when_formatter_is_unavailable()
    {
        var factory = new ArtefactConfigurationFactory(new NullLogger());
        var request = new ArtefactConfigurationRequest
        {
            Type = ArtefactType.Packed,
            EnableSpecified = true,
            Enable = true,
            AddRequiredModulesSpecified = true,
            AddRequiredModules = true,
            Path = "Artefacts/Packed",
            ModulesPath = "Modules/Public",
            RequiredModulesPath = "Modules/Required",
            CopyDirectories = new[]
            {
                new ArtefactCopyMapping { Source = "Docs/Help", Destination = "Internals/Help" }
            },
            CopyFiles = new[]
            {
                new ArtefactCopyMapping { Source = "README.md", Destination = "Internals/README.md" }
            },
            PreScriptMergeText = "Write-Host 'pre'",
            PostScriptMergeText = "Write-Host 'post'"
        };

        var segment = factory.Create(request);

        Assert.Equal(ArtefactType.Packed, segment.ArtefactType);
        Assert.True(segment.Configuration.Enabled);
        Assert.True(segment.Configuration.RequiredModules.Enabled);
        Assert.Equal(Normalize("Artefacts/Packed"), segment.Configuration.Path);
        Assert.Equal(Normalize("Modules/Public"), segment.Configuration.RequiredModules.ModulesPath);
        Assert.Equal(Normalize("Modules/Required"), segment.Configuration.RequiredModules.Path);
        Assert.Equal(Normalize("Docs/Help"), segment.Configuration.DirectoryOutput![0].Source);
        Assert.Equal(Normalize("Internals/Help"), segment.Configuration.DirectoryOutput[0].Destination);
        Assert.Equal(Normalize("README.md"), segment.Configuration.FilesOutput![0].Source);
        Assert.Equal(Normalize("Internals/README.md"), segment.Configuration.FilesOutput[0].Destination);
        Assert.Equal("Write-Host 'pre'", segment.Configuration.PreScriptMerge);
        Assert.Equal("Write-Host 'post'", segment.Configuration.PostScriptMerge);
    }

    [Fact]
    public void Create_reads_secret_from_file_and_requires_username()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-artefact-" + Guid.NewGuid().ToString("N")));

        try
        {
            var secretPath = Path.Combine(root.FullName, "secret.txt");
            File.WriteAllText(secretPath, " top-secret " + Environment.NewLine, new UTF8Encoding(false));

            var factory = new ArtefactConfigurationFactory(new NullLogger());
            var segment = factory.Create(new ArtefactConfigurationRequest
            {
                Type = ArtefactType.Unpacked,
                RequiredModulesCredentialUserName = "user",
                RequiredModulesCredentialSecretFilePath = secretPath
            });

            Assert.NotNull(segment.Configuration.RequiredModules.Credential);
            Assert.Equal("user", segment.Configuration.RequiredModules.Credential!.UserName);
            Assert.Equal("top-secret", segment.Configuration.RequiredModules.Credential.Secret);

            var ex = Assert.Throws<ArgumentException>(() => factory.Create(new ArtefactConfigurationRequest
            {
                Type = ArtefactType.Unpacked,
                RequiredModulesCredentialSecret = "secret"
            }));

            Assert.Contains("RequiredModulesCredentialUserName", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static string Normalize(string value)
    {
        return OperatingSystem.IsWindows()
            ? value.Replace('/', '\\')
            : value.Replace('\\', '/');
    }
}
