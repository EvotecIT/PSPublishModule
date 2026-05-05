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
- static security headers for HSTS, CSP, X-Content-Type-Options,
  X-Frame-Options, Referrer-Policy, and discovery-resource CORS
- optional static Markdown artifacts generated from rendered HTML
- API catalog Linkset generation
- Agent Skills index + default SKILL.md generation
- agents.json generation
- optional A2A Agent Card generation
- optional MCP server card
- OpenAPI detection/configuration
- local HTML, JSON-LD, and semantic checks aligned with AI-readiness scanners
- local WebMCP smoke detection for imperative `navigator.modelContext` calls
  and exact declarative HTML tool attributes such as `tool-name` and
  `tool-description`
- remote scan of live headers and well-known URLs

Cloudflare Markdown for Agents is a host-level feature. PowerForge verifies it
with a live scan, but static output cannot prove that `Accept: text/markdown`
will be negotiated by the deployed edge or origin.

PowerForge can also generate Markdown artifacts itself. This is separate from
live HTTP negotiation: the static site can contain `/index.md` and
`/docs/index.md`, but the deployed host or edge still needs a rule if the same
HTML route should return Markdown for `Accept: text/markdown`.

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

If `apiCatalog.entries` is empty but `_site/api/index.json` exists, PowerForge
infers a basic API documentation entry. For public programmable APIs, prefer
explicit entries that point `serviceDesc` at an OpenAPI document.

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
powerforge-web agent-ready scan --url https://example.com --fail-on-failures
```

## Deployment Notes

The generated `_headers` file is compatible with Cloudflare Pages-style static
headers. Other hosts may need their own response-header configuration.

GitHub Pages does not consume `_headers` or `.htaccess`. For GitHub Pages sites,
generate the discovery files and Markdown artifacts, then put Cloudflare or
another edge in front if the live site must satisfy response-header and
`Accept: text/markdown` negotiation checks.

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
