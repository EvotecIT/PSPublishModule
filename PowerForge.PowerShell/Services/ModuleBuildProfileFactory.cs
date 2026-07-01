using System.Collections;

namespace PowerForge;

internal sealed class ModuleBuildProfileFactory
{
    public IReadOnlyList<IConfigurationSegment> Create(ModuleBuildProfileRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var segments = new List<IConfigurationSegment>();

        AddFormatting(segments);
        AddDocumentation(segments, request);
        AddValidation(segments, request);
        AddFileConsistency(segments, request);
        AddCompatibility(segments, request);
        AddImport(segments, request);
        AddBuild(segments, request);

        return segments;
    }

    private static void AddFormatting(List<IConfigurationSegment> segments)
    {
        var factory = new FormatConfigurationFactory();
        AddIfNotNull(segments, factory.Create(new FormatConfigurationRequest
        {
            ApplyTo = new[] { "OnMergePSM1", "OnMergePSD1" },
            Sort = "None",
            RemoveCommentsSpecified = true,
            RemoveComments = true,
            RemoveEmptyLinesSpecified = true,
            RemoveEmptyLines = true,
            PlaceOpenBraceEnable = true,
            PlaceOpenBraceOnSameLine = true,
            PlaceOpenBraceNewLineAfter = true,
            PlaceOpenBraceIgnoreOneLineBlock = false,
            PlaceCloseBraceEnable = true,
            PlaceCloseBraceNewLineAfter = true,
            PlaceCloseBraceIgnoreOneLineBlock = false,
            PlaceCloseBraceNoEmptyLineBefore = true,
            UseConsistentIndentationEnable = true,
            UseConsistentIndentationKind = "space",
            UseConsistentIndentationPipelineIndentation = "IncreaseIndentationAfterEveryPipeline",
            UseConsistentIndentationIndentationSize = 4,
            UseConsistentWhitespaceEnable = true,
            UseConsistentWhitespaceCheckInnerBrace = true,
            UseConsistentWhitespaceCheckOpenBrace = true,
            UseConsistentWhitespaceCheckOpenParen = true,
            UseConsistentWhitespaceCheckOperator = true,
            UseConsistentWhitespaceCheckPipe = true,
            UseConsistentWhitespaceCheckSeparator = true,
            AlignAssignmentStatementEnable = true,
            AlignAssignmentStatementCheckHashtable = true,
            UseCorrectCasingEnable = true
        }));

        AddIfNotNull(segments, factory.Create(new FormatConfigurationRequest
        {
            ApplyTo = new[] { "DefaultPSD1", "DefaultPSM1" },
            EnableFormatting = true,
            Sort = "None"
        }));

        AddIfNotNull(segments, factory.Create(new FormatConfigurationRequest
        {
            ApplyTo = new[] { "DefaultPSD1", "OnMergePSD1" },
            PSD1Style = "Minimal"
        }));
    }

    private static void AddDocumentation(List<IConfigurationSegment> segments, ModuleBuildProfileRequest request)
    {
        if (!request.Documentation)
            return;

        segments.AddRange(new DocumentationConfigurationFactory().Create(new DocumentationConfigurationRequest
        {
            Enable = true,
            SyncExternalHelpToProjectRoot = request.SyncExternalHelpToProjectRoot,
            Path = request.DocumentationPath,
            PathReadme = request.DocumentationReadmePath,
            AboutTopicsSourcePath = request.AboutTopicsSourcePath ?? Array.Empty<string>(),
            AboutTopicsSourcePathSpecified = true
        }));
    }

    private static void AddValidation(List<IConfigurationSegment> segments, ModuleBuildProfileRequest request)
    {
        if (!request.Validation)
            return;

        segments.Add(new ValidationConfigurationFactory().Create(new ValidationConfigurationRequest
        {
            Enable = true,
            StructureSeverity = ValidationSeverity.Warning,
            DocumentationSeverity = ValidationSeverity.Warning,
            ScriptAnalyzerSeverity = ValidationSeverity.Warning,
            FileIntegritySeverity = ValidationSeverity.Warning,
            EnableScriptAnalyzer = request.EnableScriptAnalyzer,
            FileIntegrityCheckTrailingWhitespace = true,
            FileIntegrityCheckSyntax = true
        }));
    }

    private static void AddFileConsistency(List<IConfigurationSegment> segments, ModuleBuildProfileRequest request)
    {
        if (!request.FileConsistency)
            return;

        segments.Add(new FileConsistencyConfigurationFactory().Create(new FileConsistencyConfigurationRequest
        {
            Enable = true,
            RequiredEncoding = request.RequiredEncoding,
            RequiredLineEnding = request.RequiredLineEnding,
            ExcludeDirectories = request.FileConsistencyExcludeDirectories ?? Array.Empty<string>(),
            ExportReport = true,
            CheckMixedLineEndings = true,
            CheckMissingFinalNewline = true,
            Scope = FileConsistencyScope.StagingAndProject,
            ScopeSpecified = true,
            EncodingOverrides = request.EncodingOverrides ?? new Hashtable { ["*.xml"] = FileConsistencyEncoding.UTF8 }
        }));
    }

    private static void AddCompatibility(List<IConfigurationSegment> segments, ModuleBuildProfileRequest request)
    {
        if (!request.Compatibility)
            return;

        segments.Add(new ConfigurationCompatibilitySegment
        {
            Settings = new CompatibilitySettings
            {
                Enable = true,
                RequireCrossCompatibility = true,
                MinimumCompatibilityPercentage = request.MinimumCompatibilityPercentage,
                ExportReport = true
            }
        });
    }

