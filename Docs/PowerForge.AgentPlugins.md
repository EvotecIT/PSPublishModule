# Agent plugin packaging

PowerForge packages Agent Plugins 1.0.0 from a dedicated source directory. These packages expose Agent Skills and MCP servers in supporting clients. The separate `powerforge plugin` command continues to package native .NET plugins.

```text
powerforge agent-plugin sync --source plugins/document-tools
powerforge agent-plugin validate --source plugins/document-tools --project Example.Tool/Example.Tool.csproj --output json
powerforge agent-plugin pack --source plugins/document-tools --out Artefacts/AgentPlugins --output json
```

`sync` regenerates `.claude-plugin/plugin.json`, `.codex-plugin/plugin.json`, `.mcp.json`, and `.codex-plugin/mcp.json` from the portable `plugin.json` and optional `mcp.json`. It replaces only those compatibility files and removes obsolete compatibility MCP files when `mcp.json` is removed. Check these generated files into a Git marketplace when clients need the native layouts directly from source.

`validate` writes nothing. It checks the supported JSON profile, package layout, portable filenames, links, and drift in existing generated compatibility files. It requires a three-part semantic plugin version for versioned artifacts. Run an Agent Skills validator for YAML semantics and skill references, then validate and install in each supported client: package validation does not execute skills or MCP servers.

`pack` creates `<name>-<version>.zip` with `plugin.json` at the archive root and a matching `.sha256` sidecar. Existing outputs are rejected. Source metadata, skill resources, and scripts are included alongside generated compatibility files. Unix builds preserve source script permissions. Use an output directory outside the plugin source.

## Supported source layout

```text
document-tools/
  plugin.json
  mcp.json
  skills/summarize/SKILL.md
  skills/summarize/references/limits.md
  scripts/
  assets/
  README.md
  LICENSE
```

The root may also contain `resources`, `references`, `CHANGELOG.md`, `LICENSE.md`, and reverse-domain extension directories. Repository metadata, environment files, unknown root files, non-portable paths, case- or Unicode-normalization-colliding paths, and symbolic links/reparse points are rejected. Use a reviewed, dedicated package root; do not point the packer at an entire repository or a folder containing private content. The source must remain unchanged during packaging.

The portable manifest targets the published [Agent Plugins 1.0.0 schema](https://agent-plugins.org/schemas/1.0.0/plugin.schema.json). Component discovery uses fixed `skills/` and `mcp.json` locations. The compatibility profile requires an author name when `author` is present and absolute HTTP(S) URLs for homepage, repository, and author URL. `extensions.com.openai` may supply client metadata, but cannot override canonical fields or component locations. Other extension namespaces remain opaque.

The MCP profile supports STDIO, Streamable HTTP, and SSE. Remote endpoints require HTTPS outside loopback, prohibit fragments/embedded credentials, and validate HTTP field syntax and case-insensitive header uniqueness. Compatibility configuration translates `streamable-http` to native `http`, plugin-relative executables to client-root paths, and `${PLUGIN_ROOT}` in STDIO arguments/environment to each client's root variable. It rejects explicit `cwd` and `${PLUGIN_DATA}` because this compatibility profile cannot preserve their semantics across both clients. Bare commands must be single executable tokens available on the client host. Plugin-relative commands use forward slashes. Server identifiers must be non-empty. Environment variable names must be unique ignoring case, cannot override reserved plugin variables, and must be valid for Windows and Unix process environments.

## Consumer validation and publication

Test the extracted archive independently of the source checkout. Install through the client's marketplace flow, inspect discovered skills/tools, and run a representative operation with its expected authorization boundaries. Local MCP servers may inherit different working directories in different clients; pass explicit authorized folders when the server supports them.

ZIP creation does not publish, submit, or approve a plugin. Git marketplaces, the MCP Registry, OpenAI's public directory, and Claude's directory have separate publisher verification and review requirements. Public OpenAI connected plugins require a stable hosted HTTPS server; local STDIO packaging is not a substitute for deployment.

The same service is available to .NET consumers:

```csharp
var packager = new PowerForge.AgentPluginPackageService();
packager.SyncCompatibility("plugins/document-tools");
var package = packager.Pack("plugins/document-tools", "Artefacts/AgentPlugins");
Console.WriteLine(package.ArchivePath);
Console.WriteLine(package.Sha256);
```

## Match a NuGet release version

Use `--project` with `validate`, `sync`, or `pack` to require the plugin version to match the owning project's explicit package version. The shared project-version reader supports a declared `PackageVersion`, `Version`, or `VersionPrefix` with `VersionSuffix`; inherited or computed versions require a release binding instead of this source check. `sync` can repair stale compatibility files after checking the canonical version.

In `project.build.json`, bind the canonical manifest to the owning project and enable compatibility synchronization:

```json
{
  "Path": "plugins/document-tools/plugin.json",
  "Project": "Example.Tool",
  "Pattern": "(?m)(?<=^  \"version\": \")\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?",
  "SyncAgentPluginCompatibility": true
}
```

Bind `mcp.json` tool pins separately when required. PowerForge generates client files from the planned canonical metadata and MCP configuration, then applies project, binding, and client-manifest changes in one rollback-backed transaction. Plan mode writes nothing. Generate and check in compatibility files with `agent-plugin sync` before enabling this setting; release synchronization requires those files to exist and use the generator's BOM-free UTF-8 encoding. It rejects conflicting explicit bindings to generated content.

Each published bundle change needs a new product release version, including skills-only changes. Keep schema/protocol versions and the packer's own version independent. Published ZIPs and checksums are immutable.
