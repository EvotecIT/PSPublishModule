# Publish an agent plugin branch

This action publishes a PowerForge plugin ZIP to a small generated branch in the caller's repository. Use it for directories that validate an entire GitHub archive and cannot scan a large product repository. The product repository owns the canonical plugin; the branch contains its released payload at the root.

Run after the release archive and its checksum are qualified, with `contents: write`. Pass `archive`, `checksum`, and `github-token`; the branch defaults to `agent-plugin`. Pin the action to an immutable PowerForge commit. The action uses the pinned repository SDK, Git, GitHub CLI and PowerForge's existing package validator.

The branch keeps previous publication commits, rejects version rollback and changed same-version payloads, and uses an ordinary fast-forward push. Concurrent publication fails instead of replacing another result. Stable three-part plugin versions are required. Do not edit this generated branch manually or use it for product source.
