using System.IO.Compression;
using System.Reflection;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    private async Task ValidateModuleAsync(ModuleArtifactValidation spec, Dictionary<string, string> variables,
        ReleaseValidationReport report, bool inferCommonVersion, CancellationToken cancellationToken)
    {
        if (spec.ProbeScript is not null && (spec.Hosts is null || spec.Hosts.Length == 0 ||
            spec.Hosts.Any(string.IsNullOrWhiteSpace)))
        {
            throw new InvalidOperationException("Module probes require at least one nonblank PowerShell host, without blank entries.");
        }
        using var workspace = new ValidationWorkspace();
        var input = Resolve(spec.Path, variables);
        var directoryInput = Directory.Exists(input);
        var root = Path.Combine(workspace.Root, "module");
        if (directoryInput)
        {
            await CopyValidationDirectoryAsync(input, root, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            root = Directory.CreateDirectory(Path.Combine(workspace.Root, "module")).FullName;
            using var archiveInput = new ArchiveMetadataReadStream(File.OpenRead(input), cancellationToken);
            using var archive = new ZipArchive(archiveInput, ZipArchiveMode.Read);
            var entries = PowerForgeReleaseArtifactVerifier.ValidateArchiveEntries(archive);
            PowerForgeReleaseArtifactVerifier.ValidateModuleArchiveBounds(entries);
            archiveInput.CompleteMetadataInspection();
            foreach (var pair in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Within(root, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var source = pair.Value.Open();
                using var destination = File.Create(path);
                var copied = PowerForgeReleaseArtifactVerifier.CopyBounded(source, destination, pair.Value.Length,
                    $"Module archive entry '{pair.Key}'", cancellationToken);
                if (copied != pair.Value.Length)
                    throw new InvalidDataException($"Module archive entry '{pair.Key}' has an unexpected extracted length.");
#if NET8_0_OR_GREATER
                // Restore ordinary Unix permissions only: never apply setuid, setgid, or sticky bits.
                var permissions = (pair.Value.ExternalAttributes >> 16) & 0x1ff;
                if (!OperatingSystem.IsWindows() && permissions != 0)
                    File.SetUnixFileMode(path, (UnixFileMode)permissions);
#endif
            }
            if (!string.IsNullOrEmpty(spec.ArchiveRoot)) root = Within(root, spec.ArchiveRoot);
        }
        var paths = EnumerateValidationFiles(root, cancellationToken).ToArray();
        var manifest = Within(root, spec.Manifest);
        FileSystemPathSafety.RejectReparsePoints(manifest, root, "Module manifest");
        var content = await DotNetPublishReleaseArtifactVerifier.ReadBoundedMetadataAsync(manifest, "Module manifest",
            DotNetPublishReleaseArtifactVerifier.MaxManifestBytes, cancellationToken).ConfigureAwait(false);
        using var manifestBytes = new MemoryStream(content, writable: false);
        var manifestText = ModuleManifestValueReader.ReadPowerShellCompatibleText(manifestBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var moduleVersion = ModuleManifestValueReader.ReadTopLevelStringFromText(manifestText, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(moduleVersion)) throw new InvalidOperationException("Module manifest has no version.");
        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArrayFromText(manifestText, "Prerelease");
        if (prerelease.Length > 1) throw new InvalidOperationException("Module manifest declares more than one prerelease label.");
        var moduleIdentity = PowerForgeReleaseArtifactVerifier.NormalizeModuleVersion(moduleVersion!, prerelease.SingleOrDefault());
        if (inferCommonVersion && string.IsNullOrEmpty(report.Version)) report.Version = moduleIdentity;
        var expectedVersion = NuGetVersion.Parse(moduleIdentity).Version;
        if (!string.IsNullOrEmpty(report.Version) &&
            !string.Equals(moduleIdentity, PowerForgeReleaseArtifactVerifier.NormalizeModuleVersionText(report.Version), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Module version {moduleIdentity} does not match {report.Version}.");
        variables["Version"] = string.IsNullOrEmpty(report.Version) ? moduleIdentity : report.Version;
        // Hosts may use the inspected module version. Resolve the whole set before copying or running any probe.
        var hosts = spec.ProbeScript is null ? Array.Empty<string>() : spec.Hosts.Select(host => Expand(host, variables)).ToArray();
        if (hosts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Module probe hosts must expand to nonblank executable names or paths.");
        }
        if (spec.ProcessorArchitecture is not null && !string.Equals(spec.ProcessorArchitecture,
                ModuleManifestValueReader.ReadTopLevelStringFromText(manifestText, "ProcessorArchitecture"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Module processor architecture does not match the contract.");
        var files = paths.Select(path => DotNetPublishReleaseArtifactVerifier.GetRelativePath(root, path).Replace('\\', '/')).ToArray();
        CheckEntries("Module", files, spec.RequiredFiles, Array.Empty<string>());
        CheckEntries("Module assemblies", files, spec.VersionedAssemblies, Array.Empty<string>());
        for (var index = 0; index < paths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (spec.VersionedAssemblies.Any(pattern => Matches(files[index], pattern)))
            {
                var version = AssemblyName.GetAssemblyName(paths[index]).Version;
                if (version is null || version.Major != expectedVersion.Major || version.Minor != expectedVersion.Minor || version.Build != expectedVersion.Build)
                    throw new InvalidOperationException($"Module assembly '{files[index]}' does not match {report.Version}.");
            }
        }
        if (spec.Signatures is not null)
        {
            var signedFiles = Enumerable.Range(0, paths.Length)
                .Where(index => spec.Signatures.Include.Any(pattern => Matches(files[index], pattern))).ToArray();
            if (signedFiles.Length == 0)
                throw new InvalidOperationException("Module signature validation requires at least one matching payload file.");
            if (!FrameworkCompatibility.IsWindows()) throw new PlatformNotSupportedException("Authenticode validation requires Windows.");
            foreach (var index in signedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var signature = DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(paths[index]);
                if (!signature.IsValid || (spec.Signatures.Thumbprints.Length > 0 &&
                    !spec.Signatures.Thumbprints.Any(value => string.Equals(value.Replace(" ", string.Empty), signature.Thumbprint, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidOperationException($"Module file '{files[index]}' does not have a valid allowed signature.");
            }
        }
        report.Checks.Add("Module " + Path.GetFileName(manifest));
        if (spec.ProbeScript is not null)
        {
            foreach (var host in hosts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var probeWorkspace = new ValidationWorkspace();
                var probeRoot = probeWorkspace.Root;
                var probeModule = Path.Combine(probeRoot, "module");
                await CopyValidationDirectoryAsync(root, probeModule, cancellationToken).ConfigureAwait(false);
                var script = Resolve(spec.ProbeScript, variables);
                // A probe shipped inside a directory module must not expose the original through PSScriptRoot.
                if (directoryInput)
                {
                    var relativeScript = DotNetPublishReleaseArtifactVerifier.GetRelativePath(input, script);
                    if (!Path.IsPathRooted(relativeScript) && relativeScript != ".." &&
                        !relativeScript.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        script = Within(probeModule, relativeScript);
                }
                await RunCommandAsync(new ReleaseCommandValidation
                {
                    Name = "Module runtime " + host, FileName = host,
                    WorkingDirectory = probeModule,
                    Arguments = new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script },
                    Environment = new Dictionary<string, string?> { ["POWERFORGE_MODULE_PATH"] = probeModule, ["POWERFORGE_TEST_ROOT"] = probeRoot }
                }, variables, report, cancellationToken, expandVariables: false).ConfigureAwait(false);
            }
        }
    }
}
