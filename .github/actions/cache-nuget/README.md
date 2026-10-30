# NuGet cache policy

Use this action before the first `dotnet restore`, `dotnet run` or build, then call
it in `save` mode after validation. The restore output supplies the original key;
generated files cannot change it later in the job.

```yaml
- uses: actions/checkout@v7
- uses: ./.github/actions/setup-dotnet
- uses: ./.github/actions/cache-nuget
  id: nuget-cache
- run: dotnet test MyLibrary.sln -c Release
- uses: ./.github/actions/cache-nuget
  with:
    mode: save
    key: ${{ steps.nuget-cache.outputs.primary-key }}
    cache-hit: ${{ steps.nuget-cache.outputs.cache-hit }}
```

Consumers can reference `EvotecIT/PSPublishModule/.github/actions/cache-nuget`
at the same reviewed SHA as their shared workflows instead of copying the action.
Self-hosted runners keep their existing package stores and do not upload them.
On hosted runners, only `~/.nuget/packages` is cached. Keys include the OS,
architecture and dependency manifests, excluding generated `obj` and `bin`
files. PRs and manual, tag and feature-branch runs restore without saving, with
a prefix fallback. Default-branch pushes restore exact keys only. On a miss,
the ephemeral hosted package folder starts empty before any restore/build, then
successful validation saves the packages actually downloaded by that job.
Exact hits are not saved again.
The module workflow saves from its full Windows/Linux/macOS build jobs;
informational audits, smoke-only and manual validation jobs only restore.

A new default-branch key does not inherit obsolete versions from an older key.
No package is deleted after validation: temporary source-engine restores can
remove their own graphs, so remaining workspace graphs cannot prove which
packages were used. The job summary records the package directory's bytes and
ten largest dependencies before saving. SDK installations, workloads, browsers
and tool directories are never part of the archive. Cold population is limited
to changed default-branch dependency keys; PRs keep their prefix restoration.

The website runner applies the same policy before social-card detection and
caches npm's download directory separately using `package-lock.json`. Ordinary
test artifacts and website reports use seven-day retention. Deployment handoff
manifests keep their one-day lifetime. Release approval evidence and release
assets keep their established retention. The cache policy is checked out from
the workflow revision separately, so an older explicit engine pin still works.

## Delete closed-PR caches

Add this small caller in a repository's next ordinary PR, using the same reviewed
workflow SHA as its other PowerForge workflows:

```yaml
name: Closed PR caches
on:
  pull_request_target:
    types: [closed]
permissions: {}
jobs:
  cleanup:
    permissions:
      actions: write
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-pr-cache-cleanup.yml@REVIEWED_SHA
```

The shared workflow deletes only `refs/pull/N/merge` caches. It checks out no
source and executes no PR code. The base-context close event permits cleanup of
fork PRs without giving their build jobs write permissions. No cron is needed.

Existing cache entries remain until normal eviction or scoped cleanup. A merge
does not migrate immutable workflow pins or advance `actions-next`. Update both
the workflow reference and a separately pinned `powerforge_ref` together when
the consumer's next ordinary PR adopts the change. After adoption, compare the
cache usage/list APIs, restore hit summaries and comparable PR wall times; the
first default-branch push may need to populate the new cache namespace.
