# Private capture for public Apple applications

Keep public pull-request CI on hosted runners. A private release controller can
capture an application's qualified merged source through PowerForge without
registering its Mac with the public application repository. Signing and App Store
operations remain local; the capture job does not receive Apple credentials.

This route separates the private workflow commit from the application commit.
Both identities remain in the retained screenshot provenance. The pinned local
operator checks the controller enrollment, successful GitHub run and private
runner-group assignment, exact application qualification, retained provenance
bytes, and approved PNG inventory before publication.

## Configure the controller

Copy `Build/ApplePrivateCapture.yml.template` into a private controller repository
as `.github/workflows/capture.yml`. Replace its placeholders with the reviewed
PowerForge commit, public application repository, qualification workflow and exact
required job names, product capture paths, owned simulator UUIDs, and qualified
runtime. Keep those settings in the reviewed workflow. Dispatch inputs select
only the exact application commit and marketing version.

Use the same reviewed PowerForge commit for the reusable workflow and
`simulator_tool_ref`. The hosted preflight verifies the actual referenced workflow
pin. It rejects public controllers, non-default-branch dispatches, non-maintainer
requests or reruns, unmerged source, and unsuccessful exact-source qualification.
The native job depends on that preflight. A SHA alone identifies bytes; it does not
establish that they were reviewed or merged.

The source must belong to its enrolled `main` or `master` history. Its latest
exact-source push qualification must pass every enrolled native job in the latest
attempt. Skipped jobs, a failed API read and incomplete job pagination reject the
capture. An older qualified ancestor can be captured without changing the
application's default branch.

## Establish the machine boundary

Use a dedicated, clean macOS VM or build host with no personal files, private
development checkouts, general-purpose credentials or signing profile. Qualify
Xcode, the capture account's existing UI access and owned simulators in that
environment before enabling the job. A separate directory, custom label or
ephemeral runner registration does not isolate the host operating system.

Enroll the runner in an organization group restricted to the private controller
repository and the exact reusable capture workflow ref. Public repositories must
be excluded. The native job accepts an explicit group and labels; the private
route rejects the Default group. Verify actual group access and a real job's
assignment, rather than inferring isolation from labels. Keep this capture pool
separate from signing and from ordinary public PR execution.

Existing shared Mac runners require their own access review. Moving an existing
runner can affect other applications; preserve their approved private workflow
access and wait for active jobs to finish. Restrict reusable workflow **callee**
refs as well as the permitted calling repositories. A private controller does not
remove an existing runner's access from another public-enabled group.

## Enroll retained evidence locally

Copy `Build/ApplePrivateCapturePolicy.json.template` to trusted local configuration
outside the application and PowerForge checkouts. Fill in the controller's exact
reviewed commit, workflow and branch, the same owner pin, private runner group,
application repository/default branch and qualification requirements. The
controller commit is separate from the application source commit. Review and
update this enrollment when the controller workflow changes.

Pass its absolute path to the existing pinned local operator:

```text
pwsh -NoProfile -File <owner>/scripts/Invoke-PinnedPowerForge.ps1
  -ExpectedCommit <reviewed-owner-commit>
  -ConsumerRoot <application-checkout>
  -ExpectedConsumerRepository example/public-app
  -ExpectedConsumerBranch master
  -CapturePolicyPath <private-local-capture-policy.json>
  apple-screenshots manifests --release-config powerforge.release.json
  --capture-provenance <downloaded-provenance.json>
  --expected-repository example/private-release-control
  --expected-workflow-ref example/private-release-control/.github/workflows/capture.yml@refs/heads/main
  --approved-by <human-approver> --allowed-root <reviewed-png-directory>
```

Join the example's lines for the shell in use. Review the exact PNG bytes before
creating approval manifests. Retain the capture workflow artifacts and guarded
simulator shutdown receipts; the private route keeps the existing schema 2 PNG
inventory and per-file hashes. It does not synthesize provenance from local
preview files or bypass source-bound screenshot publication.

Same-repository captures retain their existing contract when
`source_repository` is blank and no private capture policy is enrolled. Private
capture support does not register a runner, create a controller repository,
change access, activate a local worker or approve screenshots automatically.
