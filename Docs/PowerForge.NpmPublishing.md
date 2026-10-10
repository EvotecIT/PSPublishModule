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

Required inputs are `artifact-name`, `package-name`, `version`, `sha256` and
`quality-run-id`. `quality-workflow` defaults to `quality.yml`. Pin the reusable
workflow to a reviewed commit and grant the caller `contents: read`, `actions:
read` and `id-token: write`.

The archive must contain `package/package.json` with the requested name/version
and `repository.url` equal to `git+https://github.com/<owner>/<repository>.git` for
the calling repository. The publisher checks the downloaded SHA256, publishes
that archive with lifecycle scripts disabled, then verifies the public SHA512
integrity and provenance. It does not change versions, commit files, create tags,
publish GitHub releases or dispatch downstream repositories.

## npm setup

Configure the trusted publisher for the package on npm with the calling GitHub
organization, repository and **calling workflow filename**. Do not register
`powerforge-npm-publish.yml` as the calling workflow. Permit direct `npm publish`
when that is the intended release mode. GitHub OIDC supplies the short-lived
publishing identity; this workflow declares no npm token secret.

The package must already exist before npm allows trusted-publisher configuration.
For a new package, the first qualified publication therefore needs an authenticated
package owner. Configure OIDC after that first publication, close to the next
release: a new trust configuration expires if it is not used within two days.
See [npm Trusted Publishing](https://docs.npmjs.com/trusted-publishers/) and
[npm trust prerequisites](https://docs.npmjs.com/cli/v11/commands/npm-trust/).

If publication succeeds but registry verification fails, inspect the public
version before dispatching again. npm versions are immutable; a verification
failure is not permission to publish a different archive under the same version.
