# Module signing configuration

Use an approved list when build machines have different signing certificates or when certificates overlap during renewal. Configure the policy in the shared build configuration; individual build scripts do not need certificate-store enumeration or interactive selection.

## Select an approved certificate

```powershell
Import-Module PSPublishModule

Build-Module -ModuleName 'MyModule' {
    New-ConfigurationBuild -Enable -SignModule -AllowedCertificateThumbprints @(
        '1111111111111111111111111111111111111111'
        '2222222222222222222222222222222222222222'
    )
}
```

Replace the example thumbprints with your code-signing certificate thumbprints. The list is also the priority order. For each identity, the signing host checks `CurrentUser\My`, then `LocalMachine\My`, before trying the next identity. It skips missing certificates, certificates without private keys, expired/not-yet-valid certificates and certificates whose enhanced key usages exclude code signing. A certificate without an EKU restriction retains the existing signing behaviour.

Selection reads certificate metadata; it does not test-sign with every candidate or open unrelated private keys. Hardware PIN/touch/consent may still be required when the selected certificate actually signs files. Selection cannot establish that a hardware provider is available or that the user will approve its prompt.

`CertificateThumbprint` remains an explicit choice. With a nonempty approved list, that choice must be listed and must be usable; it does not fall back to another identity. PFX certificates must match the same list. When no approved signer is available, signing fails with candidate diagnostics. It never picks another certificate from the store or silently produces an unsigned release.

Omitting the list, setting it to null or supplying an empty array preserves explicit single-thumbprint/PFX configuration. Keep an approved list in the authoritative configuration when enforcing a signing policy. It is a build rule, not protection from someone who can edit that configuration or execute arbitrary signing code.

The module profile command accepts the same `AllowedCertificateThumbprints` option. Delivery and final artefact signing preserve the module policy. Existing valid third-party signatures retain their normal handling; this list selects the publisher's signing identity and does not declare every vendor certificate untrusted.

## Build JSON

The equivalent options segment is:

```json
{
  "Type": "Options",
  "Options": {
    "Signing": {
      "AllowedCertificateThumbprints": [
        "1111111111111111111111111111111111111111",
        "2222222222222222222222222222222222222222"
      ]
    }
  }
}
```

Include it in the build specification's `Segments` array and enable signing in its Build segment. Execute the specification with `Invoke-ModuleBuild -ConfigPath Build/powerforge.json -RunMode Build`. `Invoke-ModuleBuild -JsonOnly -JsonPath Build/powerforge.json` exports a PowerShell-configured pipeline without executing signing.

## PFX inputs and secret references

Use environment references for JSON kept in source control:

```powershell
New-ConfigurationBuild -Enable -SignModule `
    -CertificatePFXBase64EnvironmentVariable 'BUILD_SIGNING_PFX' `
    -CertificatePFXPasswordEnvironmentVariable 'BUILD_SIGNING_PASSWORD' `
    -AllowedCertificateThumbprints @('1111111111111111111111111111111111111111')
```

```json
{
  "Type": "Options",
  "Options": {
    "Signing": {
      "CertificatePFXBase64EnvironmentVariable": "BUILD_SIGNING_PFX",
      "CertificatePFXPasswordEnvironmentVariable": "BUILD_SIGNING_PASSWORD",
      "AllowedCertificateThumbprints": ["1111111111111111111111111111111111111111"]
    }
  }
}
```

The environment references resolve only when signing executes. Planning and configuration export keep the variable names and do not resolve the secrets. Missing or empty variables fail with the variable name, without its value. For a PFX file, use `CertificatePFXPath` with `CertificatePFXPasswordEnvironmentVariable`. Configure one certificate source: explicit thumbprint, PFX path or PFX Base64. Do not combine a literal secret with an environment reference for the same value.

Existing `CertificatePFXBase64` and `CertificatePFXPassword` inputs remain supported, including an explicit empty password for an unprotected fixture or PFX. Those literal values remain part of the configuration supplied by the caller; do not put private key material or passwords in checked-in build JSON or exported files.

The signing host receives PFX data through child-scoped environment overrides, never command-line arguments or temporary secret files. It removes both the source-variable names and transport variables from its environment before provider operations, and imports PFX keys with `EphemeralKeySet`. The parent process environment is unchanged. This reduces command-line and descendant-process exposure; it does not isolate secrets from a debugger, process dump, privileged administrator or other code running in the caller's process.

## Related configuration

- [Delivery layouts and mappings](PSPublishModule.ModuleDelivery.md) describe which files are packaged and signed.
- Release payload signature validation has a separate thumbprint list for validating completed artifacts. It does not select the signing certificate.
- This policy applies to module and delivery Authenticode signing. Project/NuGet release signing has its own configuration. Graph and other runtime authentication certificates are separate from build code signing.
