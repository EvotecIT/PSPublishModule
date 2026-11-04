using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class AppleAppArchiveService
{
    private async Task ValidateArchivedBundlesAsync(
        AppleAppArchiveUploadRequest request,
        string primaryInfoPlist,
        CancellationToken cancellationToken)
    {
        var embeddedIds = RequiredArchiveValues(request.RequiredEmbeddedBundleIds);
        var entitlementKeys = RequiredArchiveValues(request.RequiredEntitlementKeys);
        if (!request.RequireArchiveAttestation && embeddedIds.Count == 0 && entitlementKeys.Count == 0)
            return;

        var primaryPath = Path.GetDirectoryName(primaryInfoPlist)!;
        if (Path.GetFileName(primaryPath).Equals("Contents", StringComparison.Ordinal))
            primaryPath = Path.GetDirectoryName(primaryPath)!;
        if ((File.GetAttributes(primaryPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The archived primary app must be a regular bundle directory.");

        var bundles = new List<string> { primaryPath };
        var pending = new Stack<string>();
        pending.Push(primaryPath);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var child in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos()
                         .OrderBy(static directory => directory.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    AppleArtifactCopy.InspectContainedSymbolicLink(primaryPath, child.FullName);
                    continue;
                }

                if (child is not DirectoryInfo)
                    continue;
                if (child.Extension.Equals(".app", StringComparison.OrdinalIgnoreCase) ||
                    child.Extension.Equals(".appex", StringComparison.OrdinalIgnoreCase) ||
                    child.Extension.Equals(".watchkitapp", StringComparison.OrdinalIgnoreCase))
                    bundles.Add(child.FullName);
                pending.Push(child.FullName);
            }
        }

        var actualEmbeddedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bundle in bundles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var infoPlist = bundle == primaryPath ? primaryInfoPlist : FindArchivedBundleInfoPlist(bundle);
            var bundleId = await ReadPlistStringAsync(infoPlist, "CFBundleIdentifier",
                request.RequireTrustedSystemTools, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(bundleId))
                throw new InvalidOperationException($"Archived bundle '{bundle}' has no bundle identifier.");
            if (bundle != primaryPath)
                actualEmbeddedIds.Add(bundleId!);

            var verification = await RunArchiveCodesignAsync(request, bundle,
                new[] { "--verify", "--deep", "--strict", bundle }, cancellationToken).ConfigureAwait(false);
            if (!verification.Succeeded)
                throw new InvalidOperationException($"Code signature verification failed for archived bundle '{bundleId}': {verification.StdErr}");

            var requiredKeys = bundle == primaryPath ? entitlementKeys : new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(request.TeamId) || requiredKeys.Count > 0)
                await ValidateArchivedBundleSignaturePolicyAsync(request, bundle, bundleId!, requiredKeys,
                    cancellationToken).ConfigureAwait(false);
        }

        foreach (var required in embeddedIds)
            if (!actualEmbeddedIds.Contains(required))
                throw new InvalidOperationException($"The final archive is missing required embedded bundle '{required}'.");

    }

    private async Task ValidateArchivedBundleSignaturePolicyAsync(
        AppleAppArchiveUploadRequest request,
        string bundle,
        string bundleId,
        HashSet<string> entitlementKeys,
        CancellationToken cancellationToken)
    {
        var details = await RunArchiveCodesignAsync(request, bundle,
            new[] { "-d", "--verbose=4", bundle }, cancellationToken).ConfigureAwait(false);
        if (!details.Succeeded)
            throw new InvalidOperationException($"Unable to inspect the code signature for archived bundle '{bundleId}'.");
        var format = ReadCodesignDetail(details, "Format=");
        if (string.IsNullOrWhiteSpace(format))
            throw new InvalidOperationException($"Archived bundle '{bundleId}' has no readable code signature format.");

        // Verification checks every Mach-O slice, but display defaults to the host slice.
        // Use codesign's own architecture names for both team and entitlement inspection.
        string?[] architectures = new string?[] { null };
        if (format!.IndexOf("Mach-O", StringComparison.Ordinal) >= 0)
        {
            var start = format.LastIndexOf('(');
            var end = format.LastIndexOf(')');
            if (start < 0 || end <= start || end != format.Length - 1)
                throw new InvalidOperationException($"Archived bundle '{bundleId}' has no readable Mach-O architecture list.");
            architectures = format.Substring(start + 1, end - start - 1)
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
            if (architectures.Length == 0)
                throw new InvalidOperationException($"Archived bundle '{bundleId}' has no Mach-O architectures.");
        }

        foreach (var architecture in architectures)
        {
            var selector = architecture is null ? Array.Empty<string>() : new[] { "--architecture", architecture };
            if (!string.IsNullOrWhiteSpace(request.TeamId))
            {
                var signedDetails = architecture is null ? details : await RunArchiveCodesignAsync(request, bundle,
                    new[] { "-d", "--verbose=4" }.Concat(selector).Concat(new[] { bundle }).ToArray(), cancellationToken).ConfigureAwait(false);
                if (!signedDetails.Succeeded)
                    throw new InvalidOperationException($"Unable to inspect the code signature for archived bundle '{bundleId}' architecture '{architecture}'.");
                var signedTeam = ReadCodesignDetail(signedDetails, "TeamIdentifier=");
                if (!string.Equals(signedTeam, request.TeamId!.Trim(), StringComparison.Ordinal))
                    throw new InvalidOperationException($"Archived bundle '{bundleId}' architecture '{architecture ?? "generic"}' has signed team '{signedTeam ?? "<missing>"}', expected '{request.TeamId.Trim()}'.");
            }
            if (entitlementKeys.Count == 0)
                continue;
            var result = await RunArchiveCodesignAsync(request, bundle,
                new[] { "-d", "--entitlements", "-", "--xml" }.Concat(selector).Concat(new[] { bundle }).ToArray(), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                throw new InvalidOperationException("Unable to read the archived primary app's signed entitlements.");
            var output = result.StdOut + "\n" + result.StdErr;
            var start = output.IndexOf("<plist", StringComparison.Ordinal);
            var end = start < 0 ? -1 : output.IndexOf("</plist>", start, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                throw new InvalidOperationException("The archived primary app has no readable signed entitlement dictionary.");
            XDocument document;
            try
            {
                using var text = new StringReader(output.Substring(start, end + "</plist>".Length - start));
                using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
                document = XDocument.Load(reader);
            }
            catch (XmlException exception)
            {
                throw new InvalidOperationException("The archived primary app's signed entitlements are malformed.", exception);
            }
            var dictionary = document.Root?.Element("dict");
            if (dictionary is null)
                throw new InvalidOperationException("The archived primary app has no signed entitlement dictionary.");
            var actual = new HashSet<string>(dictionary.Elements("key").Select(static key => key.Value), StringComparer.Ordinal);
            foreach (var key in entitlementKeys)
                if (!actual.Contains(key))
                    throw new InvalidOperationException($"The archived primary app is missing required signed entitlement '{key}'.");
        }
    }

    private static string? ReadCodesignDetail(ProcessRunResult result, string prefix)
        => (result.StdOut + "\n" + result.StdErr)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?
            .Substring(prefix.Length).Trim();

    private Task<ProcessRunResult> RunArchiveCodesignAsync(
        AppleAppArchiveUploadRequest request,
        string bundlePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var environment = request.RequireTrustedSystemTools ? AppleTrustedExecutionEnvironment.Create() : null;
        var executable = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.OSX) ? "/usr/bin/codesign" : "codesign";
        return _processRunner.RunAsync(new ProcessRunRequest(executable, bundlePath, arguments,
            request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout, environment, captureOutput: true, captureError: true,
            inheritEnvironment: environment is null), cancellationToken);
    }

    private static HashSet<string> RequiredArchiveValues(IEnumerable<string>? values)
        => new((values ?? Array.Empty<string>()).Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim()), StringComparer.Ordinal);

    private static string FindArchivedBundleInfoPlist(string bundlePath)
    {
        var candidates = new[] { Path.Combine(bundlePath, "Info.plist"), Path.Combine(bundlePath, "Contents", "Info.plist") };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException($"Archived bundle '{bundlePath}' has no Info.plist.");
    }
}
