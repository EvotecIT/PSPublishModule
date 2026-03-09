using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class DeliveryCommandGeneratorTests
{
    [Fact]
    public void Generate_CreatesInstallAndUpdateScripts()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var delivery = new DeliveryOptionsConfiguration
            {
                Enable = true,
                InternalsPath = "Internals",
                PreservePaths = new[] { "Config/**", "Docs" },
                OverwritePaths = new[] { "Artefacts/**" },
                GenerateInstallCommand = true,
                GenerateUpdateCommand = true
            };

            var generator = new DeliveryCommandGenerator(new NullLogger());
            var generated = generator.Generate(root.FullName, "EFAdminManager", delivery);

            Assert.Contains(generated, g => g.Name == "Install-EFAdminManager");
            Assert.Contains(generated, g => g.Name == "Update-EFAdminManager");

            var installPath = Path.Combine(root.FullName, "Public", "Install-EFAdminManager.ps1");
            var updatePath = Path.Combine(root.FullName, "Public", "Update-EFAdminManager.ps1");

            Assert.True(File.Exists(installPath));
            Assert.True(File.Exists(updatePath));

            var installContent = File.ReadAllText(installPath);
            Assert.Contains("function Install-EFAdminManager", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Write-Host \"[EFAdminManager] Installing bundled package content\"", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Write-DeliveryError", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $PreservePaths = @('Config/**', 'Docs')", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $OverwritePaths = @('Artefacts/**')", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $Bootstrap", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string] $RepositoryCredentialSecretFilePath", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $__DeliveryNoBootstrap", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Resolve-DeliverySecret", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Test-DeliveryPathMatch", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Get-DeliveryAction", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{{", installContent, StringComparison.Ordinal);

            var updateContent = File.ReadAllText(updatePath);
            Assert.Contains("function Update-EFAdminManager", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-EFAdminManager", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Write-Host \"[EFAdminManager] Updating bundled package content via Install-EFAdminManager\"", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Delivery.InstallCommandMissing", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $PreservePaths", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $OverwritePaths", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $Bootstrap", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string] $RepositoryCredentialSecretFilePath", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $__DeliveryNoBootstrap", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{{", updateContent, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Generate_Skips_WhenScriptAlreadyExists()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var publicFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Public"));
            File.WriteAllText(Path.Combine(publicFolder.FullName, "Install-EFAdminManager.ps1"), "function Install-EFAdminManager { }");

            var delivery = new DeliveryOptionsConfiguration
            {
                Enable = true,
                GenerateInstallCommand = true,
                GenerateUpdateCommand = false
            };

            var generator = new DeliveryCommandGenerator(new NullLogger());
            var generated = generator.Generate(root.FullName, "EFAdminManager", delivery);

            Assert.DoesNotContain(generated, g => g.Name == "Install-EFAdminManager");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
