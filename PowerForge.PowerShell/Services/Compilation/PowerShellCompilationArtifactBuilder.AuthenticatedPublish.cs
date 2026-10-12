using System.Text.Json;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static bool RequiresAuthenticatedSdkPublish(PowerShellCompilationBuildSpec spec)
        => spec.Mode == PowerShellCompilationMode.Strict && spec.Kind == PowerShellCompilationArtifactKind.Executable &&
           spec.Optimization == PowerShellCompilationExecutableOptimization.Trimmed;

    /// <summary>Prepares a fresh SDK producer from the authenticated packages selected by the actual restore.</summary>
    private static AuthenticatedSdkPublish PrepareAuthenticatedSdkPublish(
        PowerShellCompilationBuildSpec spec, string workspace, string projectPath, string runtimeIdentifier,
        PowerShellCompilationDependencyGraph graph, PowerShellCompilationToolchainEvidence toolchain,
        PowerShellCompilationProviderLock providerLock, CancellationToken cancellationToken)
    {
        PowerShellCompilationRuntimePackAuthority.EnsureInputOwnership(graph, providerLock);
        var linkerVersion = PowerShellCompilationToolchainFingerprint.ResolveILLinkPackVersion(spec.TargetFramework);
        var resolved = PowerShellCompilationResolvedPackageCatalog.ReadAndVerify(workspace, graph, spec.NuGetLockFilePath);
        EnsureOnlySdkLinker(resolved, linkerVersion);
        var authority = PowerShellCompilationRuntimePackAuthority.Authenticate(graph,
            spec.TargetFramework, runtimeIdentifier, spec.NuGetPackageRoot, cancellationToken);
        var packages = authority.Packages.ToDictionary(static package => package.Id + "/" + package.Version,
            StringComparer.OrdinalIgnoreCase);
        var linker = PowerShellCompilationRuntimePackAuthority.AuthenticateMicrosoftPackage(
            "Microsoft.NET.ILLink.Tasks", linkerVersion, spec.NuGetPackageRoot, cancellationToken);
        packages.Add(linker.Id + "/" + linker.Version, linker);
        // Runtime download dependencies are a separate SDK/NuGet restore output, not the
        // libraries collection. Authenticate their exact selection without evaluating MSBuild.
        using (var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "obj", "project.assets.json"))))
        {
            var framework = assets.RootElement.GetProperty("project").GetProperty("frameworks").GetProperty(spec.TargetFramework);
            foreach (var download in framework.GetProperty("downloadDependencies").EnumerateArray())
            {
                var id = download.GetProperty("name").GetString()!;
                var range = VersionRange.Parse(download.GetProperty("version").GetString()!);
                if (range.MinVersion is null || !range.MinVersion.Equals(range.MaxVersion) ||
                    !range.IsMinInclusive || !range.IsMaxInclusive)
                    throw new InvalidOperationException($"Authenticated SDK publication requires an exact download package version for '{id}'.");
                var version = range.MinVersion.ToNormalizedString();
                var key = id + "/" + version;
                if (!packages.ContainsKey(key)) packages.Add(key,
                    PowerShellCompilationRuntimePackAuthority.AuthenticateMicrosoftPackage(id, version, spec.NuGetPackageRoot, cancellationToken));
            }
        }
        var packageRoot = Path.Combine(workspace, "authenticated-packages");
        Directory.CreateDirectory(packageRoot);
        foreach (var package in packages.Values)
            package.InstallAsync(packageRoot, cancellationToken).GetAwaiter().GetResult();
        // Explicit configuration, no feed/fallback folder, and a new root ensure the
        // publish cannot consume caller-selected extracted tool/runtime entries.
        PowerShellCompilationBuildIsolation.Write(workspace, requireSdkSelection: true,
            offlineRestore: true, lockedRestore: !string.IsNullOrWhiteSpace(spec.NuGetLockFilePath));
        // The first restore's generated imports still point to the caller's extracted
        // package root. Preserve its selection evidence, but never import it while
        // restoring or publishing with authenticated snapshots.
        Directory.Move(Path.Combine(workspace, "obj"), Path.Combine(workspace, "selected-restore"));
        var restore = RunDotNetRestore(spec, projectPath, runtimeIdentifier, cancellationToken, packageRoot);
        if (restore.TimedOut || restore.ExitCode != 0)
            throw new InvalidOperationException("Authenticated SDK isolated restore failed. " + BoundOutput(restore.Output));
        EnsureOnlySdkLinker(PowerShellCompilationResolvedPackageCatalog.ReadAndVerify(workspace, graph, spec.NuGetLockFilePath), linkerVersion);
        using (var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "obj", "project.assets.json"))))
        {
            var folders = assets.RootElement.GetProperty("packageFolders").EnumerateObject().ToArray();
            if (folders.Length != 1 || !PowerShellCompilationPathSafety.PathEquals(folders[0].Name, packageRoot))
                throw new InvalidOperationException("Authenticated SDK restore selected a package folder outside its fresh compiler-owned root.");
        }
        foreach (var package in packages.Values) package.VerifyInstalled(packageRoot);
        var inputHashes = CapturePublishInputHashes(workspace, packageRoot);
        var binding = string.Join("|", graph.LockSha256, toolchain.TargetContractSha256, toolchain.CompilerSha256,
            toolchain.DotNetSdkVersion, toolchain.DotNetSdkSha256,
            string.Join(";", inputHashes.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(static item => item.Key + "=" + item.Value)));
        return new AuthenticatedSdkPublish(authority, packageRoot, packages.Values.ToArray(), inputHashes, binding, restore.Output);
    }

    private static void EnsureOnlySdkLinker(PowerShellCompilationResolvedPackage[] packages, string linkerVersion)
    {
        if (packages.Length != 1 || !packages[0].Id.Equals("Microsoft.NET.ILLink.Tasks", StringComparison.OrdinalIgnoreCase) ||
            !packages[0].Version.Equals(linkerVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Authenticated trimmed SDK publication accepts only the selected SDK ILLink build package. " +
                "Additional NuGet build packages cannot receive runtime derivation authority.");
    }

    private static Dictionary<string, string> CapturePublishInputHashes(string workspace, string packageRoot)
    {
        var nestedInputs = new[] { "provider-runtime", "provider-native-runtime", "EmbeddedDependencies", "selected-restore" }
            .Select(name => Path.Combine(workspace, name)).Where(Directory.Exists)
            .SelectMany(static path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories));
        var paths = Directory.EnumerateFiles(workspace, "*", SearchOption.TopDirectoryOnly)
            .Where(static path => !Path.GetFileName(path).StartsWith(".powerforge-", StringComparison.Ordinal))
            .Concat(nestedInputs).Concat(Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(workspace, "obj"), "*", SearchOption.TopDirectoryOnly));
        return paths.ToDictionary(path => FrameworkCompatibility.GetRelativePath(workspace, path).Replace('\\', '/'), ComputeSha256,
            StringComparer.Ordinal);
    }

    private static void CompleteAuthenticatedSdkPublish(AuthenticatedSdkPublish publish,
        PowerShellCompilationBuildSpec spec, string workspace, string artifactName, string publishDirectory,
        string runtimeIdentifier, PowerShellCompilationDependencyGraph graph, PowerShellCompilationToolchainEvidence toolchain,
        PowerShellCompilationProviderLock providerLock)
    {
        foreach (var package in publish.Packages) package.VerifyInstalled(publish.PackageRoot);
        var after = CapturePublishInputHashes(workspace, publish.PackageRoot);
        if (after.Count != publish.InputHashes.Count || after.Any(item =>
                !publish.InputHashes.TryGetValue(item.Key, out var expected) || expected != item.Value))
            throw new InvalidOperationException("Authenticated SDK publication inputs changed during publish.");
        if (!toolchain.DotNetSdkSha256.Equals(PowerShellCompilationToolchainFingerprint.ComputeSdkSha256(
                toolchain.DotNetSdkVersion, forceRefresh: true), StringComparison.OrdinalIgnoreCase) ||
            !toolchain.CompilerSha256.Equals(ComputeSha256(typeof(PowerShellCompilationArtifactBuilder).Assembly.Location), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Authenticated SDK publication toolchain changed during publish.");
        VerifyDependencyInputsHaveNotDrifted(spec, graph);
        var primary = Path.Combine(publishDirectory, artifactName + (runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : ""));
        var files = Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories)
            .Select(path => CreateArtifactFile(path, PowerShellCompilationPathSafety.PathEquals(path, primary) ? "Primary" : "RuntimeDependency"));
        var outputs = PowerShellStrictDependencyClosureVerifier.CaptureRuntimeOutputHashes(new PowerShellStrictDependencyClosureRequest(
            files, spec.TargetFramework, runtimeIdentifier, graph, spec.Optimization, providerLock, runtimePackAuthority: publish.Authority));
        publish.Authority.RecordSdkOutputs(outputs, publish.Binding);
    }

    private sealed class AuthenticatedSdkPublish
    {
        internal AuthenticatedSdkPublish(PowerShellCompilationRuntimePackAuthority authority, string packageRoot,
            DotNetNuGetClient.AuthenticatedPackage[] packages, Dictionary<string, string> inputHashes, string binding, string restoreOutput)
        {
            Authority = authority;
            PackageRoot = packageRoot;
            Packages = packages;
            InputHashes = inputHashes;
            Binding = binding;
            RestoreOutput = restoreOutput;
        }

        internal PowerShellCompilationRuntimePackAuthority Authority { get; }
        internal string PackageRoot { get; }
        internal DotNetNuGetClient.AuthenticatedPackage[] Packages { get; }
        internal Dictionary<string, string> InputHashes { get; }
        internal string Binding { get; }
        internal string RestoreOutput { get; }
    }
}
