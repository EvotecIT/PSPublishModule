# NuGet trusted publishing with local signing

I keep assembly Authenticode signing and NuGet author signing on the local USB
token. GitHub Actions publishes the resulting files with a short-lived key from
`NuGet/login`. The runner never rebuilds or repacks the release packages.
PowerShell Gallery publishing uses its existing flow.

## Stage a signed release locally

Use the existing `project.build.json` with an explicit package output directory,
the normal certificate thumbprint and timestamp service, and these settings:

```json
{
  "RootPath": "..",
  "OutputPath": "Artefacts/NuGet",
  "StageNuget": true,
  "SignAssemblies": true,
  "SignPackages": true,
  "CertificateThumbprint": "YOUR_EXISTING_SIGNING_CERTIFICATE_SHA1_THUMBPRINT",
  "TimeStampServer": "http://timestamp.digicert.com",
  "IncludeSymbols": true,
  "GitHubUsername": "EvotecIT",
  "GitHubRepositoryName": "YourLibrary",
  "GitHubAccessTokenEnvName": "GITHUB_TOKEN",
  "GitHubReleaseMode": "Single",
  "GitHubTagName": "v1.2.3",
  "GitHubTagConflictPolicy": "Fail"
}
```

Keep the repository's project selection and version configuration alongside these
settings. `RootPath` is relative to the configuration file; output paths are
relative to that root. All selected packages must share one output directory.
The SHA1 thumbprint selects the local certificate; it is different from the
SHA256 certificate fingerprint used in the manifest and workflow allow-list.

```powershell
Invoke-ProjectBuild -ConfigPath ./Build/project.build.json -StageNuget
# Equivalent installed CLI entry point:
powerforge nuget stage --config ./Build/project.build.json
```

Staging builds and signs assemblies before packing, signs packages with the
configured timestamp service, runs `dotnet nuget verify --all`, and requires
timestamped author signatures. It uploads the exact `.nupkg` and optional
`.snupkg` files with `nuget-release-manifest.json` to a **new draft** GitHub
release. No NuGet push occurs and NuGet publication credentials are not resolved,
even when the configuration also has `PublishNuget` enabled. The GitHub token is
still needed to create the draft and upload assets. Existing signing prompts
remain local.

Draft staging uses Single release mode and Fail tag conflict policy. It does not
reuse or replace a published release or an existing draft. Inspect and remove an
unwanted partial draft before staging that tag again. When the draft is ready,
publish it through GitHub. A draft created with the local GitHub token should
produce the normal release event when published; a release created by another
workflow's `GITHUB_TOKEN` may need a manual dispatch because GitHub suppresses
recursive workflow triggers.

Use staging directly through `Invoke-ProjectBuild` or `powerforge nuget stage`.
Module-owned package lanes and deferred combined-release checkpoints reject
`StageNuget` before building or resolving publication credentials. Keep their
existing configurations separate; PowerShell Gallery publication stays with its
current publisher. An explicit `PublishGitHub: false` request also prevents
staging from creating a draft.

Staging copies the signed package set into private temporary storage before
verification, manifest creation and draft upload. A later local build cannot
replace the files being uploaded. The snapshot is removed after upload. A
configured `PlanOnly: true` must be accompanied by an explicit `-Plan` request;
the CLI stage command requires execution and rejects that configuration.

The manifest records a source commit and, for each asset, its filename, nuspec
package id, normalized version, signed-file SHA256 and author certificate SHA256
fingerprint. The source SHA identifies the available local Git commit; it does
not assert that uncommitted files were absent. Stage the intended default-branch
source and inspect intentional working-tree changes before signing.

## Configure nuget.org and GitHub

Create a nuget.org trusted-publishing policy with these fields:

| Field | Value |
| --- | --- |
| Policy owner | **Evotec**, the nuget.org organization owning the packages |
| Repository owner | `EvotecIT` |
| Repository | The **calling** package repository, for example `YourLibrary` |
| Workflow file | The **calling** workflow filename, for example `publish-nuget.yml` |
| Environment | `nuget-release`, matching the protected GitHub environment |
| Scopes | Only the intended package ids or package glob; permit new versions, and new packages only when needed |

