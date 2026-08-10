# PowerForge.Web Search Intelligence

PowerForge.Web can collect search-performance and first-party traffic observations, keep an idempotent fleet history, and produce evidence-linked opportunities. Search and traffic use separate contracts and tables so related measurements can be compared without pretending they mean the same thing.

The current release supports imported Search Analytics-style data, fleet provider configuration and capability checks, authenticated Google Search Console collection, Bing Webmaster API collection with a CSV export fallback, and Cloudflare end-user HTTP traffic collection. It does not yet collect Lighthouse/CrUX performance data, and it does not crawl competitors or draft articles automatically.

## Ownership

| Capability | Owner | Current surface |
|---|---|---|
| Observation contract, normalization, identity and opportunity rules | `PowerForge.Web` | `WebSearchObservationBatch`, `WebSearchObservationNormalizer`, `WebSearchOpportunityAnalyzer` |
| Import orchestration and SQLite history | `powerforge-web` | `observe import`, backed by `DBAClientX.SQLite` |
| Opportunity report | `powerforge-web` | `opportunity list` human or JSON output |
| Provider identities, requested capabilities and credential references | `PowerForge.Web` | `WebSearchProviderConfiguration`, `WebSearchProviderDoctor` |
| Provider authentication and collection | Adapters in `PowerForge.Web` | Google Search Console and Bing Webmaster collectors with thin `powerforge-web` orchestration |
| First-party traffic observations | `PowerForge.Web` | `WebTrafficObservationBatch`, Cloudflare GraphQL collector, and `traffic collect/list` |
| Site-specific product facts and content changes | Owning site repository | Consume evidence; normal PR review remains the publication gate |
| Authenticated fleet UI | Future thin `Control.Web` consumer | Read the PowerForge Search service/API when that boundary is justified |

## Import contract

The JSON contract is versioned by `schemaVersion`. Version 2 added durable collection coverage and explicit zero-data evidence. Version 3 adds an explicit coverage mode: `daily` for providers queried as consecutive daily partitions, and `snapshot` for provider endpoints or exports that return a dated snapshot in one operation. Versions 1 and 2 remain import-compatible and retain their original normalized JSON and deterministic identities. Provider and site identifiers are normalized to lowercase. When `runId` is omitted, the collection run receives a deterministic SHA-256 identity from its normalized evidence; each row then receives a deterministic identity scoped to that run. A caller may instead supply a stable run ID from an external collection system. Run IDs are scoped by provider and site, so independent collectors may reuse the same external identifier. Re-importing the same normalized run ID within that scope is safe: the SQLite store ignores identities it already contains and rejects that scoped ID if it points at different normalized evidence.

```json
{
  "schemaVersion": 3,
  "provider": "google-search-console",
  "siteId": "officeimo",
  "collectedAtUtc": "2026-08-02T08:00:00Z",
  "sourceKind": "api",
  "status": "complete",
  "collectionCoverage": {
    "mode": "daily",
    "fromDate": "2026-08-01",
    "throughDate": "2026-08-01",
    "searchType": "web",
    "completedDates": ["2026-08-01"]
  },
  "configurationHash": "sha256:fleet-configuration-hash",
  "evidenceReference": "evidence/gsc-officeimo-2026-08-01.json",
  "observations": [
    {
      "date": "2026-08-01",
      "page": "https://officeimo.com/convert/",
      "query": "convert office files",
      "country": "pl",
      "device": "desktop",
      "searchType": "web",
      "clicks": 3,
      "impressions": 240,
      "averagePosition": 9.4
    }
  ]
}
```

The complete portable structural schema is in `Schemas/powerforge.web.search-observations.schema.json`. A runnable input is available at `Examples/PowerForge.Web/Search/observations.json`. JSON Schema Draft 2020-12 cannot compare sibling numeric fields, so semantic cross-field invariants such as `clicks <= impressions` are declared in schema annotations and enforced by `WebSearchObservationNormalizer` during import. Schema validation is therefore a structural preflight, while the normalizer is the authoritative semantic gate.

