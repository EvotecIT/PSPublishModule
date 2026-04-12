---
title: "Run a PowerForge.Web quality pipeline"
description: "Run a CI-style PowerForge.Web pipeline for a website."
layout: docs
---

This pattern is useful when you want the same checks locally that CI should enforce for a website.

It is adapted from `Docs/PowerForge.Web.WebsiteStarter.md`.

## Example

```powershell
$env:POWERFORGE_ROOT = 'C:\Support\GitHub\PSPublishModule'

Set-Location 'C:\Support\GitHub\Website'

powerforge-web pipeline --config .\pipeline.json --mode ci
```

## What this demonstrates

- making the engine root explicit
- running the site pipeline in CI mode
- letting baselines and fail-on-new-issue policy catch regressions that a preview server may not report

## Source

- [PowerForge.Web.WebsiteStarter.md](https://github.com/EvotecIT/PSPublishModule/blob/master/Docs/PowerForge.Web.WebsiteStarter.md)

