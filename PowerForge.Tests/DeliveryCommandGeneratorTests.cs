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
            Assert.DoesNotContain("function Copy-DeliveryRootFiles", installContent, StringComparison.OrdinalIgnoreCase);

            var updateContent = File.ReadAllText(updatePath);
            Assert.Contains("function Update-EFAdminManager", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-EFAdminManager", updateContent, StringComparison.OrdinalIgnoreCase);
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

