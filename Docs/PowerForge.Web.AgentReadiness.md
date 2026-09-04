# PowerForge.Web Agent Readiness

PowerForge.Web includes an `agent-ready` surface for the emerging checks used by
Cloudflare's Is Your Site Agent-Ready scanner and similar tools.

The goal is not to pretend every static documentation site is an API platform.
The goal is to make the site explicit about what agents can discover, crawl,
read, and use.

## What The Current Scanners Check

The scanner families currently group checks roughly like this:

- Discoverability:
  - `/robots.txt`
  - `/sitemap.xml`
  - response `Link` headers on the homepage
- Content:
  - `Accept: text/markdown` negotiation
- Bot access control:
  - crawler rules in `robots.txt`
  - `Content-Signal` directives for `search`, `ai-input`, and `ai-train`
  - optional Web Bot Auth request-signing metadata
- AI content discovery:
  - AI crawler directives
  - meta robots
  - sitemap freshness signals
- AI search signals:
  - JSON-LD
  - Schema.org types
  - entity links such as `sameAs` or `@id`
  - BreadcrumbList
  - Organization and FAQPage schemas where applicable
  - author or publisher attribution
- Content and semantics:
  - server-rendered HTML
  - heading hierarchy
  - semantic landmarks
  - ARIA landmarks
  - image alt text
  - language
  - useful link text
  - question headings where applicable
- Security and trust:
  - HTTPS
  - HSTS
  - CSP
  - X-Content-Type-Options
  - X-Frame-Options or CSP `frame-ancestors`
  - CORS for agent/API discovery resources
  - Referrer-Policy
- API, auth, MCP, and skill discovery:
  - `/.well-known/api-catalog` as `application/linkset+json`
  - OAuth / OIDC discovery files where protected APIs exist
  - `/.well-known/oauth-protected-resource` where protected resources exist
  - `/.well-known/mcp/server-card.json` where an MCP server exists
  - `/.well-known/agent-skills/index.json`
  - `/agents.json` and `/.well-known/agents.json`
  - `/.well-known/agent-card.json` for A2A discovery where a site can truthfully describe an agent surface
  - OpenAPI where a programmable HTTP API exists
  - optional WebMCP browser tools
- Commerce:
  - x402, UCP, and ACP discovery for commerce sites

PowerForge.Web can prepare and verify the common static-site subset:

- robots.txt with Content Signals
- sitemap.xml integration
- static host `_headers` with Link headers and well-known content types
- optional Apache `.htaccess` rules with homepage Link headers and Markdown
  negotiation for Apache-hosted static sites
- static security headers for default-on (explicitly opt-out) HSTS, CSP, X-Content-Type-Options,
  X-Frame-Options, Referrer-Policy, Permissions-Policy, and discovery-resource CORS
- optional static Markdown artifacts generated from rendered HTML
- API catalog Linkset generation
- Agent Skills index + default SKILL.md generation
- agents.json generation
- optional A2A Agent Card generation
- optional MCP server card
- OpenAPI detection/configuration
- local HTML, JSON-LD, and semantic checks aligned with AI-readiness scanners
- route-scoped WebMCP verification for the current imperative API, including
  the exact canonical engine runtime, same-origin page/index/runtime URLs, and
  exact configured tool names and descriptions
- a reusable, read-only `site-search` WebMCP runtime backed by the generated
  search index
- remote scan of live headers and well-known URLs

Cloudflare Markdown for Agents is a host-level feature. PowerForge verifies it
with a live scan, but static output cannot prove that `Accept: text/markdown`
will be negotiated by the deployed edge or origin.

PowerForge can also generate Markdown artifacts itself. This is separate from
live HTTP negotiation: the static site can contain `/index.md` and
`/docs/index.md`, but the deployed host or edge still needs a rule if the same
HTML route should return Markdown for `Accept: text/markdown`.
When a live scan sees a valid direct Markdown artifact but the homepage still
returns HTML for `Accept: text/markdown`, PowerForge reports the negotiation as
a warning instead of a failure so sites without header-aware edge caching can
still pass on the portable direct-artifact path.

