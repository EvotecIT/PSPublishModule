using System.DirectoryServices.Protocols;
using PowerForge;

if (args.Length != 2)
    throw new ArgumentException("Expected output package path and package version.");

PowerShellDirectoryRuntimeProviderPackage.Build(args[0], args[1]);

/// <summary>Builds the distributable executable directory-provider package consumed by compiler projects.</summary>
public static class PowerShellDirectoryRuntimeProviderPackage
{
    /// <summary>Stable NuGet and provider-package identity.</summary>
    public const string PackageId = "PowerForge.PowerShell.Provider.Directory.Runtime";

    /// <summary>Creates and immediately validates one exact win-x64 provider package.</summary>
    public static PowerShellCompilationProviderResolution Build(string outputPath, string version)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("An output package path is required.", nameof(outputPath));
        if (!Version.TryParse(version, out var parsedVersion) || parsedVersion.Build < 0 || parsedVersion.Revision >= 0)
            throw new ArgumentException("A three-part provider package version is required.", nameof(version));

        var providerAssembly = typeof(PowerShellDirectoryProviderEntryPoints).Assembly.Location;
        var protocolsAssembly = typeof(LdapConnection).Assembly.Location;
        RequireFile(providerAssembly);
        RequireFile(protocolsAssembly);
        var manifest = new PowerShellCompilationProviderPackageManifest
        {
            PackageId = PackageId,
            PackageVersion = version,
            Publisher = "EvotecIT",
            LicenseExpression = "MIT",
            Redistributable = true,
            SupportedRuntimeIdentifiers = new[] { "win-x64" },
            SourceSemanticProfiles = new[] { PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId },
            SemanticProfiles = new[]
            {
                PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" +
                PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion
            },
            Providers = CreateProviders()
        };
        return new PowerShellCompilationProviderPackageBuilder().Build(
            new PowerShellCompilationProviderPackageBuildRequest(Path.GetFullPath(outputPath), manifest)
            {
                Assemblies = new[]
                {
                    new PowerShellCompilationProviderAssemblyInput(
                        providerAssembly,
                        "lib/net10.0/PowerForge.PowerShell.Provider.Directory.dll"),
                    new PowerShellCompilationProviderAssemblyInput(
                        protocolsAssembly,
                        "lib/net10.0/System.DirectoryServices.Protocols.dll")
                }
            });
    }

    /// <summary>Creates the exact command-provider contracts published in the package manifest.</summary>
    public static PowerShellCompilationCommandProviderContract[] CreateProviders()
        => new[]
        {
            Provider("search", "Invoke-DirectorySearchCore", "Search"),
            Provider("read", "Invoke-DirectoryReadCore", "Read"),
            Provider("add", "Invoke-DirectoryAddCore", "Add"),
            Provider("modify", "Invoke-DirectoryModifyCore", "Modify"),
            Provider("delete", "Invoke-DirectoryDeleteCore", "Delete"),
            Provider("rename", "Invoke-DirectoryRenameCore", "ModifyDistinguishedName"),
            Provider("compare", "Invoke-DirectoryCompareCore", "Compare")
        };

    private static PowerShellCompilationCommandProviderContract Provider(string id, string commandName, string methodName)
        => new()
        {
            ProviderId = "powerforge.directory." + id,
            ProviderVersion = "1.0",
            FeatureId = "directory." + id,
            Family = PowerShellCompilationCommandFamily.ExternalOperation,
            CommandName = commandName,
            Parameters = new[] { new PowerShellCompilationCommandParameterContract { Name = "RequestJson", Position = 0 } },
            Output = PowerShellCompilationCommandOutput.Projected,
            Cardinality = PowerShellCompilationCommandCardinality.Scalar,
            Stream = "Success",
            Errors = PowerShellCompilationCommandErrors.Terminating,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = "Directory." + methodName,
                SemanticProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" +
                                  PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                RuntimeFree = true,
                AotCompatible = false,
                Cancellation = PowerShellCompilationProviderCancellation.ProcessIsolated,
                ProcessIsolationTimeoutSeconds = 45,
                Cleanup = PowerShellCompilationProviderCleanup.Deterministic,
                Dependencies = new[] { "System.DirectoryServices.Protocols" },
                EntryPoint = new PowerShellCompilationProviderAdapterEntryPoint
                {
                    AssemblyPath = "lib/net10.0/PowerForge.PowerShell.Provider.Directory.dll",
                    TypeName = typeof(PowerShellDirectoryProviderEntryPoints).FullName!,
                    MethodName = methodName,
                    ResultType = PowerShellCompilationProviderValueType.String
                }
            }
        };

    private static void RequireFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Directory-provider package input was not found.", path);
    }
}