The publishing job runs in the calling repository's workflow and uses the shared
`.github/actions/nuget-publish` composite action. Do not call it through a
cross-repository reusable workflow: nuget.org rejects the resulting
`job_workflow_ref` because it names a workflow outside the policy's repository
([NuGet's tracked limitation](https://github.com/NuGet/NuGetGallery/issues/11000)).
The composite action preserves the calling workflow's OIDC identity while keeping
verification and publication in PowerForge. See GitHub's
[OIDC workflow identity documentation](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-with-reusable-workflows).

The `nuget-user` input is the NuGet account username used by the login action, not
necessarily the organization name selected as policy owner. The account must
have the required rights in Evotec. Follow the current
[NuGet trusted-publishing setup](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
and [NuGet/login inputs](https://github.com/NuGet/login).

Create the GitHub environment in the calling repository before running the
workflow. Select custom deployment rules with exactly one **tag** policy:
`v*` for prefix `v`, or `YourLibrary-v*` for prefix `YourLibrary-v`. Do not add
branch policies or additional tag policies. Use the repository's normal source
protection and any required environment reviewers. Store the allowed SHA256
author fingerprints independently of the release assets, in reviewed caller
YAML. Certificate
fingerprints are public metadata. During certificate rotation, explicitly allow
both certificates until the old one is retired.

Register the signing certificate on the relevant nuget.org account before the
pilot. Once a certificate is registered, uploads for that account must be signed
with a registered certificate. Check package ownership and signature requirements
when several accounts own a package. Trusted publishing replaces upload
authentication; it does not bypass the
[registered-certificate requirements](https://learn.microsoft.com/nuget/create-packages/sign-a-package).

## Call the publishing action

After the shared changes have been merged and PowerForge.Build containing these
commands has been published, set `POWERFORGE_NUGET_PUBLISHER_VERSION` to that
reviewed three-part version. Pin the composite action to the reviewed **full
commit SHA** in the caller. Replace the capitalized placeholders below before use.
This source PR alone does not make the new CLI available on nuget.org.

```yaml
name: Publish NuGet
on:
  release:
    types: [published]
  workflow_dispatch: {} # select the published release tag in the Run workflow UI

permissions:
  contents: read
  actions: read
  id-token: write

concurrency:
  group: nuget-publish-${{ github.repository }}
  cancel-in-progress: false
  queue: max

jobs:
  nuget:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    environment: nuget-release
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/nuget-publish@REVIEWED_FULL_COMMIT_SHA
        with:
          environment-name: nuget-release
          release-tag-prefix: v
          nuget-user: YOUR_NUGET_USERNAME
          author-fingerprints: YOUR_AUTHOR_CERTIFICATE_SHA256
          powerforge-version: ${{ vars.POWERFORGE_NUGET_PUBLISHER_VERSION }}
```

The action accepts `release: published` and manual dispatch **on the same
published tag**. A dispatch on `main` is rejected. It resolves annotated tags,
requires the tag to match the workflow source and to belong to the current
default branch history, and checks the environment's exact tag policy. Public
repositories use a standard GitHub-hosted Ubuntu runner. For private repositories,
set the job's `runs-on` to the existing self-hosted Linux capability, with PowerShell and GitHub CLI
available. It installs the pinned PowerForge.Build tool, downloads release
packages and the manifest, then checks hashes, package identities, timestamps,
author signatures and the independent signer allow-list before OIDC login.

`NuGet/login` supplies its temporary `NUGET_API_KEY` output only to the push step.
PowerForge consumes it through the existing `PublishApiKeyEnvName` resolution
path. Do not add a NuGet API key file, repository secret, environment secret or
local key to this flow. Invocation-owned download and tool directories are
removed after the job.

## Verification and retry behavior

These commands also work against already downloaded assets:

```powershell
powerforge nuget verify --manifest ./assets/nuget-release-manifest.json `
    --source-commit FULL_SOURCE_COMMIT_SHA --author-fingerprints AUTHOR_SHA256

# NUGET_API_KEY is supplied by NuGet/login in the workflow, never written to a file.
powerforge nuget publish --manifest ./assets/nuget-release-manifest.json `
    --source-commit FULL_SOURCE_COMMIT_SHA --author-fingerprints AUTHOR_SHA256 `
    --api-key-env NUGET_API_KEY
```

Before uploading anything, PowerForge reads every existing primary package from
nuget.org and verifies its signature, id, version and manifest author signer.
An unsigned, repository-only, differently signed or mismatched package stops
the whole release. Registry errors also stop publication instead of being treated
as missing versions. nuget.org adds a repository signature, so registry bytes
are intentionally **not** compared with the uploaded-file SHA256. The original
GitHub assets always retain their exact hash check.

Publication snapshots the complete package set into private invocation-owned
storage and verifies that snapshot against the manifest. Uploads and symbol
retries use those files, so another local build cannot replace the verified
inputs during registry preflight. The snapshot is removed when publication ends.

New packages are published in dependency order through the existing exact-file
publisher. There is no build or pack operation on those files. Registry readback
also runs after a duplicate response while an accepted version is still indexing.
Its author identity must match before the retry can succeed. Readback
waits up to five minutes per new primary package without uploading again. If
indexing takes longer, inspect nuget.org and dispatch the same published tag
again. Matching existing primary versions need no repeat primary upload.
Companion symbols can resume after an interrupted upload; the symbol service
does not expose the same primary-package download/readback contract. Symbol
ingestion and debugger availability remain part of pilot validation.

Local proof can validate package metadata, integrity failures, author identity,
credential suppression and retry decisions. The pilot must still establish the
actual OIDC policy match, environment access, USB signing, real upload, NuGet
repository countersigning, indexing and optional symbol ingestion. Neither
staging nor this workflow changes nuget.org account settings or creates keys
outside the short-lived OIDC exchange.