An observation must contain a page or query dimension. Page values must be absolute HTTP(S) URLs. `collectedAtUtc` must include `Z` or an explicit numeric offset so run identity never depends on the collector machine's time zone. Clicks, impressions, CTR and position are validated before anything reaches storage, including rejection of missing required counts and non-finite numeric values plus canonicalization of signed zero. A run cannot contain multiple rows for the same provider, site, date and dimension set because those rows would be ambiguous revisions. A `partial` batch may be empty so a collector can preserve an honest partial-run record. Collector-produced batches persist `collectionCoverage`: in `daily` mode, the consecutive prefix of dates whose paging completed plus the first failed date/category; in `snapshot` mode, only dates explicitly present in successful provider responses, with a non-date-bound failure category for partial snapshots. Non-web observations require an explicit coverage `searchType`, so complete revisions cannot suppress or preserve the wrong search surface. Schema-v3 snapshot imports can also declare `dimensionScopes` (`page`, `query`, or `page-query`) so one single-dimension export cannot supersede evidence from another dimension; schema-v2 manifests omit that newer member and remain byte-stable when re-imported. Storage preserves the date-to-dimension association of snapshot observations, preventing a query-only date from replacing page evidence merely because another date in the same run contained page rows. Partial observations may belong only to completed dates or the failed partition. An empty `complete` batch is accepted only when `zeroDataConfirmed` is true and every date in its durable coverage completed; snapshot zero confirmation additionally requires explicit provider evidence for every requested date.

## Import observations

```powershell
powerforge-web observe import `
    --input .\observations.json `
    --database .\.powerforge\search.db `
    --output json
```

Use `--provider` or `--site` only when an export format cannot carry those fields. Overrides still pass through the same normalization and validation.

Position-based opportunity rules use only observations that contain both positive impressions and average-position evidence. Unpositioned rows remain in imported history and report observation counts, but they cannot inflate the impressions, clicks, CTR, confidence, date range, or evidence links attached to a position-based opportunity.

The public analyzer rejects competing revisions for the same provider, site, date, and dimensions because an observation row does not carry enough run metadata to choose the newest revision safely. Querying through the SQLite store resolves revisions by collection time first; direct library callers must make the same choice before analysis.

Search storage initializes and migrates its schema transactionally. It initializes only an empty SQLite database and refuses to claim an unrelated version-zero database. Human-readable Search command output escapes control and line-separator characters from provider text; JSON output retains the original normalized values for machine consumers.

The import result reports input, inserted and duplicate counts plus the database schema version. The stored run keeps the normalized manifest and a non-secret evidence reference. Providers may revise recent daily metrics, so later collection runs remain immutable revisions. Reports select the newest complete covered provider/site/date/search-type slice before selecting its dimensions, so rows omitted by a newer complete revision do not survive from an older snapshot. A partial-only dimension remains available only when no complete covered revision supersedes it. A provider/site pair may have only one run at a given `collectedAtUtc`; collectors must use the actual completion time so competing revisions never rely on arbitrary ID ordering. Raw provider payloads should remain in a separately governed evidence location; do not put access tokens or private account data in the import file.

## Configure and inspect providers

Keep fleet identities and provider intent in one reusable JSON document rather than duplicating authentication choices across site repositories. The structural schema is `Schemas/powerforge.web.search-providers.schema.json`, and `Examples/PowerForge.Web/Search/providers.json` shows Google Search Console, Bing Webmaster, Cloudflare, Lighthouse and CrUX registrations for one site.

Credential values never belong in provider configuration. A provider references only an environment variable name and the expected credential shape:

```json
{
  "id": "google-search-console",
  "kind": "google-search-console",
  "capabilities": ["search.analytics"],
  "credential": {
    "kind": "google-service-account-file",
    "environmentVariable": "POWERFORGE_GSC_CREDENTIALS_FILE"
  },
  "settings": {
    "property": "sc-domain:officeimo.com"
  }
}
```

Run the capability doctor before collection:

```powershell
powerforge-web provider doctor `
    --config .\search-providers.json `
    --output json
```

The doctor validates schema version, unique fleet identities, canonical site URLs, provider kinds, requested capabilities, provider-specific non-secret settings, credential kinds and whether the referenced environment variables are visible to the current process. Property names and stable identifiers use the exact casing and whitespace accepted by the published schema. The loader rejects duplicate JSON object members before deserialization, so a later member cannot silently replace reviewed intent. The doctor never emits credential values. Secret-looking settings are rejected, and only the catalog's known non-secret setting names may contribute values to configuration identity; unsupported setting values are redacted as well. A missing credential for an enabled provider is an error; a disabled provider may keep an unavailable credential reference as a warning so fleet configuration can be prepared before a rollout. A successful report emits a deterministic `configurationHash` over normalized non-secret configuration; reports with any blocking semantic error omit it. Collectors should copy a successful report's hash into observation batches so historical runs can be tied to the configuration that produced them.

Provider state deliberately separates `configurationReady`, `collectorAvailable` and `collectionReady`. Collector readiness is capability-specific: the current Google and Bing adapters implement `search.analytics`; sitemap and URL-inspection capabilities remain visibly incomplete. A valid registration can therefore be reviewed and deployed before every requested adapter capability ships without pretending that collection already works.

## Collect Google Search Console observations

Enable the Search Console API for the Google Cloud project that owns the service account, then grant the service-account email access to the exact Search Console property in `settings.property`. The collector requests only the `webmasters.readonly` OAuth scope. The configured environment variable can contain either the service-account JSON itself (`google-service-account-json`) or the path to a service-account JSON file (`google-service-account-file`). Other Google credential types are rejected.

Collect a bounded date range and import it into the existing history in one command:

```powershell
powerforge-web observe collect `
    --config .\search-providers.json `
    --database .\.powerforge\search.db `
    --site officeimo `
    --provider google-search-console `
    --from 2026-08-01 `
    --to 2026-08-07 `
    --search-type web `
    --evidence evidence/gsc-officeimo-2026-08-01-to-07 `
    --output json
```

