using System.IO.Compression;
using System.Reflection;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    private async Task ValidateModuleAsync(ModuleArtifactValidation spec, Dictionary<string, string> variables,
        ReleaseValidationReport report, CancellationToken cancellationToken)
    {
        using var workspace = new ValidationWorkspace();
        var input = Resolve(spec.Path, variables);
        var root = input;
        if (!Directory.Exists(input))
        {
            root = Directory.CreateDirectory(Path.Combine(workspace.Root, "module")).FullName;
            using var archive = ZipFile.OpenRead(input);
            var entries = PowerForgeReleaseArtifactVerifier.ValidateArchiveEntries(archive);
            PowerForgeReleaseArtifactVerifier.ValidateModuleArchiveBounds(entries);
            foreach (var pair in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Within(root, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var source = pair.Value.Open();
                using var destination = File.Create(path);
                await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(spec.ArchiveRoot)) root = Within(root, spec.ArchiveRoot);
        }
        var manifest = Within(root, spec.Manifest);
        var moduleVersion = ModuleManifestValueReader.ReadTopLevelString(manifest, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(moduleVersion)) throw new InvalidOperationException("Module manifest has no version.");
        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArray(manifest, "Prerelease");
        if (prerelease.Length > 1) throw new InvalidOperationException("Module manifest declares more than one prerelease label.");
        var moduleIdentity = PowerForgeReleaseArtifactVerifier.NormalizeModuleVersion(moduleVersion!, prerelease.SingleOrDefault());
        if (string.IsNullOrEmpty(report.Version)) report.Version = moduleIdentity;
        var expectedVersion = NuGetVersion.Parse(report.Version).Version;
        if (!string.Equals(moduleIdentity, PowerForgeReleaseArtifactVerifier.NormalizeModuleVersionText(report.Version), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Module version {moduleIdentity} does not match {report.Version}.");
        variables["Version"] = report.Version;
        if (spec.ProcessorArchitecture is not null && !string.Equals(spec.ProcessorArchitecture,
                ModuleManifestValueReader.ReadTopLevelString(manifest, "ProcessorArchitecture"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Module processor architecture does not match the contract.");
        var paths = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
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
            if (!FrameworkCompatibility.IsWindows()) throw new PlatformNotSupportedException("Authenticode validation requires Windows.");
            CheckEntries("Signed module payload", files, spec.Signatures.Include, Array.Empty<string>());
            for (var index = 0; index < paths.Length; index++)
            {
                if (!spec.Signatures.Include.Any(pattern => Matches(files[index], pattern))) continue;
                var signature = DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(paths[index]);
                if (!signature.IsValid || (spec.Signatures.Thumbprints.Length > 0 &&
                    !spec.Signatures.Thumbprints.Any(value => string.Equals(value.Replace(" ", string.Empty), signature.Thumbprint, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidOperationException($"Module file '{files[index]}' does not have a valid allowed signature.");
            }
        }
        report.Checks.Add("Module " + Path.GetFileName(manifest));
        if (spec.ProbeScript is not null)
        {
            if (spec.Hosts.Length == 0) throw new InvalidOperationException("Module probes require at least one PowerShell host.");
            foreach (var host in spec.Hosts)
            {
                var probeRoot = Directory.CreateDirectory(Path.Combine(workspace.Root, Guid.NewGuid().ToString("N"))).FullName;
                await RunCommandAsync(new ReleaseCommandValidation
                {
                    Name = "Module runtime " + host, FileName = host,
                    Arguments = new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Resolve(spec.ProbeScript, variables) },
                    Environment = new Dictionary<string, string?> { ["POWERFORGE_MODULE_PATH"] = root, ["POWERFORGE_TEST_ROOT"] = probeRoot }
                }, variables, report, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