When `MarkdownArtifacts.Enabled` is `true`, the direct homepage artifact is a
required deployed resource. The live scan fetches it even when content
negotiation succeeds, requires a `text/markdown` media type, and includes it in
the configured discovery-resource CORS check.

## Site Configuration

Add an `agentReadiness` block to `site.json`:

```json
{
  "agentReadiness": {
    "enabled": true,
    "contentSignals": {
      "enabled": true,
      "search": true,
      "aiInput": true,
      "aiTrain": false
    },
    "securityHeaders": {
      "enabled": true,
      "contentSecurityPolicyValue": "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'"
    },
    "apiCatalog": {
      "enabled": true,
      "includeProjectApiReferences": true,
      "entries": [
        {
          "anchor": "/api/",
          "serviceDesc": "/api/index.json",
          "serviceDoc": "/api/"
        }
      ]
    },
    "agentSkills": {
      "enabled": true
    },
    "agentsJson": {
      "enabled": true
    },
    "webMcp": true,
    "webMcpTools": [
      {
        "name": "search_site",
        "route": "/search/",
        "description": "Search this website's public content.",
        "kind": "site-search",
        "readOnly": true
      }
    ],
    "markdownArtifacts": {
      "enabled": true,
      "extension": ".md",
      "maxPages": 0,
      "includeTitle": true
    },
    "apache": {
      "enabled": false,
      "outputPath": ".htaccess",
      "linkHeaders": true,
      "contentSignalsHeader": true,
      "markdownNegotiation": true,
      "discoveryResourceHeaders": true
    },
    "a2aAgentCard": {
      "enabled": false
    },
    "mcpServerCard": {
      "enabled": false
    },
    "openApi": {
      "enabled": false
    },
    "markdownNegotiation": true
  }
}
```

If `agentSkills.skills` is empty, PowerForge writes a conservative default
`site-assistant` skill and computes the SHA-256 digest required by the Agent
Skills Discovery index.

Optional discovery documents such as Agent Skills and `agents.json` are reported
as informational when disabled or absent. They become required checks only when
the matching generator is enabled in `site.json`.

When `webMcp` is enabled, every entry in `webMcpTools` is a route-scoped
contract. PowerForge verifies that the route declares the configured tool and
loads a same-origin external script marked with `data-powerforge-webmcp`.
Each `page-tool` adapter must also declare its exact `data-webmcp-tool-name`, so
multiple tools on one route cannot satisfy each other's readiness contract.
Remote verification resolves relative resources against the final page URL and
rejects cross-origin redirects for the page, index, or runtime asset.

The read-only `site-search` kind emits
`/assets/powerforge/webmcp-site-search.v1.js`. Its script must be byte-for-byte
the canonical embedded PowerForge runtime, and the declared index must be a
bounded JSON array. A theme search page should expose
the tool name, description, and index route on its existing search surface:

```html
<main data-webmcp-site-search
      data-webmcp-tool-name="search_site"
      data-webmcp-tool-description="Search this website's public content."
      data-webmcp-search-index="/search/index.json">
  <!-- Keep the normal visible search form here. -->
</main>
<script src="/assets/powerforge/webmcp-site-search.v1.js"
        data-powerforge-webmcp defer></script>
```

The runtime registers a read-only tool with a 200-character query limit. It
returns three results by default and accepts at most five. The complete
serialized result is capped at 1,500 characters; `moreResultsAvailable` and
`outputTruncated` tell the caller when the bounded response omits matches.
Result URLs are never truncated into a different target; entries whose URL
cannot fit the declared result contract are omitted.

The generic runtime accepts only a same-origin JSON array containing at most
5,000 entries and 8 MiB of decoded UTF-8 data. It reads the response as a
bounded stream before parsing it. The generator applies those same limits to
the aggregate and every shard, preserving deterministic search order and
omitting entries that no longer fit. Generated search indexes exclude arbitrary
front matter from both `meta` and `searchText`; presentation fields such as
page scripts and CSS are not agent/search input. A small compatibility allowlist
preserves metadata used by current search and localization UIs, while aliases
and explicit search keywords remain searchable.

