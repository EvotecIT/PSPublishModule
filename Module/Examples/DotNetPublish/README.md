# DotNetPublish Examples

This folder contains starter examples for the DotNet publish engine.

Files:

- `Example.ServiceMsi.json`
  - service packaging + MSI prepare/build pattern.
- `Example.RebuildState.json`
  - rebuild/state preservation pattern with service lifecycle.

These are intentionally production-shaped templates. Update project/service/installer paths for your repository before running.

## Typical Usage

1. Copy one template to repo root:

```powershell
Copy-Item '.\Module\Examples\DotNetPublish\Example.ServiceMsi.json' '.\powerforge.dotnetpublish.json'
```

2. Adjust `Projects`, `Targets`, `Installers`, and paths.

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
- For command naming and fast overrides, see:
  - `Docs/PSPublishModule.DotNetPublish.Quickstart.md`
