# Install a prebuilt macOS app bundle

`AppleMacAppDeploymentService.DeployBundleAsync` installs an already built app
without requiring an Xcode project or initiating a build. It shares snapshots,
copy verification, installation locking and atomic replacement with local Apple
deployment. It does not sign, notarize or publish the app.

Approve the complete staged bundle before applying it:

```csharp
var hash = AppleMacAppDeploymentService.GetBundleSha256(stagedAppPath);
// Retain this hash for review before installation.
var service = new AppleMacAppDeploymentService();
var result = await service.DeployBundleAsync(
    stagedAppPath,
    approvedHash,
    new AppleMacAppDeploymentRequest {
        InstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Applications")
    });
```

The installer consumes a private snapshot and rejects a changed approved hash.
Build-specific fields on the request are unused. Set `Launch = false` to install
only. Launch requires the current console user's account and GUI domain; this API
does not provide cross-user access or invoke sudo.

Before replacement, the service requests termination of the current app after
checking its bundle, executable, PID and launch time. It refuses to stop another
installation of the same bundle ID. It then refreshes Launch Services and uses a
new, fresh launch. This avoids reopening a moved-aside copy of an app.

An exit code from `open` does not establish a successful launch. The service checks
the running app's paths and compares the mapped executable's device/inode with
the installed file. It also verifies the executable hash against the approved
snapshot. `AppleMacAppBundleDeploymentResult` exposes that PID, path, hash and
file identity. `Succeeded` requires this evidence when launch was requested.

An app that refuses termination holds installation. A launch that exits before
verification, opens another copy or maps a different inode fails verification.
The installed app remains available for diagnosis; the service does not force
quit a different installation or silently restore an older executable after a
launch failure.
