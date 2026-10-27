# Set up .NET from the official CDN

This action reuses matching SDKs already installed on a runner. Otherwise it
resolves the repository's `global.json` policy through Microsoft's release metadata, then calls
Microsoft's pinned dotnet-install script with an exact SDK version and both
feeds set to `https://builds.dotnet.microsoft.com/dotnet`. Exact versions bypass
`aka.ms`; there is no separate floating LTS runtime installation.

```yaml
- uses: actions/checkout@v7
- uses: EvotecIT/PSPublishModule/.github/actions/setup-dotnet@actions-v1
```

The default input is `global-json-file: global.json`. Its `sdk.version` and
`sdk.rollForward` select the primary SDK. `latestFeature` permits newer feature
bands in the same major/minor release; `latestPatch` stays within one feature
band and never selects a version below the configured minimum. `disable`
honors an exact version. An omitted roll-forward setting stays within the
configured feature band. Stable SDKs are preferred unless `allowPrerelease` is
enabled. Comments in global.json are supported, and the action never edits it.

Additional SDKs required by test runtimes use the same declaration shape in
`powerforge.sdks`, an array within global.json. The action contains no SDK
version defaults, and workflow inputs cannot override repository SDK policy.

`additional-sdk-versions` adds SDK versions or channels alongside that policy,
separated by spaces or newlines, for example `8.0.426` or `8.0.x`. It does not
replace the primary SDK declaration or edit `global.json`. Callers without their
own SDK declaration can pass an engine-owned file through `global-json-file`.

`architecture` defaults to the runner architecture and supports x64, x86,
arm64 and arm. The action exports DOTNET_ROOT,
the architecture-specific root, and PATH. Its outputs are
`dotnet-version` (highest selected SDK) and `dotnet-root`.

The existing writable matching-architecture installation remains the target so other SDKs,
test runtimes and workloads stay available. An explicit DOTNET_INSTALL_DIR takes
precedence. Matching read-only system installations can satisfy a request without
downloads; missing SDKs use a writable target. Earlier requested SDK versions
are carried forward per architecture if a later setup step needs another root.
The action never exports an automatically selected root as DOTNET_INSTALL_DIR,
so it cannot turn read-only reuse into an explicit write target. macOS falls back to
the user's `~/.dotnet`, matching Microsoft's normal installer default. With no
installation on other platforms, the action uses
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