The adapter first lists visible Search Console properties and requires an exact normalized property match with a verified readable permission. Both the finality probe and analytics requests apply an anchored page filter for the fleet site's exact origin and path subtree, so a broad domain property cannot mix sibling sites into the selected fleet identity; an exact subpath URL remains in scope when it carries a query string. It probes the requested range with `dataState=all` grouped by date and refuses to confirm any date at or after Google's `first_incomplete_date`. It then requests one finalized reporting date at a time, grouping by date, page, query, country and device. Each daily query pages in 25,000-row offsets until Google returns a short page or the documented 50,000-row daily cap is reached. One in-memory collection batch is limited to seven daily partitions, so larger backfills must run as consecutive bounded jobs. Google does not guarantee every underlying row, so a successful run means the requested API pages completed; it does not mean Search Console disclosed an exhaustive event-level dataset.

Rows already received are returned as a `partial` batch if a later page fails or the requested range reaches incomplete Google data. The durable coverage identifies completed daily partitions plus the failed boundary/category. The CLI imports that partial batch, returns exit code 1, and leaves a later complete run free to supersede it in reports. A successful zero-row response is stored as an explicit zero-data run only after Google reports the entire requested range as final. The `--evidence` value is a non-secret reference only: PowerForge stores it with the run but does not copy raw Google payloads, tokens or private account data into the SQLite database.

## Collect Bing Webmaster observations

Create a Bing Webmaster API key and expose it through the environment variable referenced by the provider's `bing-api-key` credential. The collector uses Bing's HTTPS JSON/HTTP API surface; it does not use the SOAP or POX protocols that Bing is retiring on August 31, 2026. The API key is never copied into configuration hashes, observations, evidence references, CLI output or provider error messages.

```powershell
powerforge-web observe collect `
    --config .\search-providers.json `
    --database .\.powerforge\search.db `
    --site officeimo `
    --provider bing-webmaster `
    --from 2026-08-01 `
    --to 2026-08-07 `
    --search-type web `
    --evidence evidence/bing-officeimo-2026-08-01-to-07 `
    --output json
```

The adapter first lists the sites visible to the credential and requires an exact normalized URL match that Bing reports as verified. It then reads dated top-query and top-page statistics and uses rank-and-traffic totals only to confirm a genuine zero-data response. Bing documents query and page statistics as top-result datasets, so a complete run means those provider responses completed for the requested range; it does not claim event-level completeness. Page and query rows remain separate dimensions rather than being joined into combinations Bing did not return. Sitemap capability is not advertised by this collector yet because sitemap inventory needs its own provider-neutral contract.

If API access is unavailable, download Search Performance data from Bing Webmaster Tools and import it through the same fleet configuration:

```powershell
powerforge-web observe import-bing `
    --config .\search-providers.json `
    --input .\bing-search-performance.csv `
    --database .\.powerforge\search.db `
    --site officeimo `
    --provider bing-webmaster `
    --from 2026-08-01 `
    --to 2026-08-07 `
    --collected-at 2026-08-10T12:34:56Z `
    --search-type web `
    --evidence evidence/bing-search-performance.csv `
    --output json
```

The fallback accepts comma-, semicolon- or tab-delimited CSV with a real date column, clicks, impressions, and at least one page or query column. Export-only registrations declare the owning Bing property through the validated `siteUrl` setting so query-only rows retain a trustworthy site boundary. CTR and average position are optional. Quoted fields, correctly grouped invariant thousands separators, and percentage CTR values are supported; malformed count grouping and nonzero CTR with zero impressions are rejected instead of being silently reinterpreted. Every row must fall inside the declared date range, and the file is normalized before storage is opened. `--collected-at` is required and becomes part of the stable run identity, so importing the same export again with the same timestamp remains idempotent even if the file was copied or downloaded again. Aggregate-only exports without dates or dimensions are rejected because converting a range total into daily evidence would fabricate data. Header-only exports are also rejected because they contain no provider-owned range evidence and cannot prove zero data for caller-supplied dates.

