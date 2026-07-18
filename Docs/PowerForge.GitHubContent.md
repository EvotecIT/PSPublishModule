# Generated GitHub Repository Content

PowerForge can keep sponsor recognition in a repository up to date without replacing the rest of a README or `SPONSORS.md`. The first provider reads public GitHub Sponsors data; the config and managed Markdown layer are designed to accept other repository content providers later.

The command is opt-in. It writes only configured marker blocks, fails before writing when GitHub cannot be queried, and rejects missing or duplicate markers unless the config explicitly allows a new file or appended block.

## Configure sponsor recognition

Copy [`Examples/GitHubContent/github-content.json`](../Examples/GitHubContent/github-content.json) to `.powerforge/github-content.json`, then set `sponsorableLogin` and remove the example override and company.

Tier recognition is disabled unless `tierRecognition.enabled` is `true`. With `useDefaultTiers` enabled, PowerForge maps the selected GitHub monthly funding tier into these public recognition bands:

| Recognition tier | Minimum monthly tier |
| --- | ---: |
| Principal | $1,000 |
| Platinum | $100 |
| Gold | $30 |
| Silver | $10 |
| Bronze | $5 |
| Sponsors | Custom, unavailable, or below a configured band |

Funding amounts and GitHub funding-tier names are not rendered or returned in JSON results. They remain internal to one tier-enabled run and only decide where a current sponsor appears. When tier recognition is disabled, PowerForge does not request funding-tier prices at all.

Enabling tier recognition still reveals an approximate funding band that GitHub does not publish to every viewer by default. Turn it on only when tier-based public recognition is part of the sponsor reward and the displayed bands match that policy. Leave it disabled for an ungrouped roster.

To place an account in a different public recognition tier, add an override. Overrides take precedence over the GitHub tier:

```json
{
  "login": "some-account",
  "recognitionTierKey": "Gold"
}
```

Manual entries use the same renderer and tier keys. They are intended for companies or people whose support is tracked outside GitHub:

```json
{
  "key": "example-company",
  "displayName": "Example Company",
  "profileUrl": "https://example.com",
  "avatarUrl": "https://example.com/logo.png",
  "recognitionTierKey": "Platinum"
}
```

If `tiers` contains custom definitions, those definitions replace the defaults. `unmappedTierKey` must name one of the configured tiers.

## Choose the output

An output can create a full `SPONSORS.md`, update an existing block, or add a compact avatar row to a README. Output paths are relative to the repository root.

New files require `createIfMissing: true`. Existing files require markers such as:

```markdown
<!-- POWERFORGE:sponsors-readme:START -->
<!-- POWERFORGE:sponsors-readme:END -->
```

Set `missingBlockBehavior` to `Append` only when the generator should add a missing block to an existing document. The default, `Fail`, protects hand-written content from accidental placement changes.

`Full` output groups current sponsors by recognition tier and keeps former public sponsors in a separate, untiered list. `Compact` output shows current sponsor avatars and can link to the full roster.

## Run locally

Provide an authenticated GitHub token through the configured environment variable. Do not store the token in the JSON file.

```powershell
$env:GITHUB_TOKEN = '<token>'
powerforge github content sync --config .\.powerforge\github-content.json
```

Use `--output json` when another tool needs the changed paths and normalized sponsor records. Automation should also pass `--restrict-output-root <repository-root>`; PowerForge then rejects outside paths and symlink/reparse-point traversal before it writes any configured output. The reusable action supplies this restriction automatically.

## Automate updates

The reusable workflow can run on a schedule or on demand. It stages only document paths returned by PowerForge.

```yaml
name: Update sponsors

on:
  workflow_dispatch:
  schedule:
    - cron: "17 5 * * 1"

permissions:
  contents: write

jobs:
  sponsors:
    # Pin a released tag or immutable commit SHA.
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-github-content.yml@<commit-sha>
    with:
      config_path: .powerforge/github-content.json
```

The reusable workflow resolves its engine checkout from the exact commit that contains the called workflow. An explicit `powerforge_ref` override is available for controlled testing, but the default never follows a mutable branch.

Set `commit_changes: false` to generate and validate content without pushing a commit. A caller may also pass a maintainer-authorized user token when accurate GitHub tier mapping is required:

```yaml
    secrets:
      github_token: ${{ secrets.SPONSORS_TOKEN }}
```

GitHub does not expose a public sponsorship's selected tier to every viewer. The repository `GITHUB_TOKEN` is sufficient for the public roster, but it may return no tier for every sponsor. In that case all accounts use `unmappedTierKey`; no amount is guessed. Store a suitable user token as `SPONSORS_TOKEN` (or another repository or organization secret) when tier grouping must reflect the selected GitHub tier.

Set `requireFundingTierData` to `true` for that case. If GitHub withholds every current sponsor's tier, PowerForge then fails before writing instead of silently moving the whole roster into the fallback tier. Individual custom sponsorships can still use the fallback while other sponsors retain their mapped tiers.

## Public data and former sponsors

PowerForge always queries GitHub with `includePrivate: false`; private sponsorships are not requested or rendered. Current sponsorships and all public sponsorships are queried separately so former public sponsors can be recognized without requesting sponsorship IDs or private metadata. Custom GraphQL endpoints must use HTTPS because every request carries a bearer token.

An account that stopped and later resumed sponsorship is treated as current. Former sponsors are not assigned a historical recognition tier because GitHub’s public connection does not provide a reliable status-and-tier history for that purpose.
