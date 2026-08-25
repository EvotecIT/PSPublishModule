namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly IReadOnlyDictionary<string, string[]> ControlledTaskFileInputAttributes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AddToWin32Manifest"] = ["ApplicationManifest"],
            ["AL"] = ["EmbedResources", "EvidenceFile", "KeyFile", "LinkResources", "ResponseFiles", "SourceModules", "Sources", "TemplateFile", "Win32Icon", "Win32Resource"],
            ["AspNetCompiler"] = ["KeyFile", "PhysicalPath"],
            ["Copy"] = ["SourceFiles", "SourceFolders"],
            ["CreateCSharpManifestResourceName"] = ["ResourceFiles"],
            ["CreateVisualBasicManifestResourceName"] = ["ResourceFiles"],
            ["Csc"] = ["AdditionalFiles", "AdditionalLibPaths", "AddModules", "AnalyzerConfigFiles", "Analyzers", "ApplicationConfiguration", "CodeAnalysisRuleSet", "EmbeddedFiles", "KeyFile", "LinkResources", "PotentialAnalyzerConfigFiles", "References", "Resources", "ResponseFiles", "SourceLink", "Sources", "TestCoverageModulePaths", "Win32AppConfig", "Win32Icon", "Win32Manifest", "Win32Resource"],
            ["Fsc"] = ["AdditionalLibPaths", "AnalyzerConfigFiles", "Analyzers", "Embed", "KeyFile", "OtherFlags", "ReferencePath", "References", "Resources", "ResponseFiles", "SourceLink", "Sources", "TestCoverageModulePaths", "VersionFile", "Win32Icon", "Win32IconFile", "Win32ManifestFile", "Win32Resource", "Win32ResourceFile"],
            ["GenerateApplicationManifest"] = ["ConfigFile", "Dependencies", "EntryPoint", "Files", "IconFile", "InputManifest", "IsolatedComReferences", "TrustInfoFile"],
            ["GenerateBindingRedirects"] = ["AppConfigFile", "SuggestedRedirects"],
            ["GenerateDeploymentManifest"] = ["EntryPoint", "InputManifest"],
            ["GenerateResource"] = ["AdditionalInputs", "References", "Sources", "StateFile"],
            ["GenerateTrustInfo"] = ["ApplicationDependencies", "BaseManifest"],
            ["GetAssemblyIdentity"] = ["AssemblyFiles"],
            ["GetFileHash"] = ["Files"],
            ["Hash"] = ["Items", "ItemsToHash"],
            ["LC"] = ["KeyFile", "LicenseTarget", "ReferencedAssemblies", "Sources"],
            ["Move"] = ["SourceFiles"],
            ["RegisterAssembly"] = ["Assemblies", "AssemblyListFile"],
            ["RequiresFramework35SP1Assembly"] = ["Assemblies", "DeploymentManifestEntryPoint", "EntryPoint", "Files", "ReferencedAssemblies"],
            ["ResolveCodeAnalysisRuleSet"] = ["CodeAnalysisRuleSet"],
            ["ResolveKeySource"] = ["CertificateFile", "KeyFile"],
            ["ResolveManifestFiles"] = ["DeploymentManifestEntryPoint", "EntryPoint", "ExtraFiles", "Files", "ManagedAssemblies", "NativeAssemblies", "PublishFiles", "RuntimePackAssets", "SatelliteAssemblies"],
            ["SGen"] = ["BuildAssemblyPath", "KeyFile", "References"],
            ["SignFile"] = ["SigningTarget"],
            ["UnregisterAssembly"] = ["Assemblies", "AssemblyListFile", "TypeLibFiles"],
            ["Unzip"] = ["SourceFiles"],
            ["UpdateManifest"] = ["ApplicationManifest", "InputManifest"],
            ["Vbc"] = ["AdditionalFiles", "AdditionalLibPaths", "AddModules", "AnalyzerConfigFiles", "Analyzers", "ApplicationConfiguration", "CodeAnalysisRuleSet", "EmbeddedFiles", "Imports", "KeyFile", "LinkResources", "PotentialAnalyzerConfigFiles", "References", "Resources", "ResponseFiles", "SdkPath", "SourceLink", "Sources", "TestCoverageModulePaths", "VBRuntimePath", "Win32AppConfig", "Win32Icon", "Win32Manifest", "Win32Resource"],
            ["VerifyFileHash"] = ["File"],
            ["WinMDExp"] = ["InputDocumentationFile", "InputPDBFile", "References", "WinMDModule"],
            ["XmlPoke"] = ["XmlInputPath"],
            ["XslTransformation"] = ["XmlInputPaths", "XslCompiledDllPath", "XslInputPath"],
            ["ZipDirectory"] = ["SourceDirectory"]
        };

    private static readonly IReadOnlyDictionary<string, string[]> ControlledTaskDirectoryInputAttributes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AspNetCompiler"] = ["PhysicalPath"],
            ["Copy"] = ["SourceFolders"],
            ["Csc"] = ["AdditionalLibPaths"],
            ["Fsc"] = ["AdditionalLibPaths"],
            ["Vbc"] = ["AdditionalLibPaths", "SdkPath"],
            ["ZipDirectory"] = ["SourceDirectory"]
        };

    private static readonly IReadOnlyDictionary<string, string[]> ControlledTaskFileOutputAttributes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AddToWin32Manifest"] = ["ManifestPath"],
            ["AL"] = ["OutputAssembly"],
            ["Copy"] = ["DestinationFiles", "DestinationFolder"],
            ["Csc"] = ["DocumentationFile", "ErrorLog", "GeneratedFilesOutputPath", "OutputAssembly", "OutputRefAssembly", "PdbFile", "TouchedFilesPath"],
            ["Delete"] = ["Files"],
            ["Fsc"] = ["DocumentationFile", "ErrorLog", "GenerateInterfaceFile", "GeneratedFilesOutputPath", "OutputAssembly", "OutputRefAssembly", "PdbFile", "TouchedFilesPath"],
            ["GenerateApplicationManifest"] = ["OutputManifest"],
            ["GenerateBindingRedirects"] = ["OutputAppConfigFile"],
            ["GenerateDeploymentManifest"] = ["OutputManifest"],
            ["GenerateResource"] = ["OutputResources", "StateFile", "StronglyTypedFileName"],
            ["GenerateTrustInfo"] = ["TrustInfoFile"],
            ["LC"] = ["OutputLicense"],
            ["MakeDir"] = ["Directories"],
            ["Move"] = ["DestinationFiles"],
            ["RemoveDir"] = ["Directories"],
            ["Touch"] = ["Files"],
            ["Unzip"] = ["DestinationFolder"],
            ["UpdateManifest"] = ["OutputManifest"],
            ["Vbc"] = ["DocumentationFile", "ErrorLog", "GeneratedFilesOutputPath", "OutputAssembly", "OutputRefAssembly", "PdbFile", "TouchedFilesPath"],
            ["WinMDExp"] = ["OutputWindowsMetadataFile"],
            ["WriteLinesToFile"] = ["File"],
            ["XmlPoke"] = ["XmlInputPath"],
            ["XslTransformation"] = ["OutputPaths"],
            ["ZipDirectory"] = ["DestinationFile"]
        };

    private static readonly ISet<string> ControlledTasksWithoutFilePaths =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AssignCulture",
            "AssignTargetPath",
            "CreateItem",
            "CreateProperty",
            "Error",
            "Message",
            "RemoveDuplicates",
            "Warning"
        };
}