    private static void AddImport(List<IConfigurationSegment> segments, ModuleBuildProfileRequest request)
    {
        if (!request.ImportSelf && !request.ImportRequiredModules)
            return;

        segments.Add(new ConfigurationImportModulesSegment
        {
            ImportModules = new ImportModulesConfiguration
            {
                Self = request.ImportSelf,
                RequiredModules = request.ImportRequiredModules
            }
        });
    }

    private static void AddBuild(List<IConfigurationSegment> segments, ModuleBuildProfileRequest request)
    {
        if (request.Profile == ModuleBuildProfileKind.Binary &&
            (string.IsNullOrWhiteSpace(request.NETProjectPath) || string.IsNullOrWhiteSpace(request.NETProjectName)))
        {
            throw new ArgumentException("NETProjectPath and NETProjectName are required for the Binary module build profile.", nameof(request));
        }

        var buildRequest = new BuildConfigurationRequest
        {
            EnableSpecified = true,
            Enable = true,
            MergeModuleOnBuildSpecified = true,
            MergeModuleOnBuild = request.MergeModuleOnBuild,
            MergeFunctionsFromApprovedModulesSpecified = request.MergeFunctionsFromApprovedModulesSpecified,
            MergeFunctionsFromApprovedModules = request.MergeFunctionsFromApprovedModules,
            SignModuleSpecified = true,
            SignModule = request.SignModule,
            DoNotAttemptToFixRelativePathsSpecified = true,
            DoNotAttemptToFixRelativePaths = request.DoNotAttemptToFixRelativePaths,
            SkipBuiltinReplacementsSpecified = request.SkipBuiltinReplacements,
            SkipBuiltinReplacements = request.SkipBuiltinReplacements,
            DotSourceLibrariesSpecified = request.DotSourceLibraries,
            DotSourceLibraries = request.DotSourceLibraries,
            DotSourceClassesSpecified = request.DotSourceClasses,
            DotSourceClasses = request.DotSourceClasses,
            InstallMissingModulesSpecified = true,
            InstallMissingModules = request.InstallMissingModules,
            VersionedInstallStrategySpecified = true,
            VersionedInstallStrategy = request.VersionedInstallStrategy,
            VersionedInstallKeepSpecified = true,
            VersionedInstallKeep = request.VersionedInstallKeep,
            KillLockersBeforeInstallSpecified = request.KillLockersBeforeInstall,
            KillLockersBeforeInstall = request.KillLockersBeforeInstall,
            KillLockersForceSpecified = request.KillLockersForce,
            KillLockersForce = request.KillLockersForce,
            CertificateThumbprintSpecified = !string.IsNullOrWhiteSpace(request.CertificateThumbprint),
            CertificateThumbprint = request.CertificateThumbprint,
            SignIncludeBinariesSpecified = request.SignIncludeBinariesSpecified,
            SignIncludeBinaries = request.SignIncludeBinaries,
            SignIncludeInternalsSpecified = request.SignIncludeInternalsSpecified,
            SignIncludeInternals = request.SignIncludeInternals,
            SignIncludeExeSpecified = request.SignIncludeExeSpecified,
            SignIncludeExe = request.SignIncludeExe
        };

        if (request.Profile == ModuleBuildProfileKind.Binary)
        {
            buildRequest.NETProjectPathSpecified = true;
            buildRequest.NETProjectPath = request.NETProjectPath;
            buildRequest.NETProjectNameSpecified = true;
            buildRequest.NETProjectName = request.NETProjectName;
            buildRequest.NETConfigurationSpecified = true;
            buildRequest.NETConfiguration = request.NETConfiguration;
            buildRequest.NETFrameworkSpecified = true;
            buildRequest.NETFramework = request.NETFramework.Length == 0 ? new[] { "net8.0", "net472" } : request.NETFramework;
            buildRequest.NETHandleAssemblyWithSameNameSpecified = request.NETHandleAssemblyWithSameName;
            buildRequest.NETHandleAssemblyWithSameName = request.NETHandleAssemblyWithSameName;
            buildRequest.NETAssemblyLoadContextSpecified = request.NETAssemblyLoadContext;
            buildRequest.NETAssemblyLoadContext = request.NETAssemblyLoadContext;
            buildRequest.NETResolveBinaryConflictsSpecified = request.ResolveBinaryConflicts;
            buildRequest.NETResolveBinaryConflicts = request.ResolveBinaryConflicts;
            buildRequest.NETResolveBinaryConflictsNameSpecified = !string.IsNullOrWhiteSpace(request.ResolveBinaryConflictsName);
            buildRequest.NETResolveBinaryConflictsName = request.ResolveBinaryConflictsName;
            buildRequest.NETAssemblyTypeAcceleratorModeSpecified = request.NETAssemblyTypeAcceleratorMode.HasValue;
            buildRequest.NETAssemblyTypeAcceleratorMode = request.NETAssemblyTypeAcceleratorMode;
            buildRequest.NETAssemblyTypeAcceleratorsSpecified = request.NETAssemblyTypeAccelerators is { Length: > 0 };
            buildRequest.NETAssemblyTypeAccelerators = request.NETAssemblyTypeAccelerators;
            buildRequest.NETAssemblyTypeAcceleratorAssembliesSpecified = request.NETAssemblyTypeAcceleratorAssemblies is { Length: > 0 };
            buildRequest.NETAssemblyTypeAcceleratorAssemblies = request.NETAssemblyTypeAcceleratorAssemblies;
        }

        foreach (var segment in new BuildConfigurationFactory().Create(buildRequest))
            segments.Add(segment);
    }

    private static void AddIfNotNull(List<IConfigurationSegment> segments, IConfigurationSegment? segment)
    {
        if (segment is not null)
            segments.Add(segment);
    }
}
