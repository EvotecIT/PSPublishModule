using PowerForge;

namespace PowerForge.Tests;

public sealed class ValidationConfigurationFactoryTests
{
    [Fact]
    public void Create_builds_validation_segment_from_request()
    {
        var factory = new ValidationConfigurationFactory();

        var segment = factory.Create(new ValidationConfigurationRequest
        {
            Enable = true,
            StructureSeverity = ValidationSeverity.Error,
            DocumentationSeverity = ValidationSeverity.Warning,
            ScriptAnalyzerSeverity = ValidationSeverity.Error,
            EnableScriptAnalyzer = true,
            ScriptAnalyzerInstallIfUnavailable = true,
            ScriptAnalyzerTimeoutSeconds = 0,
            FileIntegritySeverity = ValidationSeverity.Error,
            BannedCommands = new[] { "Invoke-Expression" },
            TestsSeverity = ValidationSeverity.Error,
            EnableTests = true,
            TestTimeoutSeconds = 0,
            TestSkipDependencies = true,
            BinarySeverity = ValidationSeverity.Warning,
            ValidateBinaryAssemblies = false,
            CsprojSeverity = ValidationSeverity.Warning,
            RequireLibraryOutput = false
        });

        Assert.True(segment.Settings.Enable);
        Assert.Equal(ValidationSeverity.Error, segment.Settings.Structure.Severity);
        Assert.True(segment.Settings.ScriptAnalyzer.Enable);
        Assert.True(segment.Settings.ScriptAnalyzer.InstallIfUnavailable);
        Assert.Equal(1, segment.Settings.ScriptAnalyzer.TimeoutSeconds);
        Assert.Equal("Invoke-Expression", Assert.Single(segment.Settings.FileIntegrity.BannedCommands));
        Assert.True(segment.Settings.Tests.Enable);
        Assert.True(segment.Settings.Tests.SkipDependencies);
        Assert.Equal(1, segment.Settings.Tests.TimeoutSeconds);
        Assert.False(segment.Settings.Binary.ValidateAssembliesExist);
        Assert.False(segment.Settings.Csproj.RequireLibraryOutput);
    }

    [Fact]
    public void Create_normalizes_null_arrays_to_empty()
    {
        var factory = new ValidationConfigurationFactory();

        var segment = factory.Create(new ValidationConfigurationRequest
        {
            PublicFunctionPaths = null!,
            InternalFunctionPaths = null!,
            ExcludeCommands = null!,
            ScriptAnalyzerExcludeDirectories = null!,
            ScriptAnalyzerExcludeRules = null!,
            FileIntegrityExcludeDirectories = null!,
            BannedCommands = null!,
            AllowBannedCommandsIn = null!,
            TestAdditionalModules = null!,
            TestSkipModules = null!
        });

        Assert.Empty(segment.Settings.Structure.PublicFunctionPaths);
        Assert.Empty(segment.Settings.Structure.InternalFunctionPaths);
        Assert.Empty(segment.Settings.Documentation.ExcludeCommands);
        Assert.Empty(segment.Settings.ScriptAnalyzer.ExcludeDirectories);
        Assert.Empty(segment.Settings.ScriptAnalyzer.ExcludeRules);
        Assert.Empty(segment.Settings.FileIntegrity.ExcludeDirectories);
        Assert.Empty(segment.Settings.FileIntegrity.BannedCommands);
        Assert.Empty(segment.Settings.FileIntegrity.AllowBannedCommandsIn);
        Assert.Empty(segment.Settings.Tests.AdditionalModules);
        Assert.Empty(segment.Settings.Tests.SkipModules);
    }
}
