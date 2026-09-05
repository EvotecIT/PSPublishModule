using Microsoft.Management.Infrastructure;
using PowerForge;

if (args.Length != 2)
    throw new ArgumentException("Expected output package path and package version.");

PowerShellManagementRuntimeProviderPackage.Build(args[0], args[1]);

/// <summary>Builds the distributable executable management-provider package consumed by compiler projects.</summary>
public static class PowerShellManagementRuntimeProviderPackage
{
    /// <summary>Stable NuGet and provider-package identity.</summary>
    public const string PackageId = "PowerForge.PowerShell.Provider.Management.Runtime";

    /// <summary>Creates and immediately validates one exact win-x64 provider package.</summary>
    public static PowerShellCompilationProviderResolution Build(string outputPath, string version)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("An output package path is required.", nameof(outputPath));
        if (!Version.TryParse(version, out var parsedVersion) || parsedVersion.Build < 0 || parsedVersion.Revision >= 0)
            throw new ArgumentException("A three-part provider package version is required.", nameof(version));

        var managementAssembly = typeof(PowerShellManagementProviderEntryPoints).Assembly.Location;
        var miAssembly = typeof(CimSession).Assembly.Location;
        var miDirectory = Path.GetDirectoryName(miAssembly)!;
        var managedBridge = Path.Combine(miDirectory, "microsoft.management.infrastructure.native.dll");
        var nativeBridge = Path.GetFullPath(Path.Combine(
            miDirectory,
            "..", "..", "native",
            "microsoft.management.infrastructure.native.unmanaged.dll"));
        RequireFile(managementAssembly);
        RequireFile(miAssembly);
        RequireFile(managedBridge);
        RequireFile(nativeBridge);

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
                        managementAssembly,
                        "lib/net10.0/PowerForge.PowerShell.Provider.Management.dll"),
                    new PowerShellCompilationProviderAssemblyInput(
                        miAssembly,
                        "lib/net10.0/microsoft.management.infrastructure.dll"),
                    new PowerShellCompilationProviderAssemblyInput(
                        managedBridge,
                        "lib/net10.0/microsoft.management.infrastructure.native.dll")
                },
                NativeAssets = new[]
                {
                    new PowerShellCompilationProviderNativeAssetInput(
                        nativeBridge,
                        "runtimes/win-x64/native/microsoft.management.infrastructure.native.unmanaged.dll",
                        "win-x64")
                }
            });
    }

    /// <summary>Creates the exact command-provider contracts published in the package manifest.</summary>
    public static PowerShellCompilationCommandProviderContract[] CreateProviders()
        => new[]
        {
            Provider("query", "Invoke-ManagementQueryCore", "Query"),
            Provider("enumerate", "Invoke-ManagementEnumerateCore", "Enumerate"),
            Provider("get", "Invoke-ManagementGetCore", "Get"),
            Provider("create", "Invoke-ManagementCreateCore", "Create"),
            Provider("modify", "Invoke-ManagementModifyCore", "Modify"),
            Provider("delete", "Invoke-ManagementDeleteCore", "Delete"),
            Provider("method", "Invoke-ManagementMethodCore", "InvokeMethod"),
            Provider("association", "Invoke-ManagementAssociationCore", "Association"),
            Provider("subscription", "Invoke-ManagementSubscriptionCore", "Subscription")
        };

    private static PowerShellCompilationCommandProviderContract Provider(string id, string commandName, string methodName)
        => new()
        {
            ProviderId = "powerforge.management." + id,
            ProviderVersion = "2.0",
            FeatureId = "management." + id,
            Family = PowerShellCompilationCommandFamily.ExternalOperation,
            CommandName = commandName,
            Parameters = new[] { new PowerShellCompilationCommandParameterContract { Name = "RequestJson", Position = 0 } },
            Output = PowerShellCompilationCommandOutput.Projected,
            Cardinality = PowerShellCompilationCommandCardinality.Scalar,
            Stream = "Success",
            Errors = PowerShellCompilationCommandErrors.Terminating,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = "Management." + methodName,
                SemanticProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" +
                                  PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                RuntimeFree = true,
                AotCompatible = false,
                Cancellation = PowerShellCompilationProviderCancellation.Cooperative,
                Cleanup = PowerShellCompilationProviderCleanup.Deterministic,
                Dependencies = new[]
                {
                    "Microsoft.Management.Infrastructure",
                    "Microsoft.Management.Infrastructure.Native"
                },
                EntryPoint = new PowerShellCompilationProviderAdapterEntryPoint
                {
                    AssemblyPath = "lib/net10.0/PowerForge.PowerShell.Provider.Management.dll",
                    TypeName = typeof(PowerShellManagementProviderEntryPoints).FullName!,
                    MethodName = methodName,
                    ResultType = PowerShellCompilationProviderValueType.String
                }
            }
        };

    private static void RequireFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Management-provider package input was not found.", path);
    }
}