`search/manifest.json` records the source count, emitted count, truncation state,
byte count, and SHA-256 digest for the aggregate, language, and collection
indexes. These values prove the generated artifact; they do not prove HTTP
compression. Remote `agent-ready scan` reports
large JSON indexes delivered without `Content-Encoding` as a warning. When
Apache support is enabled, the generated managed block prefers Brotli and falls
back to deflate for JSON if the corresponding modules are available.

Themes with richer ranking can call `PowerForgeWebMcpSearch.bindAdapter(...)` to
reuse their visible search implementation. If no adapter is bound, the runtime
loads and reuses the generated index for the lifetime of the page;
`PowerForgeWebMcpSearch.invalidateIndex()` explicitly clears that cache. Browsers
without WebMCP continue to use the normal search page. Engine-generated fallback
pages use route-relative index/runtime URLs so sites hosted below an origin path
remain functional; theme-owned pages should do the same or include the deployed
base path explicitly.

The `page-tool` kind is a verification contract for a product-owned adapter;
PowerForge does not generate its behavior. Use it only when an existing visible
page already owns the operation. The rendered route must contain exactly one
matching surface and a marked, executable same-origin external adapter:

```html
<main data-webmcp-page-tool
      data-webmcp-tool-name="query_dns_records"
      data-webmcp-tool-description="Resolve one bounded public DNS record query."
      data-webmcp-read-only="true">
  <!-- Existing visible product controls and results. -->
</main>
<script src="/assets/product-dns-tool.js"
        data-powerforge-webmcp
        data-webmcp-tool-name="query_dns_records" defer></script>
```

The marker, description, read-only declaration, and reachable adapter are
artifact evidence, not proof of the callback's behavior. Exercise product tools
in a WebMCP-capable browser and verify their input bounds, annotations, response
budget, cancellation behavior, and visible result synchronization. Keep product
logic in its existing owner; the marked adapter should only validate and map the
Website Tool call into that visible workflow.

See `Docs/PowerForge.Web.WebMcpRollout.md` for the Phase 2 site eligibility
rules, Phase 3 safety contract, and the distinction between public documentation
tools and authenticated product actions.

Generated `agents.json` reports `capabilities.webMcp: true` and lists
`webMcpTools` only after the rendered routes and registration runtime pass local
verification. Configuration alone is not advertised as support.

If `apiCatalog.entries` is empty but `_site/api/index.json` exists, PowerForge
infers a basic API documentation entry. For public programmable APIs, prefer
explicit entries that point `serviceDesc` at an OpenAPI document.

If `apiCatalog.includeProjectApiReferences` is true, PowerForge also scans the
rendered site for local `/projects/{slug}/api/index.html` pages and adds those
hosted API reference surfaces to the linkset. It uses
`data/projects/catalog.json` only to improve titles and classify local
PowerShell API references; set `projectCatalogPath` to a different relative
site-root path when needed, or leave it blank to use the default catalog.
External project sites must publish their own API catalogs instead of being
claimed by the hub. A generated
`/projects/{slug}/api/index.json` is used as `service-desc` when present.

If `markdownArtifacts.enabled` is true, `agent-ready prepare` converts rendered
HTML pages to sibling Markdown files such as `index.md` and `docs/index.md`.
These files are useful directly and can be served by host-level rules for
`Accept: text/markdown`. Keep this disabled on very large sites unless the
extra files are expected; use `maxPages` for staged rollouts.

If `apache.enabled` is true, `agent-ready prepare` appends a managed block to
`.htaccess` (or `apache.outputPath`). The block emits homepage discovery Link
headers with `mod_headers`, sets Content Signals as a response header when
configured, sets content types/CORS for generated well-known resources, and
uses `mod_rewrite` to serve generated Markdown artifacts when a request sends
`Accept: text/markdown`. This is intended for Apache static deployments where
`AllowOverride` and `mod_headers`/`mod_rewrite` are enabled.

