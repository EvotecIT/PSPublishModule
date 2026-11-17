using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppleMacAppDeploymentService
{
    private const string LaunchServicesRegister = "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister";
    private const string RunningAppsScript = "ObjC.import('AppKit'); function run(a) { return JSON.stringify($.NSWorkspace.sharedWorkspace.runningApplications.js.filter(x => ObjC.unwrap(x.bundleIdentifier) === a[0]).map(x => ({pid:Number(x.processIdentifier), executable:ObjC.unwrap(x.executableURL.path), bundle:ObjC.unwrap(x.bundleURL.path), started:Number(x.launchDate.timeIntervalSince1970)}))); }";
    private const string TerminateAppScript = "ObjC.import('AppKit'); function run(a) { const apps=$.NSWorkspace.sharedWorkspace.runningApplications.js.filter(x => Number(x.processIdentifier) === Number(a[0])); if (apps.length === 0) return 'already-exited'; const app=apps[0]; if (ObjC.unwrap(app.bundleURL.path) !== a[1] || ObjC.unwrap(app.executableURL.path) !== a[2] || Number(app.launchDate.timeIntervalSince1970) !== Number(a[3])) throw Error('Application identity changed before termination'); if (!app.terminate) throw Error('Application refused termination'); return 'termination-requested'; }";

    /// <summary>Hashes a staged bundle, including the link and file rules used by the installer.</summary>
    /// <param name="appPath">Existing application bundle.</param>
    /// <returns>SHA-256 used to approve this exact bundle for installation.</returns>
    public static string GetBundleSha256(string appPath)
        => AppleArchiveUploadSnapshot.CaptureCompleteIdentity(Path.GetFullPath(appPath), "staged app bundle").Sha256;

    /// <summary>
    /// Installs an approved, already built bundle and optionally relaunches it in the
    /// current console user's GUI session. No build, signing, login or sudo is performed.
    /// </summary>
    /// <param name="appPath">Staged bundle; it is privately snapshotted before installation.</param>
    /// <param name="expectedSha256">Approved complete-bundle SHA-256.</param>
    /// <param name="request">Installation root and launch options. Build options are unused.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Installation and verified launch results.</returns>
    public async Task<AppleMacAppBundleDeploymentResult> DeployBundleAsync(
        string appPath,
        string expectedSha256,
        AppleMacAppDeploymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (!System.Text.RegularExpressions.Regex.IsMatch(expectedSha256 ?? string.Empty, "^[a-fA-F0-9]{64}$"))
            throw new ArgumentException("An approved complete-bundle SHA-256 is required.", nameof(expectedSha256));
        var source = Path.GetFullPath(appPath);
        if (!source.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The source must be an .app bundle.", nameof(appPath));
        using var snapshot = AppleBuiltAppSnapshot.Create(source);
        if (!GetBundleSha256(snapshot.AppPath).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The staged bundle differs from the approved SHA-256.");
        var installRoot = ResolveInstallRootAtBoundary(request.InstallRoot, source);
        var destination = Path.Combine(installRoot, Path.GetFileName(source));
        using var installLock = AppleMacAppBundleReplacement.AcquireInstallLock(destination);
        var result = new AppleMacAppBundleDeploymentResult();
        string? bundleId = null;
        string? executableName = null;
        if (request.Launch)
        {
            await RequireConsoleUserAsync(installRoot, cancellationToken).ConfigureAwait(false);
            bundleId = await ReadPlistAsync(snapshot.AppPath, "CFBundleIdentifier", cancellationToken).ConfigureAwait(false);
            executableName = await ReadPlistAsync(snapshot.AppPath, "CFBundleExecutable", cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(bundleId) || Path.GetFileName(executableName) != executableName || executableName is "." or "..")
                throw new InvalidOperationException("The bundle identity or executable name is invalid.");
            var prior = await ReadRunningAppsAsync(bundleId, installRoot, cancellationToken).ConfigureAwait(false);
            foreach (var app in prior)
            {
                // A second installation is someone's working copy, not ours to kill.
                if (!app.Bundle.Equals(destination, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Bundle {bundleId} is running from another location: {app.Bundle} (PID {app.Pid}). Stop that copy explicitly first.");
                if (!request.TerminateExisting)
                    throw new InvalidOperationException("The app is running and termination was not requested.");
                await RunBundleToolAsync("/usr/bin/osascript", installRoot,
                    new[] { "-l", "JavaScript", "-e", TerminateAppScript, app.Pid.ToString(CultureInfo.InvariantCulture), app.Bundle, app.Executable, app.Started.ToString("R", CultureInfo.InvariantCulture) },
                    cancellationToken).ConfigureAwait(false);
            }
            await WaitForAppsToExitAsync(bundleId, installRoot, cancellationToken).ConfigureAwait(false);
        }
        result.Install = await InstallAsync(request, installRoot, snapshot.AppPath, snapshot,
            AppleTrustedExecutionEnvironment.ResolveSystemTool(request.DittoExecutable, "ditto", "/usr/bin/ditto", "Staged bundle installation"),
            cancellationToken).ConfigureAwait(false);
        result.Install.SourceAppPath = source;
        if (!result.Install.Succeeded || !request.Launch) return result;

        await RunBundleToolAsync(LaunchServicesRegister, installRoot, new[] { "-f", destination }, cancellationToken).ConfigureAwait(false);
        var launchRequest = new AppleMacAppDeploymentRequest
        {
            TerminateExisting = false,
            LaunchEnvironment = request.LaunchEnvironment,
            LaunchArguments = request.LaunchArguments,
            Timeout = request.Timeout
        };
        result.Launch = await LaunchAsync(launchRequest, installRoot, destination,
            AppleTrustedExecutionEnvironment.ResolveSystemTool(request.OpenExecutable, "open", "/usr/bin/open", "Staged bundle launch"),
            null, cancellationToken).ConfigureAwait(false);
        if (!result.Launch.Succeeded) return result;
        var executable = Path.Combine(destination, "Contents", "MacOS", executableName!);
        var binaryHash = HashBundleExecutable(Path.Combine(snapshot.AppPath, "Contents", "MacOS", executableName!));
        if (HashBundleExecutable(executable) != binaryHash)
            throw new InvalidOperationException("The installed executable differs from the approved bundle.");
        var identity = (await RunBundleToolAsync("/usr/bin/stat", installRoot, new[] { "-f", "%d:%i", executable }, cancellationToken).ConfigureAwait(false)).StdOut.Trim();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var running = await ReadRunningAppsAsync(bundleId!, installRoot, cancellationToken).ConfigureAwait(false);
            if (running.Count > 1) throw new InvalidOperationException("Multiple instances appeared after launch; binary verification is ambiguous.");
            if (running.Count == 1)
            {
                var app = running[0];
                if (app.Bundle != destination || app.Executable != executable)
                    throw new InvalidOperationException($"Launch Services opened another copy: {app.Executable} (PID {app.Pid}).");
                var openedFiles = await RunBundleToolAsync("/usr/sbin/lsof", installRoot,
                    new[] { "-a", "-p", app.Pid.ToString(CultureInfo.InvariantCulture), "-d", "txt", "-F", "Din" }, cancellationToken).ConfigureAwait(false);
                if (!HasExecutableIdentity(openedFiles.StdOut, executable, identity))
                    throw new InvalidOperationException("The running process maps a different executable inode than the installed bundle.");
                if (HashBundleExecutable(executable) != binaryHash)
                    throw new InvalidOperationException("The installed executable changed during launch verification.");
                result.ProcessId = app.Pid;
                result.ExecutablePath = executable;
                result.ExecutableSha256 = binaryHash;
                result.ExecutableFileIdentity = identity;
                return result;
            }
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("open returned successfully but no running app could be verified within ten seconds.");
    }

    private async Task RequireConsoleUserAsync(string root, CancellationToken cancellationToken)
    {
        var uid = (await RunBundleToolAsync("/usr/bin/id", root, new[] { "-u" }, cancellationToken).ConfigureAwait(false)).StdOut.Trim();
        var console = (await RunBundleToolAsync("/usr/bin/stat", root, new[] { "-f", "%u", "/dev/console" }, cancellationToken).ConfigureAwait(false)).StdOut.Trim();
        if (uid == "0" || uid != console) throw new InvalidOperationException("Launch requires the current console user's account; no sudo or cross-user launch is supported.");
        await RunBundleToolAsync("/bin/launchctl", root, new[] { "print", "gui/" + uid }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReadPlistAsync(string appPath, string key, CancellationToken cancellationToken)
        => (await RunBundleToolAsync("/usr/libexec/PlistBuddy", Path.GetDirectoryName(appPath)!,
            new[] { "-c", "Print:" + key, Path.Combine(appPath, "Contents", "Info.plist") }, cancellationToken).ConfigureAwait(false)).StdOut.Trim();

    private async Task<List<RunningBundle>> ReadRunningAppsAsync(string bundleId, string root, CancellationToken cancellationToken)
    {
        var process = await RunBundleToolAsync("/usr/bin/osascript", root,
            new[] { "-l", "JavaScript", "-e", RunningAppsScript, bundleId }, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(process.StdOut.Trim());
        return json.RootElement.EnumerateArray().Select(app => new RunningBundle(
            app.GetProperty("pid").GetInt32(), app.GetProperty("executable").GetString()!, app.GetProperty("bundle").GetString()!, app.GetProperty("started").GetDouble())).ToList();
    }

    private async Task WaitForAppsToExitAsync(string bundleId, string root, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if ((await ReadRunningAppsAsync(bundleId, root, cancellationToken).ConfigureAwait(false)).Count == 0) return;
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("The existing app did not exit; installation was not started.");
    }

    private async Task<ProcessRunResult> RunBundleToolAsync(string executable, string root, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(AppleTrustedExecutionEnvironment.CreateProcessRequest(
            executable, executable, executable, "User-session bundle deployment", root, arguments, TimeSpan.FromSeconds(20)), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) throw new InvalidOperationException($"{Path.GetFileName(executable)} failed: {result.StdErr.Trim()}");
        return result;
    }

    private static string HashBundleExecutable(string path)
    {
        using var sha = SHA256.Create();
        using var input = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal static bool HasExecutableIdentity(string output, string executable, string expectedIdentity)
    {
        string? device = null;
        string? inode = null;
        foreach (var line in output.Split('\n'))
        {
            if (line.Length == 0) continue;
            if (line[0] is 'p' or 'f') { device = null; inode = null; }
            if (line[0] == 'D') device = long.Parse(line.Substring(1).Replace("0x", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            if (line[0] == 'i') inode = line.Substring(1).TrimEnd('\r');
            if (line[0] == 'n' && line.Substring(1).TrimEnd('\r') == executable && device + ":" + inode == expectedIdentity) return true;
        }
        return false;
    }

    private sealed class RunningBundle
    {
        internal RunningBundle(int pid, string executable, string bundle, double started)
        {
            if (pid <= 0 || started <= 0 || double.IsNaN(started) || double.IsInfinity(started))
                throw new InvalidOperationException("The running-app inventory returned an invalid process identity.");
            Pid = pid;
            Executable = executable;
            Bundle = bundle;
            Started = started;
        }
        internal int Pid { get; }
        internal string Executable { get; }
        internal string Bundle { get; }
        internal double Started { get; }
    }
}
