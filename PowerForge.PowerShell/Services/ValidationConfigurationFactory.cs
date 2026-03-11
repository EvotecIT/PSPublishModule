using System;

namespace PowerForge;

internal sealed class ValidationConfigurationFactory
{
    public ConfigurationValidationSegment Create(ValidationConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return new ConfigurationValidationSegment
        {
            Settings = new ModuleValidationSettings
            {
                Enable = request.Enable,
                Structure = new ModuleStructureValidationSettings
                {
                    Severity = request.StructureSeverity,
                    PublicFunctionPaths = request.PublicFunctionPaths ?? Array.Empty<string>(),
                    InternalFunctionPaths = request.InternalFunctionPaths ?? Array.Empty<string>(),
                    ValidateManifestFiles = request.ValidateManifestFiles,
                    ValidateExports = request.ValidateExports,
                    ValidateInternalNotExported = request.ValidateInternalNotExported,
                    AllowWildcardExports = request.AllowWildcardExports
                },
                Documentation = new DocumentationValidationSettings
                {
                    Severity = request.DocumentationSeverity,
                    MinSynopsisPercent = request.MinSynopsisPercent,
                    MinDescriptionPercent = request.MinDescriptionPercent,
                    MinExampleCountPerCommand = request.MinExamplesPerCommand,
                    ExcludeCommands = request.ExcludeCommands ?? Array.Empty<string>()
                },
                ScriptAnalyzer = new ScriptAnalyzerValidationSettings
                {
                    Severity = request.ScriptAnalyzerSeverity,
                    Enable = request.EnableScriptAnalyzer,
                    ExcludeDirectories = request.ScriptAnalyzerExcludeDirectories ?? Array.Empty<string>(),
                    ExcludeRules = request.ScriptAnalyzerExcludeRules ?? Array.Empty<string>(),
                    SkipIfUnavailable = request.ScriptAnalyzerSkipIfUnavailable,
                    TimeoutSeconds = Math.Max(1, request.ScriptAnalyzerTimeoutSeconds)
                },
                FileIntegrity = new FileIntegrityValidationSettings
                {
                    Severity = request.FileIntegritySeverity,
                    ExcludeDirectories = request.FileIntegrityExcludeDirectories ?? Array.Empty<string>(),
                    CheckTrailingWhitespace = request.FileIntegrityCheckTrailingWhitespace,
                    CheckSyntax = request.FileIntegrityCheckSyntax,
                    BannedCommands = request.BannedCommands ?? Array.Empty<string>(),
                    AllowBannedCommandsIn = request.AllowBannedCommandsIn ?? Array.Empty<string>()
                },
                Tests = new TestSuiteValidationSettings
                {
                    Severity = request.TestsSeverity,
                    Enable = request.EnableTests,
                    TestPath = request.TestsPath,
                    AdditionalModules = request.TestAdditionalModules ?? Array.Empty<string>(),
                    SkipModules = request.TestSkipModules ?? Array.Empty<string>(),
                    SkipDependencies = request.TestSkipDependencies,
                    SkipImport = request.TestSkipImport,
                    Force = request.TestForce,
                    TimeoutSeconds = Math.Max(1, request.TestTimeoutSeconds)
                },
                Binary = new BinaryModuleValidationSettings
                {
                    Severity = request.BinarySeverity,
                    ValidateAssembliesExist = request.ValidateBinaryAssemblies,
                    ValidateManifestExports = request.ValidateBinaryExports,
                    AllowWildcardExports = request.AllowBinaryWildcardExports
                },
                Csproj = new CsprojValidationSettings
                {
                    Severity = request.CsprojSeverity,
                    RequireTargetFramework = request.RequireTargetFramework,
                    RequireLibraryOutput = request.RequireLibraryOutput
                }
            }
        };
    }
}