The generated Apache Markdown negotiation rules cover the site root and
directory-style routes such as `/docs/` that map to `index.md`. If a deployment
serves extensionless deep paths without trailing slashes, configure canonical
trailing-slash redirects before the managed PowerForge block.

Do not enable MCP, WebMCP, OAuth, OpenAPI, A2A, or commerce settings just to
make a scanner green. These discovery files are contracts. Publish them only
when the site really has the corresponding endpoint, tool surface, protected
resource, or commerce flow.

## Pipeline

Run `agent-ready` after `sitemap` and after any step that writes `_headers`.
For optimized static sites, put it after `optimize` so discovery and security
headers are appended after cache headers:

```json
{
  "task": "agent-ready",
  "id": "agent-ready",
  "dependsOn": "optimize-site",
  "operation": "prepare",
  "config": "./site.json",
  "siteRoot": "./_site",
  "failOnFailures": true,
  "modes": ["ci"]
}
```

Use `operation: "verify"` when you only want to check already-generated output.
Use `operation: "scan"` when CI should check a deployed URL:

```json
{
  "task": "agent-ready",
  "id": "agent-ready-live",
  "operation": "scan",
  "url": "https://example.com",
  "failOnFailures": true,
  "modes": ["ci"]
}
```

## CLI

Prepare local static output:

```powershell
powerforge-web agent-ready prepare --site-root .\_site --config .\site.json
```

Verify local output:

```powershell
powerforge-web agent-ready verify --site-root .\_site --config .\site.json --fail-on-failures
```

Scan a deployed site:

```powershell
powerforge-web agent-ready scan --config .\site.json --fail-on-failures
```

Exercise the real rendered Website Tool in Chromium:

```powershell
powerforge-web agent-ready exercise `
  --url https://example.com/search/ `
  --tool search_site `
  --query "convert Word documents to PDF" `
  --ensure-browser `
  --output json
```

The exercise command injects a test browser host before page scripts run,
captures the page's actual imperative registration, invokes its `execute`
callback, checks the schema and annotations, enforces the 1,500-character output
budget, and confirms that the visible search input received the same query. It
uses HtmlTinkerX/Playwright, the same browser owner as rendered site audits.

The equivalent pipeline operation is `agent-ready` with
`"operation": "exercise"`, an exact search-page `url`, and a `query`. Keep this
as a deployed acceptance step; local `prepare` and `verify` remain deterministic
artifact checks.

## Deployment Notes

The generated `_headers` file is compatible with Cloudflare Pages-style static
headers. Other hosts may need their own response-header configuration.

GitHub Pages does not consume `_headers` or `.htaccess`. For GitHub Pages sites,
generate the discovery files and Markdown artifacts, then put Cloudflare or
another edge in front if the live site must satisfy response-header and
`Accept: text/markdown` negotiation checks.

PowerForge retains its existing HSTS-enabled default for compatibility. Set
`AgentReadiness.SecurityHeaders.Hsts` to `false` for any site whose certificate
renewal and HTTPS recovery path are not proven. For Cloudflare-proxied GitHub
Pages, an edge response header does not prove that GitHub can renew the separate
origin certificate.

Apache deployments can set `agentReadiness.apache.enabled: true` and run
`agent-ready prepare` after any step that creates or filters `.htaccess`, so the
managed agent-readiness block is appended after redirect and cache artifacts.

For Cloudflare zones, enable Markdown for Agents in AI Crawl Control or with a
Configuration Rule. PowerForge can scan the deployed behavior, but it does not
toggle Cloudflare zone settings by itself.

Cloudflare's Markdown for Agents checks the deployed edge behavior: requests
with `Accept: text/markdown` should return markdown content. A local static
site build can prepare everything around that feature, but only the deployed
zone or origin can prove the negotiation result.
