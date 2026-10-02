using System.Security;
using System.Text;

namespace PowerForge;

public sealed partial class AppleAppArchiveService
{
    private async Task<AppleAppArchiveResult> CreateDotNetMacArchiveAsync(
        AppleAppArchiveRequest request, CancellationToken cancellationToken)
    {
        if (request.Platform != ApplePlatform.macOS || request.ArchiveVariant != AppleArchiveVariant.Default)
            throw new ArgumentException("Native .NET archives support macOS only.", nameof(request));
        if (request.RequireExactPackageSnapshot)
            throw new NotSupportedException("The Xcode/Swift exact-package snapshot contract does not cover native .NET archives. Use the normal .NET packaging lane and its committed-source provenance.");
        var spec = DotNetPublishConfiguration.Load(request.ProjectPath);
        var installer = spec.Installers.SingleOrDefault(item => item.Id == request.DotNetPublishInstallerId)
            ?? throw new InvalidOperationException("The selected .NET MacApp installer was not found.");
        var mac = installer.MacApp;
        if (installer.Kind != DotNetPublishInstallerKind.MacApp || mac?.AppStore != true)
            throw new InvalidOperationException("Apple .NET archives require a MacApp installer with AppStore=true.");
        if (spec.Installers.Length != 1 || spec.Targets.Length != 1)
            throw new InvalidOperationException("An Apple archive publish config must contain exactly one target and installer.");
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var plan = runner.Plan(spec, request.ProjectPath);
        if (plan.Steps.Count(step => step.InstallerId == installer.Id && step.Kind == DotNetPublishStepKind.MacAppPackage) != 1)
            throw new InvalidOperationException("An Apple archive must resolve exactly one macOS app; select one runtime and framework.");
        request.ReportProgress("Publishing and signing native .NET macOS application");
        var result = await Task.Run(() => runner.Run(plan, null, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new InvalidOperationException("Native .NET Mac App Store packaging failed: " + result.ErrorMessage);
        var artifact = result.Artefacts.Single(item => item.InstallerId == installer.Id);
        string archivePath = ResolveArchivePath(request);
        if (Directory.Exists(archivePath))
            throw new IOException("The native .NET archive output already exists; use a fresh archive path.");
        Directory.CreateDirectory(archivePath);
        try
        {
            string applicationPath = "Applications/" + Path.GetFileName(artifact.PublishDir);
            string destination = Path.Combine(archivePath, "Products", applicationPath);
            var copy = await _processRunner.RunAsync(new ProcessRunRequest("/usr/bin/ditto", archivePath,
                new[] { artifact.PublishDir, destination }, request.Timeout), cancellationToken).ConfigureAwait(false);
            if (!copy.Succeeded) throw new IOException("Could not copy the signed native .NET app into its archive: " + copy.StdErr);
            static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
            string plist = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0"><dict>
                <key>ArchiveVersion</key><integer>2</integer>
                <key>Name</key><string>{Escape(mac.BundleName)}</string>
                <key>SchemeName</key><string>{Escape(request.Scheme)}</string>
                <key>CreationDate</key><date>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</date>
                <key>ApplicationProperties</key><dict>
                <key>ApplicationPath</key><string>{Escape(applicationPath)}</string>
                <key>CFBundleIdentifier</key><string>{Escape(mac.BundleIdentifier)}</string>
                <key>CFBundleShortVersionString</key><string>{Escape(mac.Version)}</string>
                <key>CFBundleVersion</key><string>{Escape(mac.BuildNumber)}</string>
                <key>SigningIdentity</key><string>{Escape(mac.CodesignIdentity)}</string>
                <key>Team</key><string>{Escape(mac.TeamId!)}</string>
                </dict></dict></plist>
                """;
            File.WriteAllText(Path.Combine(archivePath, "Info.plist"), plist, new UTF8Encoding(false));
            var verification = await _processRunner.RunAsync(new ProcessRunRequest("/usr/bin/codesign", archivePath,
                new[] { "--verify", "--deep", "--strict", destination }, request.Timeout), cancellationToken).ConfigureAwait(false);
            if (!verification.Succeeded) throw new IOException("Archived native .NET application signature verification failed: " + verification.StdErr);
            return new AppleAppArchiveResult {
                ArchivePath = archivePath,
                Destination = GetGenericDestination(request.Platform),
                ArchiveSha256 = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(archivePath).Sha256,
                ProcessResult = verification
            };
        }
        catch
        {
            Directory.Delete(archivePath, recursive: true);
            throw;
        }
    }
}
