# Docs Builder Checklist

## Preflight

1. Confirm whether target files are generated or authored:
   - Generated: `Module/Docs/*`, `Module/en-US/*-help.xml`
   - Authored: `Docs/*.md`, C# XML doc comments, `Help/About/about_*`
2. Locate docs settings in module build config:
   - `New-ConfigurationDocumentation`
   - `Path`, `PathReadme`, `AboutTopicsSourcePath`, `StartClean`, `UpdateWhenNew`
3. Confirm command naming clarity:
   - `New-*` scaffold
   - `New-Configuration*` DSL object
   - `Invoke-*` execution

## Run and Verify

1. Regenerate docs via `.\Module\Build\Build-Module.ps1`.
2. Validate documentation checks in summary output.
3. Confirm expected diffs:
   - relevant `Module/Docs/*.md`
   - `Module/en-US/PSPublishModule-help.xml`
   - authored docs under `Docs/*.md` when intentionally changed.

## Troubleshooting

- Generated docs reverted manual edits:
  - edit source comments/about-topic files and regenerate.
- Missing about topics:
  - verify filename pattern `about_*` and source path inclusion.
- New cmdlet missing docs page:
  - ensure XML comments/examples exist and cmdlet is part of module build output.
