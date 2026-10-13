# npm release version reservations

Use this action after a successful default-branch push quality run to select a
three-part version for an npm release. It reads the checked-out package manifest,
public npm versions and repository release tags. It does not modify source files
or push to the default branch.

The manifest version is a minimum. If a published or reserved version reaches that
minimum, the action selects the next patch above the highest known version. An
explicit new version must exceed that highest version. Raising the manifest minimum
starts a new minor or major line.

With `reserve: true`, the action atomically creates a lightweight `npm-v` tag at
the qualified source commit. This namespace belongs to the npm release workflow.
Retries reuse the source commit's existing reservation. A reservation is not a
successful publication; only the publisher's registry verification proves that.
No reservation is created for a dry run. Network failures and tag collisions fail
before publication instead of silently choosing another version.

Provide `GH_TOKEN` with Actions read permission and Contents write permission for
reservations, and `DEFAULT_BRANCH` from the repository event. Inputs are
`package-json`, `quality-run-id`, optional `quality-workflow` (default `quality.yml`),
optional `version`, `tag-prefix` (default `npm-v`) and `reserve` (default `false`).
Outputs are `version` and `tag`.

The shared `powerforge-npm-publish.yml` accepts an explicit default-branch dispatch
or the completed quality run's `workflow_run` event. Both paths require the same
successful push run and exact source commit. The protected environment still
allows only the exact default branch. A repeated upload is skipped only when the
existing public version has the qualified archive's SHA512 and required provenance.
An existing version with different bytes is an error.
