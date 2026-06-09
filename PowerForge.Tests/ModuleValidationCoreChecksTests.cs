using System;
using System.IO;
using System.Management.Automation;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleValidationCoreChecksTests
{
    [Fact]
    public void ValidateStructure_ReportsExportAndManifestFileIssues()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Public"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "Internal"));

            File.WriteAllText(Path.Combine(root.FullName, "Public", "Get-PublicThing.ps1"), "function Get-PublicThing { }");
            File.WriteAllText(Path.Combine(root.FullName, "Internal", "Invoke-HiddenThing.ps1"), "function Invoke-HiddenThing { }");

            var manifestPath = Path.Combine(root.FullName, "SampleModule.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'SampleModule.psm1'
    FunctionsToExport = @('Invoke-HiddenThing')
    FormatsToProcess = @('SampleModule.format.ps1xml')
    TypesToProcess = @('SampleModule.types.ps1xml')
    RequiredAssemblies = @('MissingBinary.dll')
}
""");

            var result = ModuleValidationCoreChecks.ValidateStructure(
                new ModuleValidationSpec
                {
                    ProjectRoot = root.FullName,
                    StagingPath = root.FullName,
                    ManifestPath = manifestPath
                },
                new ModuleStructureValidationSettings
                {
                    Severity = ValidationSeverity.Warning,
                    PublicFunctionPaths = new[] { "Public" },
                    InternalFunctionPaths = new[] { "Internal" },
                    ValidateExports = true,
                    ValidateInternalNotExported = true,
                    ValidateManifestFiles = true,
                    AllowWildcardExports = false
                });

            Assert.NotNull(result);
            Assert.Equal("Module structure", result!.Name);
            Assert.Equal(CheckStatus.Warning, result.Status);
            Assert.Contains("public functions 1", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("internal functions 1", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.Issues, issue => issue.Contains("Public functions not exported: Get-PublicThing", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("Exports not found in public folder: Invoke-HiddenThing", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("Internal functions exported: Invoke-HiddenThing", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("RootModule missing: SampleModule.psm1", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("Format file missing: SampleModule.format.ps1xml", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("Type file missing: SampleModule.types.ps1xml", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("Required assembly missing: MissingBinary.dll", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateStructure_SkipsStrictExportComparisonForMixedManifestExpressions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Public"));
            File.WriteAllText(Path.Combine(root.FullName, "Public", "Get-PublicThing.ps1"), "function Get-PublicThing { }");

            var manifestPath = Path.Combine(root.FullName, "SampleModule.psd1");
            File.WriteAllText(manifestPath, """
@{
    FunctionsToExport = @('Get-PublicThing', $DynamicExport)
}
""");

            var result = ModuleValidationCoreChecks.ValidateStructure(
                new ModuleValidationSpec
                {
                    ProjectRoot = root.FullName,
                    StagingPath = root.FullName,
                    ManifestPath = manifestPath
                },
                new ModuleStructureValidationSettings
                {
                    Severity = ValidationSeverity.Warning,
                    PublicFunctionPaths = new[] { "Public" },
                    InternalFunctionPaths = Array.Empty<string>(),
                    ValidateExports = true,
                    ValidateInternalNotExported = false,
                    ValidateManifestFiles = false,
                    AllowWildcardExports = false
                });

            Assert.NotNull(result);
            Assert.Equal(CheckStatus.Pass, result!.Status);
            Assert.DoesNotContain(result.Issues, issue => issue.Contains("not exported", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Issues, issue => issue.Contains("public folder", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateBinary_ReportsManifestExportMismatchAgainstDetectedBinaryExports()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var manifestPath = Path.Combine(root.FullName, "BinaryModule.psd1");
            File.WriteAllText(manifestPath,
                "@{" + Environment.NewLine +
                $"    RootModule = '{typeof(GetValidationExampleCommand).Assembly.Location}'" + Environment.NewLine +
                "    CmdletsToExport = @('Set-OtherCommand')" + Environment.NewLine +
                "    AliasesToExport = @('missing-alias')" + Environment.NewLine +
                "}");

            var result = ModuleValidationCoreChecks.ValidateBinary(
                new ModuleValidationSpec
                {
                    ProjectRoot = root.FullName,
                    StagingPath = root.FullName,
                    ManifestPath = manifestPath
                },
                new BinaryModuleValidationSettings
                {
                    Severity = ValidationSeverity.Warning,
                    ValidateAssembliesExist = true,
                    ValidateManifestExports = true,
                    AllowWildcardExports = false
                });

            Assert.NotNull(result);
            Assert.Equal("Binary exports", result!.Name);
            Assert.Equal(CheckStatus.Warning, result.Status);
            Assert.Contains("cmdlets", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("aliases", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.Issues, issue => issue.Contains("Binary cmdlets not exported:", StringComparison.Ordinal) &&
                                                    issue.Contains("Get-ValidationExample", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("Manifest cmdlets missing from binaries: Set-OtherCommand", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("Binary aliases not exported:", StringComparison.Ordinal) &&
                                                    issue.Contains("gvx", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("Manifest aliases missing from binaries: missing-alias", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateCsproj_ReportsMissingTargetFrameworkAndInvalidOutputType()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectPath = Path.Combine(root.FullName, "SampleModule.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>
""");

            var result = ModuleValidationCoreChecks.ValidateCsproj(
                new ModuleValidationSpec
                {
                    ProjectRoot = root.FullName,
                    BuildSpec = new ModuleBuildSpec
                    {
                        CsprojPath = "SampleModule.csproj"
                    }
                },
                new CsprojValidationSettings
                {
                    Severity = ValidationSeverity.Warning,
                    RequireTargetFramework = true,
                    RequireLibraryOutput = true
                });

            Assert.NotNull(result);
            Assert.Equal("Csproj", result!.Name);
            Assert.Equal(CheckStatus.Warning, result.Status);
            Assert.Contains(result.Issues, issue => issue.Contains("TargetFramework/TargetFrameworks not set.", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("OutputType is 'Exe' (expected Library).", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Cmdlet(VerbsCommon.Get, "ValidationExample")]
    [Alias("gvx")]
    private sealed class GetValidationExampleCommand : PSCmdlet
    {
    }
}
