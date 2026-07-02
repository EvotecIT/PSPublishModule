# PowerForge.ConsoleShared

This folder contains Spectre.Console rendering helpers that are shared by the
PowerForge CLI and the PSPublishModule PowerShell surface.

The files are source-linked into both consumers instead of built as a separate
assembly because the two surfaces currently carry different Spectre.Console
package versions and different packaging/runtime constraints. Keep reusable
pipeline behavior in `PowerForge`; keep only console rendering, progress UI,
and human-facing summary formatting here.

If the CLI and PowerShell module later converge on the same console dependency
and packaging model, this folder can become a small project reference. Until
then, source linking is intentional and keeps console dependencies out of the
core engine.
