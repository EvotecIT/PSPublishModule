# DotNetPublish Examples

This folder contains starter examples for the DotNet publish engine.

Files:

- `Example.ServiceMsi.json`
  - service packaging + MSI prepare/build pattern.
- `Example.RebuildState.json`
  - rebuild/state preservation pattern with service lifecycle.
- `Example.PortableBundleMsi.json`
  - desktop app + service sidecar + portable bundle + MSI pattern.
- `Example.PackageBundleMsi.json`
  - service/CLI package layout with README copy, module payload include, generated wrapper script, bundle post-process, and MSI from the composed bundle.
- `Example.StorePackage.json`
  - desktop app + Store/MSIX packaging project pattern.
- `Example.StoreSubmit.json`
  - packaged-app Partner Center submission pattern for `powerforge store submit`.
- `Example.StoreDesktopSubmit.json`
  - MSI/EXE Partner Center submission pattern for `powerforge store submit`.

These are intentionally production-shaped templates. Update project/service/installer paths for your repository before running.

## Typical Usage

1. Copy one template to repo root:

```powershell
Copy-Item '.\Module\Examples\DotNetPublish\Example.ServiceMsi.json' '.\powerforge.dotnetpublish.json'
```

For desktop/portable delivery:

```powershell
Copy-Item '.\Module\Examples\DotNetPublish\Example.PortableBundleMsi.json' '.\powerforge.dotnetpublish.json'
```

2. Adjust `Projects`, `Targets`, `Installers`, `StorePackages`, or Store submission paths as needed.

3. Validate and plan:

```powershell
Invoke-DotNetPublish -ConfigPath '.\powerforge.dotnetpublish.json' -Validate
Invoke-DotNetPublish -ConfigPath '.\powerforge.dotnetpublish.json' -Plan
```

4. Run:

```powershell
Invoke-DotNetPublish -ConfigPath '.\powerforge.dotnetpublish.json' -ExitCode
```

## Notes

- `Example.ServiceMsi.json` expects a WiX installer project (`*.wixproj`).
- `Example.RebuildState.json` is aimed at preserve/restore deployments and service-aware rebuild flows.
- `Example.PortableBundleMsi.json` shows how to use `Bundles`, include sidecar targets, and build an MSI from the composed bundle instead of the raw publish output.
- `Example.PackageBundleMsi.json` is the starter for TierBridge-style packages: composed service/CLI payload, shipped PowerShell module, generated scripts, ZIP, and MSI from the bundle.
- `Example.StorePackage.json` shows how to keep Store/MSIX packaging in the same PowerForge publish matrix as the app target.
- `Example.StoreSubmit.json` shows how to submit a packaged-app ZIP to Partner Center after `StorePackages[]` produces the `*.msixupload` or `*.appxupload` artifact.
- `Example.StoreDesktopSubmit.json` shows how to submit MSI/EXE metadata through the newer desktop Store submission API.
- For command naming and fast overrides, see:
  - `Docs/PSPublishModule.DotNetPublish.Quickstart.md`
