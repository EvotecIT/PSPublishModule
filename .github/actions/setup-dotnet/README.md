# Set up .NET from the official CDN

This action reuses matching SDKs already installed on a runner. Otherwise it
resolves channel requests through Microsoft's release metadata, then calls
Microsoft's pinned dotnet-install script with an exact SDK version and both
feeds set to `https://builds.dotnet.microsoft.com/dotnet`. Exact versions bypass
`aka.ms`; there is no separate floating LTS runtime installation.

```yaml
- uses: actions/checkout@v7
- uses: EvotecIT/PSPublishModule/.github/actions/setup-dotnet@main
  with:
    dotnet-version: |
      8.0.x
      10.0.x
    global-json-file: global.json
```

Use an immutable commit instead of `main` when a workflow pins its actions.
`dotnet-version` accepts exact SDK versions, major/minor channels such as
`10.x` or `10.0.x`, and feature bands such as `10.0.3xx`. An explicit
`global-json-file` adds its SDK pin. With neither input, a root `global.json`
supplies the version. Comments in global.json are supported. The action never
edits global.json; the .NET CLI applies its rollForward and allowPrerelease
settings when a subsequent build selects its SDK.

`architecture` defaults to the runner architecture and supports x64, x86,
arm64 and arm. `dotnet-quality: preview` permits preview SDKs for a channel;
exact versions are always honored. The action exports DOTNET_ROOT,
DOTNET_INSTALL_DIR, the architecture-specific root, and PATH. Its outputs are
`dotnet-version` (highest selected SDK) and `dotnet-root`.

The existing matching-architecture installation remains the target so other SDKs,
test runtimes and workloads stay available. An explicit DOTNET_INSTALL_DIR takes
precedence. With no installation, the action uses
`RUNNER_TOOL_CACHE/evotec-dotnet/<architecture>`. These installations persist on
self-hosted runners and are reused across jobs; no large SDK cache archives are
uploaded to GitHub. A matching preinstalled SDK is preferred over downloading a
newer patch. Use exact versions when a specific patch or feature band is required.

The action uses the runner's Node 24 runtime, bash on Linux/macOS, and Windows
PowerShell on Windows. No npm install or additional production package is needed.
It does not configure NuGet authentication or NuGet package caching.

The upstream scripts are pinned to dotnet/install-scripts v2026.07.21, commit
`da3ce11ba63f3dbb0fb835d41bda2665d5c48e84`; SHA-256 is verified before execution.
SDK archive handling remains with Microsoft's installer. To update the scripts,
review the upstream change and replace the commit and both SHA-256 values in
setup-dotnet.cjs together, then qualify a clean Windows and POSIX installation.

Run the focused contract tests with:

```sh
node --test .github/actions/setup-dotnet/setup-dotnet.test.cjs
```
