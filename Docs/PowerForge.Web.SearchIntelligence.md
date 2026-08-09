# PowerForge.Web Search Intelligence

PowerForge.Web can import daily search-performance observations, keep an idempotent local history, and produce evidence-linked opportunities. This is the first operational slice of the broader Search Intelligence design. It gives provider collectors, scheduled jobs, reports, and future Control.Web screens one owned data contract instead of letting each integration invent its own model.

The current release supports imported Search Analytics-style data. It does not yet authenticate to Google Search Console, Bing Webmaster Tools, or Cloudflare, and it does not crawl competitors or draft articles automatically.

## Ownership

| Capability | Owner | Current surface |
|---|---|---|
| Observation contract, normalization, identity and opportunity rules | `PowerForge.Web` | `WebSearchObservationBatch`, `WebSearchObservationNormalizer`, `WebSearchOpportunityAnalyzer` |
| Import orchestration and SQLite history | `powerforge-web` | `observe import`, backed by `DBAClientX.SQLite` |
| Opportunity report | `powerforge-web` | `opportunity list` human or JSON output |
| Provider authentication and collection | Future adapters in `powerforge-web` | Normalize into the same observation batch |
| Site-specific product facts and content changes | Owning site repository | Consume evidence; normal PR review remains the publication gate |
| Authenticated fleet UI | Future thin `Control.Web` consumer | Read the PowerForge Search service/API when that boundary is justified |

## Import contract

The JSON contract is versioned by `schemaVersion`. Provider and site identifiers are normalized to lowercase. Every collection run and row receives a deterministic SHA-256 identity from its evidence. Re-importing the same run is safe: the SQLite store ignores identities it already contains and rejects a caller-supplied run ID if it points at different normalized evidence.

```json
{
  "schemaVersion": 1,
  "provider": "google-search-console",
  "siteId": "officeimo",
  "collectedAtUtc": "2026-08-02T08:00:00Z",
  "sourceKind": "api",
  "status": "complete",
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

The complete schema is in `Schemas/powerforge.web.search-observations.schema.json`. A runnable input is available at `Examples/PowerForge.Web/Search/observations.json`.

An observation must contain a page or query dimension. Page values must be absolute HTTP(S) URLs. Clicks, impressions, CTR and position are validated before anything reaches storage. A run cannot contain multiple rows for the same provider, site, date and dimension set because those rows would be ambiguous revisions. A `partial` batch may be empty so a collector can preserve an honest partial-run record; a `complete` batch must contain observations.

## Import observations

```powershell
powerforge-web observe import `
    --input .\observations.json `
    --database .\.powerforge\search.db `
    --output json
```

Use `--provider` or `--site` only when an export format cannot carry those fields. Overrides still pass through the same normalization and validation.

The import result reports input, inserted and duplicate counts plus the database schema version. The stored run keeps the normalized manifest and a non-secret evidence reference. Providers may revise recent daily metrics, so later collection runs remain immutable revisions while reports select only the latest snapshot for each provider/site/date/dimension. Raw provider payloads should remain in a separately governed evidence location; do not put access tokens or private account data in the import file.

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

1. capability and identity checks before provider collection;
2. Google Search Console and Bing adapters plus export fallbacks;
3. Cloudflare traffic observations and Lighthouse/CrUX performance contracts kept separate from search metrics;
4. scheduled collection, retention and static fleet reports;
5. competitor evidence through RadarX/HtmlTinkerX, followed by human-approved briefs and measured outcomes.

Those additions must keep the current separation: first-party search facts, traffic facts, performance measurements, competitor evidence and recommendations are related, but they are not interchangeable datasets.
