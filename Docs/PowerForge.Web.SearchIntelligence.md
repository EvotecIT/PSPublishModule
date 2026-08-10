# PowerForge.Web Search Intelligence

PowerForge.Web can import daily search-performance observations, keep an idempotent local history, and produce evidence-linked opportunities. This is the first operational slice of the broader Search Intelligence design. It gives provider collectors, scheduled jobs, reports, and future Control.Web screens one owned data contract instead of letting each integration invent its own model.

The current release supports imported Search Analytics-style data, fleet provider configuration and capability checks, and authenticated Google Search Console Search Analytics collection. It does not yet collect Bing or Cloudflare data, and it does not crawl competitors or draft articles automatically.

## Ownership

| Capability | Owner | Current surface |
|---|---|---|
| Observation contract, normalization, identity and opportunity rules | `PowerForge.Web` | `WebSearchObservationBatch`, `WebSearchObservationNormalizer`, `WebSearchOpportunityAnalyzer` |
| Import orchestration and SQLite history | `powerforge-web` | `observe import`, backed by `DBAClientX.SQLite` |
| Opportunity report | `powerforge-web` | `opportunity list` human or JSON output |
| Provider identities, requested capabilities and credential references | `PowerForge.Web` | `WebSearchProviderConfiguration`, `WebSearchProviderDoctor` |
| Provider authentication and collection | Adapters in `PowerForge.Web` | Google Search Console collector with thin `powerforge-web observe collect` orchestration |
| Site-specific product facts and content changes | Owning site repository | Consume evidence; normal PR review remains the publication gate |
| Authenticated fleet UI | Future thin `Control.Web` consumer | Read the PowerForge Search service/API when that boundary is justified |

## Import contract

The JSON contract is versioned by `schemaVersion`. Version 2 adds durable collection coverage and explicit zero-data evidence; version 1 remains import-compatible and retains its original normalized JSON and deterministic identities. Provider and site identifiers are normalized to lowercase. When `runId` is omitted, the collection run receives a deterministic SHA-256 identity from its normalized evidence; each row then receives a deterministic identity scoped to that run. A caller may instead supply a stable run ID from an external collection system. Run IDs are scoped by provider and site, so independent collectors may reuse the same external identifier. Re-importing the same normalized run ID within that scope is safe: the SQLite store ignores identities it already contains and rejects that scoped ID if it points at different normalized evidence.

```json
{
  "schemaVersion": 2,
  "provider": "google-search-console",
  "siteId": "officeimo",
  "collectedAtUtc": "2026-08-02T08:00:00Z",
  "sourceKind": "api",
  "status": "complete",
  "collectionCoverage": {
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

An observation must contain a page or query dimension. Page values must be absolute HTTP(S) URLs. `collectedAtUtc` must include `Z` or an explicit numeric offset so run identity never depends on the collector machine's time zone. Clicks, impressions, CTR and position are validated before anything reaches storage, including rejection of non-finite numeric values and canonicalization of signed zero. A run cannot contain multiple rows for the same provider, site, date and dimension set because those rows would be ambiguous revisions. A `partial` batch may be empty so a collector can preserve an honest partial-run record. Collector-produced batches persist `collectionCoverage`: the requested date/search surface, the consecutive prefix of dates whose paging completed, and the first failed date/category for a partial run. Non-web observations require an explicit coverage `searchType`, so complete revisions cannot suppress or preserve the wrong search surface. Partial observations may belong only to those completed dates or the failed partition. An empty `complete` batch is accepted only when `zeroDataConfirmed` is true and every date in its durable coverage completed, which records the exact provider slice that returned no rows rather than silently accepting an ambiguous empty import.

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

Provider state deliberately separates `configurationReady`, `collectorAvailable` and `collectionReady`. Collector readiness is capability-specific: the current Google adapter implements `search.analytics`, so a registration that also requests `search.sitemaps` or `search.url-inspection` remains visibly incomplete. A valid registration can therefore be reviewed and deployed before every requested adapter capability ships without pretending that collection already works.

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

The adapter first lists visible Search Console properties and requires an exact normalized property match with a verified readable permission. Both the finality probe and analytics requests apply an anchored page filter for the fleet site's exact origin and path subtree, so a broad domain property cannot mix sibling sites into the selected fleet identity. It probes the requested range with `dataState=all` grouped by date and refuses to confirm any date at or after Google's `first_incomplete_date`. It then requests one finalized reporting date at a time, grouping by date, page, query, country and device. Each daily query pages in 25,000-row offsets until Google returns a short or empty page. Google currently exposes at most 50,000 top rows per day and search type and does not guarantee every underlying row, so a successful run means the requested API pages completed; it does not mean Search Console disclosed an exhaustive event-level dataset.

Rows already received are returned as a `partial` batch if a later page fails or the requested range reaches incomplete Google data. The durable coverage identifies completed daily partitions plus the failed boundary/category. The CLI imports that partial batch, returns exit code 1, and leaves a later complete run free to supersede it in reports. A successful zero-row response is stored as an explicit zero-data run only after Google reports the entire requested range as final. The `--evidence` value is a non-secret reference only: PowerForge stores it with the run but does not copy raw Google payloads, tokens or private account data into the SQLite database.

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

1. Bing collection plus an export fallback;
2. Cloudflare traffic observations and Lighthouse/CrUX performance contracts kept separate from search metrics;
3. scheduled collection, backfill, retention and static fleet reports;
4. competitor evidence through RadarX/HtmlTinkerX, followed by human-approved briefs and measured outcomes;
5. Google sitemap and URL-inspection capabilities when their operator workflows are defined.

Those additions must keep the current separation: first-party search facts, traffic facts, performance measurements, competitor evidence and recommendations are related, but they are not interchangeable datasets.
