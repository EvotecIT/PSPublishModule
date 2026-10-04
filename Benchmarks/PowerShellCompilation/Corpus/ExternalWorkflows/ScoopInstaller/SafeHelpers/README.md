# Scoop installer safe-helper fixture

The five `.ps1` files are unchanged function extents from ScoopInstaller/Install `install.ps1` at commit `1e2f334083d609986d8c8bc9e31ae8e87c39fab4` (source SHA-256 `94f983b190438311e006b957db7c8422709e0ba62a6c2ac04e278164108f2512`). The source's Unlicense notice is copied as `LICENSE`.

The artifact test builds only these functions. It executes four safe helpers to check install-entry selection, command discovery, information output, console-color restoration, and verbose diagnostics in separate owned PowerShell processes. It verifies that `Exit-Install` stays hosted but never calls it. It never invokes `Install-Scoop`, download, process, registry, or filesystem mutation paths. This fixture does not qualify the complete installer or its top-level script entry.
