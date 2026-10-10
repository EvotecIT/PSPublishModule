# npm archive publication

The `powerforge-npm-publish.yml` reusable workflow publishes a package archive built
and qualified by the calling repository. It uses npm Trusted Publishing on a
GitHub-hosted Ubuntu runner. Consumers keep their own build and package contracts;
the shared workflow owns archive identity, publication and registry verification.

The caller uploads one `.tgz` as a workflow artifact and passes its package name,
three-part version and SHA256. It also supplies a successful `push` run of its
quality workflow for the exact default-branch commit being released. Publication
requires a deliberate `workflow_dispatch` on that branch. `publish` defaults to
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

## npm setup

Configure the trusted publisher for the package on npm with the calling GitHub
organization, repository, **calling workflow filename** and release environment.
Do not register `powerforge-npm-publish.yml` as the calling workflow. Permit direct `npm publish`
when that is the intended release mode. GitHub OIDC supplies the short-lived
publishing identity for ordinary releases without a stored npm token.

Create the GitHub release environment before using the workflow. Configure its
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

The allowed branch also needs a trusted update boundary. Use environment-required
reviewers where the GitHub plan supports them, or restrict default-branch updates
to release maintainers. An environment name in YAML alone provides no protection.

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
