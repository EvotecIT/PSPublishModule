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
            ["Csc"] = ["AdditionalFiles", "AnalyzerConfigFiles", "Analyzers", "ApplicationConfiguration", "CodeAnalysisRuleSet", "KeyFile", "LinkResources", "References", "Resources", "ResponseFiles", "SourceLink", "Sources", "TestCoverageModulePaths", "Win32AppConfig", "Win32Icon", "Win32Manifest", "Win32Resource"],
            ["Fsc"] = ["AnalyzerConfigFiles", "Analyzers", "KeyFile", "References", "Resources", "ResponseFiles", "SourceLink", "Sources", "TestCoverageModulePaths", "Win32Icon", "Win32Resource"],
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
            ["Vbc"] = ["AdditionalFiles", "AnalyzerConfigFiles", "Analyzers", "ApplicationConfiguration", "CodeAnalysisRuleSet", "Imports", "KeyFile", "LinkResources", "References", "Resources", "ResponseFiles", "SourceLink", "Sources", "TestCoverageModulePaths", "Win32AppConfig", "Win32Icon", "Win32Manifest", "Win32Resource"],
            ["VerifyFileHash"] = ["File"],
            ["WinMDExp"] = ["InputDocumentationFile", "InputPDBFile", "References", "WinMDModule"],
            ["XmlPoke"] = ["XmlInputPath"],
            ["XslTransformation"] = ["XmlInputPaths", "XslCompiledDllPath", "XslInputPath"],
            ["ZipDirectory"] = ["SourceDirectory"]
        };

    private static readonly IReadOnlyDictionary<string, string[]> ControlledTaskFileOutputAttributes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AL"] = ["OutputAssembly"],
            ["Copy"] = ["DestinationFiles", "DestinationFolder"],
            ["Csc"] = ["DocumentationFile", "ErrorLog", "GeneratedFilesOutputPath", "OutputAssembly", "OutputRefAssembly", "PdbFile", "TouchedFilesPath"],
            ["Delete"] = ["Files"],
            ["Fsc"] = ["DocumentationFile", "ErrorLog", "GeneratedFilesOutputPath", "OutputAssembly", "OutputRefAssembly", "PdbFile", "TouchedFilesPath"],
            ["GenerateResource"] = ["OutputResources"],
            ["MakeDir"] = ["Directories"],
            ["Move"] = ["DestinationFiles"],
            ["RemoveDir"] = ["Directories"],
            ["Touch"] = ["Files"],
            ["Unzip"] = ["DestinationFolder"],
            ["Vbc"] = ["DocumentationFile", "ErrorLog", "GeneratedFilesOutputPath", "OutputAssembly", "OutputRefAssembly", "PdbFile", "TouchedFilesPath"],
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
