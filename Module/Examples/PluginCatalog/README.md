# PowerForge Plugin Catalog Example

Use `powerforge.plugins.json` when a repo needs one catalog for folder-style plugin export and NuGet plugin package builds.

Plan folder export:

```powershell
Invoke-PowerForgePluginExport -ConfigPath .\powerforge.plugins.json -Group public -Plan
```

Export plugin folders:

```powershell
Invoke-PowerForgePluginExport -ConfigPath .\powerforge.plugins.json -Group public -OutputRoot .\Artifacts\Plugins -ExitCode
```

Pack plugin NuGet packages:

```powershell
Invoke-PowerForgePluginPack -ConfigPath .\powerforge.plugins.json -Group pack-public -OutputRoot .\Artifacts\NuGet -ExitCode
```

The CLI exposes the same engine:

```powershell
powerforge plugin export --config .\powerforge.plugins.json --group public --plan
powerforge plugin pack --config .\powerforge.plugins.json --group pack-public --output-root .\Artifacts\NuGet
```
