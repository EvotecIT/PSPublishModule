# npm archive publication

The `powerforge-npm-publish.yml` reusable workflow publishes a package archive built
and qualified by the calling repository. It uses npm Trusted Publishing on a
GitHub-hosted Ubuntu runner. Consumers keep their own build and package contracts;
the shared workflow owns archive identity, publication and registry verification.

The caller uploads one `.tgz` as a workflow artifact and passes its package name,
three-part version and SHA256. It also supplies a successful `push` run of its
quality workflow for the exact default-branch commit being released. Publication
accepts a `workflow_dispatch` on that branch or a completed `workflow_run` event
for the same qualifying run. `publish` defaults to
`false`, which validates the archive and runs `npm publish --dry-run`.

Required inputs are `artifact-name`, `package-name`, `version`, `sha256`,
`quality-run-id` and `environment-name`. `quality-workflow` defaults to
`quality.yml`. Pin the reusable workflow to a reviewed commit and grant the
caller `contents: read`, `actions:
read` and `id-token: write`.

The archive must contain `package/package.json` with the requested name/version
and `repository.url` equal to `git+https://github.com/<owner>/<repository>.git` for
the calling repository. The publisher checks the downloaded SHA256, publishes
that archive with lifecycle scripts disabled, then verifies the public SHA512
integrity. Public source repositories also require npm provenance; private source
repositories explicitly disable provenance because npm does not support it for
that source visibility. The release summary records the applied requirement.
The workflow does not change versions, commit files, create tags,
publish GitHub releases or dispatch downstream repositories.

## Join a product release

Set `normal-release-tag-prefix` when npm belongs to an existing product release,
for example `OfficeIMO-v`. The caller runs on `release: published`, or dispatches
on that same tag to retry. The shared publisher requires the live published
release to be stable, its tag to resolve to the workflow commit, and that commit
to belong to the default branch with a successful push quality run. Semantic
tags must match the npm version; legacy fourteen-digit timestamp tags use the
version supplied with the qualified archive. Annotated tags are resolved before
checking the source identity.

Use a separate protected environment whose only deployment policy is a tag
pattern consisting of that prefix and `*`. For `OfficeIMO-v`, the exact policy is
type `tag`, name `OfficeIMO-v*`. Extra branch or tag policies are rejected. The
default mode continues to require only the exact default branch. A branch
dispatch cannot use product-tag mode, and a release event cannot enter the
default mode.

The caller owns its product-specific version bridge and package qualification.
It may read a version from an immutable artifact in the product release and stamp
the runner's package metadata before packing. This does not require committing
publisher-owned version changes before an existing native release. A separate npm
workflow is a delivery stage with its own retry and registry checks; it does not
join a native publisher's local checkpoint or make the registries transactional.
Leave `release-tag-prefix` empty when the product tag owns the release identity;
no additional npm reservation tag or GitHub release is needed.

## npm setup

For automatic releases, call the shared `npm-release-version` action after the
successful default-branch Quality run. It selects a patch version above public
and reserved versions, using the package manifest as a minimum, and atomically
reserves an `npm-v` tag for the source commit. It never pushes to the default
branch. Retrying the commit reuses that reservation. See the
[action inputs and version rules](../.github/actions/npm-release-version/README.md).
Serialize version selection, packaging and publication in the caller workflow
with `cancel-in-progress: false` and `queue: max`. Pass `release-tag-prefix: npm-v`
to the publisher. It rechecks reservations immediately before a new upload so a
newer commit's pending reservation also prevents an older release becoming latest.
The caller sets the selected package version, qualifies the archive, and calls
the publisher. This supports normal checked PR merges without an administrator
exception or a follow-up version-only PR.

Before uploading an ordinary OIDC release, the publisher checks whether that
version is already public. Matching archive SHA512 and required provenance let
a retry finish without another upload. Conflicting bytes or an unavailable
registry stop the release.
For public source repositories, a new publication requires the validated package
source SHA to match the workflow run's GitHub OIDC source identity. If a later
merge advances that identity, the older upload stops; the newer commit's successful
quality run qualifies its own archive. The workflow never changes `GITHUB_SHA` to make a
provenance statement disagree with the identity GitHub actually signs.
Registry readback checks the provenance dependency and archive digest, rather than
accepting an attestation merely because one exists.

Configure the trusted publisher for the package on npm with the calling GitHub
organization, repository, **calling workflow filename** and release environment.
Do not register `powerforge-npm-publish.yml` as the calling workflow. Permit direct `npm publish`
when that is the intended release mode. GitHub OIDC supplies the short-lived
publishing identity for ordinary releases without a stored npm token.

Create the GitHub release environment before using the workflow. In default mode, configure its
custom deployment policy to allow exactly the default branch, with no tags or
wildcard patterns. The publisher verifies that policy before processing the
archive. Store `NPM_BOOTSTRAP_TOKEN` only as a secret of that environment; do not
create a repository-level copy. The caller must forward its exact name:

```yaml
secrets:
  NPM_BOOTSTRAP_TOKEN: ${{ secrets.NPM_BOOTSTRAP_TOKEN }}
```

GitHub supplies the called job's environment value through this mapping, even
when the secret exists only in the environment. The required declaration checks
forwarding presence; the publisher also checks for a value when bootstrap is
selected. Outside bootstrap, the secret can be empty and is not used.
Bind the same environment name in npm's
trust settings so a branch cannot replace the caller and reuse its publishing
identity.

Protect the allowed branch with the repository's normal pull-request and check
requirements. Use required reviews where repository policy calls for them.
Administrators do not need to be the only people allowed to merge. The npm
environment prevents deployments from other branches; it does not replace the
repository's source review policy.

The package must already exist before npm allows trusted-publisher configuration.
For a new package, the first qualified publication uses `publish: true`,
`bootstrap: true` and the environment `NPM_BOOTSTRAP_TOKEN` secret. A package owner
creates a short-lived granular token with package-write scope and bypass 2FA for
unattended publication, then supplies it through the caller's GitHub environment
secret settings. The workflow loads it only for that explicit first-publication
mode and requires an authenticated package lookup to return 404. Existing public
or private packages, registry failures and missing credentials fail the bootstrap
check.

After the first publication, remove the bootstrap secret and leave `bootstrap`
false. Configure OIDC close to the next release: a new trust configuration expires
if it is not used within two days. This keeps credential creation and registry
authorization with the package owner while both release modes upload through
GitHub Actions.
See [npm Trusted Publishing](https://docs.npmjs.com/trusted-publishers/) and
[npm trust prerequisites](https://docs.npmjs.com/cli/v11/commands/npm-trust/).

If publication succeeds but registry verification fails, inspect the public
version before dispatching again. npm versions are immutable; a verification
failure is not permission to publish a different archive under the same version.