## Collect Cloudflare traffic observations

Configure an API token with read access to the exact zone and expose it through the environment variable referenced by the provider's `cloudflare-api-token` credential. Before GraphQL collection, the collector reads the zone identity and requires its canonical name to own the configured fleet site's base host. It then reads the zone-specific dataset settings, including availability, retention and maximum row count. Every analytics request is constrained to the configured site's exact host and, when the base URL contains a path, that path subtree. This prevents a valid token, shared zone or unrelated zone ID from storing another site's traffic under the wrong fleet identity.

```powershell
powerforge-web traffic collect `
    --config .\search-providers.json `
    --database .\.powerforge\search.db `
    --site officeimo `
    --provider cloudflare `
    --from 2026-08-01 `
    --to 2026-08-07 `
    --evidence evidence/cloudflare-officeimo-2026-08-01-to-07 `
    --output json
```

Each closed UTC reporting date is queried separately through `httpRequestsAdaptiveGroups`, grouped by scheme, host and request path, filtered to `requestSource: eyeball`, and stored as requests, visits, edge response bytes and sampling interval. Scheme, host and path must all remain inside the configured fleet site boundary. The current UTC date and future dates are rejected before any provider request because they cannot support complete or zero-data claims. A later failed date preserves earlier completed partitions. Reaching the provider's row limit produces an explicit partial run because the collector cannot prove that every host/path row was returned.

Cloudflare's request count is an HTTP traffic metric, not a browser page-view metric, and a visit is not a unique visitor. Adaptive datasets may return estimates; `sampleInterval` remains attached to every observation and `traffic list` reports when sampled estimates are present. This keeps future Cloudflare Web Analytics/RUM page views and CrUX field performance in their own truthful contracts.

```powershell
powerforge-web traffic list `
    --database .\.powerforge\search.db `
    --site officeimo `
    --provider cloudflare `
    --from 2026-08-01 `
    --to 2026-08-07 `
    --output json
```

`traffic list` selects one best partition per provider, site and reporting date, preferring completed partitions before recency. A date completed before a later failure remains complete evidence even though its parent run is partial. Every totals query requires `--provider`, preventing independently collected providers from being added together or from hiding complementary gaps. Its JSON and human output distinguish a missing database, no matching evidence, partial evidence, missing dates inside a bounded range and an explicit complete-zero run; partial, incomplete or missing evidence returns a non-zero exit code instead of presenting ordinary-looking totals. The traffic contract is published at `Schemas/powerforge.web.traffic-observations.schema.json`; `Examples/PowerForge.Web/Search/traffic-observations.json` is a runnable example. Traffic and search runs share the transactional fleet database and deterministic revision rules but use independent tables and normalizers.

## List opportunities

```powershell
powerforge-web opportunity list `
    --database .\.powerforge\search.db `
    --site officeimo `
    --provider google-search-console `
    --from 2026-07-01 `
    --to 2026-07-31 `
    --min-impressions 100 `
    --min-ctr 0.02 `
    --output json
```

The first analyzer has two deliberately narrow rules:

- `search.weak-page`: meaningful impressions with a weighted average position from 8 through 20.
- `search.ctr-underperformance`: meaningful impressions at position 10 or better with CTR below the configured threshold.

Each opportunity includes its rule and version, bounded date window, dimensions, clicks, impressions, calculated CTR, weighted position, score, confidence, explanation, recommendation and every supporting observation key. Recommendations ask for investigation; they do not promise rankings or conversions.

## Integration boundary

Provider adapters should preserve their raw evidence and map only stable Search Analytics fields into `WebSearchObservationBatch`. Provider-specific availability, sampling, quota and metric definitions belong in collection-run metadata added by the adapter, not in the opportunity rules.

The next implementation steps are:

1. Lighthouse and CrUX performance observations in their own contract;
2. scheduled collection, backfill, retention and static fleet reports;
3. competitor evidence through RadarX/HtmlTinkerX, followed by human-approved briefs and measured outcomes;
4. Google and Bing sitemap capabilities when their operator workflows and shared sitemap contract are defined.

Those additions must keep the current separation: first-party search facts, traffic facts, performance measurements, competitor evidence and recommendations are related, but they are not interchangeable datasets.
