# Source-backed Markdown examples

PowerForge keeps selected Markdown code blocks synchronized with local source
files. Use this for package READMEs and guides whose examples also compile or run
as ordinary programs. The surrounding prose remains editable Markdown.

## Select documents

Save a configuration such as `Build/docs-sync.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.docsync.schema.json",
  "rootPath": "..",
  "files": ["README.md", "MyLibrary/README.md"]
}
```

`rootPath` is relative to the configuration file. Document paths and source paths
in markers are relative to that root. Files are selected explicitly; PowerForge
does not scan or rewrite other Markdown documents.

## Mark an example

Put the complete block on unindented lines in a selected Markdown file:

````markdown
<!-- powerforge:code source="Examples/QuickStart.cs" region="QuickStart" -->
```csharp
// Replaced with the selected source code.
```
<!-- /powerforge:code -->
````

In the source file, use `#region QuickStart` and `#endregion`. Region names match
exactly and must be unique in the selected file. Nested region directives are
removed from the displayed excerpt; their code is retained. Omit `region` to
include the entire file. Common indentation and outer blank lines are removed.

The language is inferred for C#, PowerShell, JSON and XML, with `text` as the
fallback. Add `language="typescript"`, for example, to select a different fence
language. Fences expand when the source contains backticks. Marker examples
inside fenced Markdown code blocks are left untouched.

## Check and update

```powershell
powerforge docs sync --config Build/docs-sync.json
powerforge docs sync --config Build/docs-sync.json --apply
powerforge docs sync --config Build/docs-sync.json --output json
```

The default command checks without writing. Exit code `0` means the blocks match,
`1` means documents need refreshing, and `2` means configuration, markers or
sources could not be processed. `--apply` refreshes stale blocks and returns `0`
on success. JSON output includes checked file/block counts and changed paths.

Every selected document is validated before any updates are written. Missing,
duplicate or unclosed regions, malformed markers, out-of-root paths and linked
paths are errors. A selected document must contain at least one source-backed
block. Markdown and source files use UTF-8; an existing UTF-8 BOM, line endings,
final newline and surrounding prose are preserved. Validation failures leave all
documents unchanged. Updates replace individual files; an I/O failure is not a
transaction across the complete document set.

Synchronization proves that the displayed code matches its source. Compile and
run the example separately to verify its behavior. Package README examples should
also build against the published package they describe, rather than relying only
on project references to newer source.

## Website pipeline

The same operation is available without a website build:

```json
{
  "steps": [
    { "task": "docs-sync", "config": "../Build/docs-sync.json" }
  ]
}
```

The task checks by default and fails on stale blocks. Set `apply: true` for an
explicit refresh pipeline. It always reads the current source bytes, including
when pipeline caching is enabled.

## .NET API

```csharp
var result = MarkdownCodeSynchronizer.Synchronize(new MarkdownCodeSyncSpec
{
    RootPath = repositoryRoot,
    Files = new[] { "README.md" }
}, apply: false);

var code = SourceCodeReader.Read(repositoryRoot, "Examples/QuickStart.cs", "QuickStart");
```

Both APIs are in the `PowerForge` namespace. They perform local file operations;
they do not fetch URLs, execute examples or change package dependencies.
