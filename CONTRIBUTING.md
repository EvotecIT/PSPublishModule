# Contributing

## Repo goals

- Keep **core logic** in `PowerForge` (C#) so it can be reused by:
  - PowerShell cmdlets (`PSPublishModule`)
  - CLI (`PowerForge.Cli`)
  - GitHub Actions / future VSCode extension
- Keep **cmdlets thin**: parameter binding + `ShouldProcess` + call core service + typed output.
- Prefer **typed models/enums**; keep `Hashtable/OrderedDictionary` only for legacy adapters.
- Avoid **unsafe code**: `AllowUnsafeBlocks` is intentionally not enabled; if you must add `unsafe`, document why and where in the PR and code.

## Build

- `dotnet build .\\PSPublishModule.sln -c Release`

## Tests

- C# unit tests: `dotnet test .\\PSPublishModule.sln -c Release`
- Pester tests: `Invoke-Pester -Path .\\Module\\Tests -CI`

## Adding/Refactoring a cmdlet

1. Create/extend a typed service in `PowerForge` (spec/result models in `PowerForge/Models`).
2. Create/update a thin cmdlet in `PSPublishModule/Cmdlets` that calls the service.
3. Remove legacy PowerShell functions once replaced (avoid duplicate implementations).
4. Add unit tests (xUnit) and/or Pester coverage for the public behavior.
