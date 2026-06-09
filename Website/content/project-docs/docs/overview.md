---
title: "PSPublishModule Overview"
description: "How PSPublishModule and PowerForge fit module, library, and website workflows."
layout: docs
---

PSPublishModule is used to keep build, packaging, documentation, and release workflows repeatable across Evotec PowerShell modules and related .NET libraries.

## Common fit

- build and package PowerShell modules
- generate PowerShell command help from authored XML docs and about-topic sources
- build .NET libraries and release artifacts through PowerForge project workflows
- build and verify PowerForge.Web websites with explicit quality gates

## Good operating pattern

Keep reusable build behavior in PowerForge services and keep cmdlets focused on PowerShell-facing orchestration. For websites, prefer explicit `site.json`, `pipeline.json`, baselines, and CI gates over preview-only checks.

