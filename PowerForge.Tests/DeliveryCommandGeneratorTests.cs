using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
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
            Assert.Contains("[string[]] $PreservePaths", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $OverwritePaths", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $Bootstrap", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string] $Version", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string] $Repository", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-Module @installParams", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{{", installContent, StringComparison.Ordinal);

            var updateContent = File.ReadAllText(updatePath);
            Assert.Contains("function Update-EFAdminManager", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-EFAdminManager", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[switch] $Bootstrap", updateContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[string[]] $PreservePaths", updateContent, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void InstallCommand_MergeRespectsPreserveAndOverwritePatterns()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "ContosoPackage";
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, moduleName));

            Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Internals", "Config"));
            Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Internals", "Artefacts"));
            Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Internals", "Other"));

            File.WriteAllText(Path.Combine(moduleRoot.FullName, "Internals", "Config", "settings.json"), "package-config");
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "Internals", "Artefacts", "script.ps1"), "package-artefact");
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "Internals", "Other", "notes.txt"), "package-notes");

            var delivery = new DeliveryOptionsConfiguration
            {
                Enable = true,
                InternalsPath = "Internals",
                GenerateInstallCommand = true,
                PreservePaths = new[] { "Config/**" },
                OverwritePaths = new[] { "Artefacts/**" }
            };

            var generator = new DeliveryCommandGenerator(new NullLogger());
            _ = generator.Generate(moduleRoot.FullName, moduleName, delivery);

            var moduleScriptPath = Path.Combine(moduleRoot.FullName, $"{moduleName}.psm1");
            File.WriteAllText(
                moduleScriptPath,
                $". $PSScriptRoot{Path.DirectorySeparatorChar}Public{Path.DirectorySeparatorChar}Install-{moduleName}.ps1");

            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "Destination"));
            Directory.CreateDirectory(Path.Combine(destination.FullName, "Config"));
            Directory.CreateDirectory(Path.Combine(destination.FullName, "Artefacts"));
            Directory.CreateDirectory(Path.Combine(destination.FullName, "Other"));

            File.WriteAllText(Path.Combine(destination.FullName, "Config", "settings.json"), "local-config");
            File.WriteAllText(Path.Combine(destination.FullName, "Artefacts", "script.ps1"), "local-artefact");
            File.WriteAllText(Path.Combine(destination.FullName, "Other", "notes.txt"), "local-notes");

            using var ps = PowerShell.Create();
            ps.AddScript("if ($env:OS -eq 'Windows_NT') { Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force }");
            ps.AddStatement();
            ps.AddCommand("Import-Module")
                .AddParameter("Name", moduleScriptPath)
                .AddParameter("Force");
            ps.AddStatement();
            ps.AddCommand($"Install-{moduleName}")
                .AddParameter("Path", destination.FullName);

            _ = ps.Invoke();
            Assert.False(
                ps.HadErrors,
                "PowerShell errors: " + string.Join(" | ", ps.Streams.Error.Select(e => e.ToString())));

            Assert.Equal("local-config", File.ReadAllText(Path.Combine(destination.FullName, "Config", "settings.json")));
            Assert.Equal("package-artefact", File.ReadAllText(Path.Combine(destination.FullName, "Artefacts", "script.ps1")));
            Assert.Equal("local-notes", File.ReadAllText(Path.Combine(destination.FullName, "Other", "notes.txt")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
