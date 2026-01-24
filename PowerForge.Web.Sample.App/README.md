# PowerForge.Web.Sample.App

Minimal Blazor WebAssembly host used for `web.publish` demos.

Purpose:
- Provide a local publish target for overlay + Blazor fixes
- Avoid touching external repos during sample runs

Usage:
```
powerforge-web publish --config Samples/PowerForge.Web.CodeGlyphX.Sample/publish-artifacts.json
```

Output:
- `Artifacts/PowerForge.Web.CodeGlyphX.Sample/publish` (published app)
- `Artifacts/PowerForge.Web.CodeGlyphX.Sample/publish/wwwroot` (overlay target)
