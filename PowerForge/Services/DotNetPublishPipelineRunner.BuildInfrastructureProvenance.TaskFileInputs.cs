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
            ["Csc"] = ["AdditionalFiles", "Analyzers", "ApplicationConfiguration", "CodeAnalysisRuleSet", "KeyFile", "LinkResources", "References", "Resources", "ResponseFiles", "Sources", "Win32Icon", "Win32Manifest", "Win32Resource"],
            ["Fsc"] = ["KeyFile", "References", "Resources", "ResponseFiles", "Sources", "Win32Icon", "Win32Resource"],
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
            ["Vbc"] = ["AdditionalFiles", "Analyzers", "ApplicationConfiguration", "CodeAnalysisRuleSet", "Imports", "KeyFile", "LinkResources", "References", "Resources", "ResponseFiles", "Sources", "Win32Icon", "Win32Manifest", "Win32Resource"],
            ["VerifyFileHash"] = ["File"],
            ["WinMDExp"] = ["InputDocumentationFile", "InputPDBFile", "References", "WinMDModule"],
            ["XmlPoke"] = ["XmlInputPath"],
            ["XslTransformation"] = ["XmlInputPaths", "XslCompiledDllPath", "XslInputPath"],
            ["ZipDirectory"] = ["SourceDirectory"]
        };
}
