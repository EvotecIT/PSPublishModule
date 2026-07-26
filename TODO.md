# TODO

## PSPublishModule and PowerForge

- [ ] Split the CLI NativeAOT-safe paths from PowerShell-SDK-dependent services.
- [ ] Add contract-focused tests around shared service boundaries and CLI integration.
- [ ] Regenerate generated command docs after managed-module command surfaces settle.
- [ ] Regenerate external help after managed-module command surfaces settle.
- [ ] Split `PowerForge.ConsoleShared\SpectrePipelineSummaryWriter.cs` by rendering responsibility.

## PowerForge.Web

- [ ] Add API docs namespace and type filters.
- [ ] Add API docs search UX.
- [ ] Add API docs member grouping for methods, properties, fields, and events.
- [ ] Render remaining XML documentation tags beyond summary and remarks.
- [ ] Add optional per-type API docs mini table-of-contents.
- [ ] Add optional API docs type hierarchy output.
- [ ] Fix Markdown parser precedence so `## Heading: Text` is treated as a heading.
- [ ] Verify Prism injection for Markdown code blocks and API pages.
- [ ] Auto-include Prism only when code blocks are present.
- [ ] Add reusable light and dark Prism theme override support.
- [ ] Validate theme layout hooks such as `extra_css_html` and `extra_scripts_html`.
- [ ] Keep generated site navigation parity checks across pages.
- [ ] Verify edit and source links resolve to correct files.
- [ ] Auto-generate docs navigation from `docs/` by default.
- [ ] Add manual docs navigation ordering.
- [ ] Warn on orphaned docs pages.
- [ ] Choose one canonical API docs URL scheme.
- [ ] Make the dev server handle the canonical API docs URL scheme without download behavior.
- [ ] Improve sitemap generation from site output.
- [ ] Add manual sitemap overrides.
- [ ] Add sitemap priority support.
- [ ] Warn on duplicate sitemap entries.
- [ ] Warn on invalid sitemap paths.
- [ ] Normalize default layout background and spacing across docs, home, and API pages.
- [ ] Add redirect-assistant workflow for route diffs and suggested 301 maps.
- [ ] Add asset policy examples for local, CDN, and hybrid hosting.
- [ ] Add asset rewrite and hashing examples.
- [ ] Add cache-header defaults for Netlify.
- [ ] Add cache-header defaults for Cloudflare Pages.
- [ ] Add config-driven Playwright smoke checks.
- [ ] Add configured audit steps to sample pipelines.
- [ ] Add configured audit steps to scaffolder defaults.
- [ ] Add content diagnostics for missing syntax highlighter assets.
- [ ] Add content diagnostics for missing theme files.
- [ ] Add content diagnostics for invalid pipeline entries.
- [ ] Finish server recovery inspect command.
- [ ] Finish server recovery capture command.
- [ ] Finish server recovery bootstrap command.
- [ ] Finish server recovery restore-secrets command.
- [ ] Finish server recovery deploy command.
- [ ] Finish server recovery verify command.
- [ ] Refresh theme anatomy documentation.
- [ ] Refresh Prism local asset documentation.
- [ ] Refresh docs navigation rules documentation.
- [ ] Refresh relative link documentation.
- [ ] Refresh sitemap behavior documentation.

## DotNet Publish and Managed Modules

- [ ] Review `Docs/PSPublishModule.DotNetPublish.ImplementationPlan.md` and promote concrete root release blockers into this file.
- [ ] Complete managed-module Authenticode catalog, timestamp, and short-lived certificate-chain proof with signed fixtures.
- [ ] Refresh managed-module release-candidate benchmark evidence and regenerate the public README tables.
