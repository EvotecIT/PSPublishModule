# VirusTotal Monitor release publishing

PowerForge can register selected final release artifacts with VirusTotal Monitor. The feature is disabled by default and each repository decides independently whether to enable it, where its API key comes from, and which artifact kinds are eligible.

This integration is a release publisher. It confirms that VirusTotal accepted the artifact and, by default, verifies the remote SHA-256 value against the local file. VirusTotal Monitor analysis is asynchronous, so a successful PowerForge receipt is not an immediate antivirus clean verdict.

## Enable it for one project

Add a `VirusTotal` section to that project's `powerforge.release.json`:

```json
{
  "$schema": "./Schemas/powerforge.release.schema.json",
  "VirusTotal": {
    "Enabled": true,
    "ProjectName": "ExampleApp",
    "ApiKeyEnvName": "VIRUSTOTAL_MONITOR_API_KEY",
    "ArtifactKinds": [
      "PowerShellModule",
      "NuGetPackage",
      "ZipArchive",
      "MsiPackage"
    ],
    "DestinationPathTemplate": "/{Project}/{Version}/{Kind}/{RelativePath}",
    "VerifySha256": true,
    "ReceiptPath": "Artifacts/Release/virustotal-monitor-receipt.json"
  }
}
```

Projects without this section, or with `Enabled: false`, do not contact VirusTotal and do not require an API key.

Configure exactly one API-key source:

- `ApiKeyEnvName` reads a named environment variable at publish time.
- `ApiKeyFilePath` reads a secret file at publish time.
- `ApiKey` accepts an inline value for temporary use, but should not be committed.

Planning, configuration validation, build/checkpoint runs, and explicit Apple status, doctor, or cleanup actions do not read the secret or upload files. When `PowerShellModule` is selected, a checkpoint build still records the packed-module provenance needed by a later signed publication. Secret resolution happens only when an actual release reaches the VirusTotal publishing phase.

## Select eligible artifacts

`ArtifactKinds` is an explicit allowlist. Supported values are:

- `PowerShellModule` for packed module ZIP files.
- `NuGetPackage` for `.nupkg` files.
- `ZipArchive` for packed binary/tool/installer ZIP files.
- `MsiPackage` for `.msi` installers.
- `MsixPackage` for `.msix`, `.appx`, `.msixbundle`, `.appxbundle`, `.msixupload`, and `.appxupload` packages.
- `Executable` for final portable, tool, or installer `.exe` files.

PowerForge selects only categorized final release entries produced by the unified release pipeline. Metadata, `.snupkg` files, arbitrary repository files, `Other` entries, and source-code archives are never eligible. This prevents a broad filesystem glob from turning the feature into a source uploader.

When `RequireMatchingArtifacts` is left at its default `true`, an enabled project records a failed Monitor phase and warning if none of its final outputs match the allowlist. The already-completed primary release remains successful. Set it to `false` only when a shared configuration intentionally covers projects that may have no eligible output.

## Monitor paths and receipts

The default destination path is:

```text
/{Project}/{Version}/{Kind}/{RelativePath}
```

Available template tokens are `{Project}`, `{Version}`, `{Kind}`, `{FileName}`, `{RelativePath}`, `{Target}`, `{Runtime}`, and `{Framework}`. A template must include `{RelativePath}` or `{FileName}`, and resolved paths must be unique.

The JSON receipt is written atomically after every accepted artifact. It records the Monitor identifier, destination, local and remote hashes, verification state, upload time, and current detection count when VirusTotal supplies one. It never includes the API key. A retry for the same project, version, and destination resumes accepted items by Monitor item id instead of creating duplicate paths.

PowerForge Studio shows VirusTotal Monitor as an explicit publish target for enabled projects. Its publish receipt reports success or failure and links the durable Monitor receipt so an operator can inspect or retry the post-release step.

Configuration and secret-source errors are checked before the release starts, so they cannot fail after a registry or tool publication. VirusTotal Monitor access, entitlements, and API quotas are controlled by the project's VirusTotal account. A rejected upload, timeout, or hash mismatch is recorded as a failed Monitor receipt and warning. It does not roll back or retroactively fail the primary release because Monitor registration and analysis are asynchronous post-release integrations; rerun the release publisher to resume from the receipt.
